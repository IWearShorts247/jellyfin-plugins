using System.Text.Json;
using System.Text.Json.Nodes;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Jellyfin.Plugin.ProgressEraser;

/// <summary>
/// Puts <see cref="NextUpFilterMiddleware"/> at the front of the server's
/// request pipeline. Plugins cannot touch Startup.Configure, but any
/// IStartupFilter in the service collection is honoured by the host.
/// </summary>
public class NextUpStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<NextUpFilterMiddleware>();
            next(app);
        };
    }
}

/// <summary>
/// Removes hidden shows from GET /Shows/NextUp responses. Runs for every
/// client, not just the web UI. On any problem it falls back to passing the
/// original response through untouched.
/// </summary>
public class NextUpFilterMiddleware
{
    private readonly RequestDelegate _next;

    public NextUpFilterMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        NextUpHider hider,
        IAuthorizationContext authContext,
        ILogger<NextUpFilterMiddleware> logger)
    {
        if (!IsNextUpRequest(context.Request) || Plugin.Instance?.Configuration.FilterNextUp != true)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var userId = await ResolveUserIdAsync(context, authContext, logger).ConfigureAwait(false);
        var hidden = userId == Guid.Empty ? new HashSet<Guid>() : hider.HiddenIdsFor(userId);
        if (hidden.Count == 0)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Ask the server for extra rows to backfill the ones we drop, and keep
        // the body uncompressed so it can be edited.
        var originalLimit = RaiseLimit(context.Request, hidden.Count);
        context.Request.Headers.Remove("Accept-Encoding");

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);

            buffer.Position = 0;
            var isJson = context.Response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
            if (context.Response.StatusCode == StatusCodes.Status200OK && isJson && buffer.Length > 0)
            {
                var filtered = Filter(buffer, hidden, originalLimit, out var removed);
                if (filtered is not null)
                {
                    logger.LogDebug("Progress Eraser: removed {Count} hidden show(s) from Next Up for user {UserId}", removed, userId);
                    context.Response.ContentLength = filtered.Length;
                    await originalBody.WriteAsync(filtered, context.RequestAborted).ConfigureAwait(false);
                    return;
                }

                buffer.Position = 0;
            }

            await buffer.CopyToAsync(originalBody, context.RequestAborted).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static bool IsNextUpRequest(HttpRequest request)
    {
        return HttpMethods.IsGet(request.Method)
            && request.Path.Value?.EndsWith("/Shows/NextUp", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static async Task<Guid> ResolveUserIdAsync(HttpContext context, IAuthorizationContext authContext, ILogger logger)
    {
        try
        {
            // Admins may query another user's Next Up; honour that like the API does.
            foreach (var (key, value) in context.Request.Query)
            {
                if (string.Equals(key, "userId", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(value, out var queried))
                {
                    return queried;
                }
            }

            var auth = await authContext.GetAuthorizationInfo(context).ConfigureAwait(false);
            return auth.UserId;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Progress Eraser: could not resolve the user for a Next Up request");
            return Guid.Empty;
        }
    }

    /// <summary>Bumps the limit query parameter; returns the original limit, if any.</summary>
    private static int? RaiseLimit(HttpRequest request, int extra)
    {
        var query = QueryHelpers.ParseQuery(request.QueryString.Value);
        var key = query.Keys.FirstOrDefault(k => string.Equals(k, "limit", StringComparison.OrdinalIgnoreCase));
        if (key is null || !int.TryParse(query[key], out var limit) || limit <= 0)
        {
            return null;
        }

        query[key] = new StringValues((limit + extra).ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.QueryString = QueryString.Create(query);
        return limit;
    }

    private static byte[]? Filter(Stream body, HashSet<Guid> hidden, int? originalLimit, out int removed)
    {
        removed = 0;
        try
        {
            var node = JsonNode.Parse(body);
            if (node is not JsonObject obj || obj["Items"] is not JsonArray items)
            {
                return null;
            }

            for (var i = items.Count - 1; i >= 0; i--)
            {
                var seriesId = items[i]?["SeriesId"]?.GetValue<string>();
                if (seriesId is not null && Guid.TryParse(seriesId, out var guid) && hidden.Contains(guid))
                {
                    items.RemoveAt(i);
                    removed++;
                }
            }

            if (originalLimit is { } limit)
            {
                while (items.Count > limit)
                {
                    items.RemoveAt(items.Count - 1);
                }
            }

            if (removed > 0 && obj["TotalRecordCount"] is JsonValue totalValue && totalValue.TryGetValue<int>(out var total))
            {
                obj["TotalRecordCount"] = Math.Max(0, total - removed);
            }

            return JsonSerializer.SerializeToUtf8Bytes(node);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
