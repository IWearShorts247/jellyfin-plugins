using System.Text;
using Jellyfin.Plugin.Showtime.Configuration;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Showtime;

/// <summary>What an action's placeholders are filled from.</summary>
public sealed record ActionContext(BaseItem? Feature, string User, string Step, string? Device);

/// <summary>
/// Fires configured HTTP actions (Home Assistant webhooks, scripts, anything with a URL).
/// </summary>
public class ActionRunner
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ActionRunner> _logger;

    public ActionRunner(IHttpClientFactory httpClientFactory, ILogger<ActionRunner> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public static ActionDefinition? Find(string name)
        => Plugin.Instance?.Configuration.Actions.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Runs an action by name. Returns the HTTP status line, or the error.</summary>
    public async Task<string> RunAsync(string name, ActionContext context, bool skipDelay = false)
    {
        var action = Find(name);
        if (action is null)
        {
            _logger.LogWarning("Showtime: action \"{Name}\" is not defined", name);
            return $"No action named \"{name}\"";
        }

        try
        {
            if (action.DelaySeconds > 0 && !skipDelay)
            {
                await Task.Delay(TimeSpan.FromSeconds(action.DelaySeconds)).ConfigureAwait(false);
            }

            using var request = new HttpRequestMessage(new HttpMethod(string.IsNullOrWhiteSpace(action.Method) ? "POST" : action.Method.Trim().ToUpperInvariant()), Fill(action.Url, context, Uri.EscapeDataString));

            foreach (var line in action.Headers.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var colon = line.IndexOf(':');
                if (colon > 0)
                {
                    request.Headers.TryAddWithoutValidation(line[..colon].Trim(), line[(colon + 1)..].Trim());
                }
            }

            if (!string.IsNullOrWhiteSpace(action.Body) && request.Method != HttpMethod.Get)
            {
                var escape = action.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase) ? JsonEscape : (Func<string, string>)(s => s);
                request.Content = new StringContent(Fill(action.Body, context, escape), Encoding.UTF8, string.IsNullOrWhiteSpace(action.ContentType) ? "text/plain" : action.ContentType);
            }

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var response = await client.SendAsync(request).ConfigureAwait(false);
            var result = $"{(int)response.StatusCode} {response.ReasonPhrase}";
            _logger.LogInformation("Showtime: action \"{Name}\" → {Result}", name, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Showtime: action \"{Name}\" failed", name);
            return ex.Message;
        }
    }

    private static string Fill(string template, ActionContext c, Func<string, string> escape)
        => template
            .Replace("{feature}", escape(c.Feature?.Name ?? string.Empty), StringComparison.OrdinalIgnoreCase)
            .Replace("{rating}", escape(c.Feature?.OfficialRating ?? string.Empty), StringComparison.OrdinalIgnoreCase)
            .Replace("{user}", escape(c.User), StringComparison.OrdinalIgnoreCase)
            .Replace("{step}", escape(c.Step), StringComparison.OrdinalIgnoreCase)
            .Replace("{device}", escape(c.Device ?? string.Empty), StringComparison.OrdinalIgnoreCase);

    private static string JsonEscape(string s) => System.Text.Json.JsonEncodedText.Encode(s).ToString();
}
