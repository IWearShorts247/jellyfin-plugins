using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.DoesTheDogDie;

[ApiController]
[Route("DoesTheDogDie")]
public class DddController : ControllerBase
{
    private readonly ISessionManager _sessionManager;
    private readonly ILibraryManager _libraryManager;
    private readonly DddClient _dddClient;

    public DddController(ISessionManager sessionManager, ILibraryManager libraryManager, DddClient dddClient)
    {
        _sessionManager = sessionManager;
        _libraryManager = libraryManager;
        _dddClient = dddClient;
    }

    /// <summary>
    /// Returns warnings for whatever the given device is currently playing.
    /// Episodes and seasons resolve to their series, since DoesTheDogDie
    /// rates shows as a whole.
    /// </summary>
    [HttpGet("Current")]
    [Authorize]
    public async Task<ActionResult<CurrentWarningsDto>> GetCurrent([FromQuery] string deviceId, CancellationToken cancellationToken)
    {
        var session = _sessionManager.Sessions
            .FirstOrDefault(s => string.Equals(s.DeviceId, deviceId, StringComparison.Ordinal)
                && s.NowPlayingItem is not null);
        var nowPlaying = session?.NowPlayingItem;
        if (nowPlaying is null)
        {
            return NotFound();
        }

        string title;
        int? year;
        if (nowPlaying.Type is BaseItemKind.Episode or BaseItemKind.Season && !string.IsNullOrEmpty(nowPlaying.SeriesName))
        {
            title = nowPlaying.SeriesName;
            year = nowPlaying.SeriesId.HasValue
                ? _libraryManager.GetItemById(nowPlaying.SeriesId.Value)?.ProductionYear
                : null;
        }
        else
        {
            title = nowPlaying.Name;
            year = nowPlaying.ProductionYear;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return NotFound();
        }

        var result = await _dddClient.GetWarningsAsync(title, year, cancellationToken).ConfigureAwait(false);
        return new CurrentWarningsDto
        {
            ItemId = nowPlaying.Id.ToString("N"),
            ItemName = nowPlaying.Name,
            Lookup = result
        };
    }

    /// <summary>Manual lookup, used by the config page's test button.</summary>
    [HttpGet("Lookup")]
    [Authorize]
    public async Task<ActionResult<WarningsDto>> Lookup([FromQuery] string title, [FromQuery] int? year, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Plugin.Instance?.Configuration.ApiKey))
        {
            return Problem("No DoesTheDogDie API key configured.", statusCode: 503);
        }

        var result = await _dddClient.GetWarningsAsync(title, year, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound();
        }

        return result;
    }

    /// <summary>
    /// Serves the overlay script. Anonymous because index.html loads it before
    /// the user signs in.
    /// </summary>
    [HttpGet("ClientScript")]
    [AllowAnonymous]
    public ActionResult GetClientScript()
    {
        using var stream = typeof(DddController).Assembly
            .GetManifestResourceStream("Jellyfin.Plugin.DoesTheDogDie.Web.ddtd.js");
        if (stream is null)
        {
            return NotFound();
        }

        using var reader = new StreamReader(stream);
        var js = reader.ReadToEnd();
        var configJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            OverlaySeconds = Plugin.Instance?.Configuration.OverlaySeconds ?? 12
        });
        js = js.Replace("__DDTD_CONFIG__", configJson, StringComparison.Ordinal);

        Response.Headers.CacheControl = "no-cache";
        return Content(js, "application/javascript");
    }
}

public class CurrentWarningsDto
{
    public string ItemId { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public WarningsDto? Lookup { get; set; }
}
