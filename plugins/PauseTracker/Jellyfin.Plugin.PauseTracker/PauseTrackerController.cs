using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.PauseTracker;

[ApiController]
[Route("PauseTracker")]
public class PauseTrackerController : ControllerBase
{
    private readonly PauseTrackingService _trackingService;

    public PauseTrackerController(PauseTrackingService trackingService)
    {
        _trackingService = trackingService;
    }

    /// <summary>
    /// Gets pause statistics for the caller's active playback, looked up by
    /// device id and/or play session id.
    /// </summary>
    [HttpGet("Stats")]
    [Authorize]
    public ActionResult<PauseStatsDto> GetStats([FromQuery] string? deviceId, [FromQuery] string? playSessionId)
    {
        if (deviceId is null && playSessionId is null)
        {
            return BadRequest("deviceId or playSessionId is required");
        }

        var stats = _trackingService.GetStats(deviceId, playSessionId);
        if (stats is null)
        {
            return NotFound();
        }

        return stats;
    }

    /// <summary>
    /// Serves the overlay script that gets injected into jellyfin-web.
    /// Anonymous because index.html loads it before the user signs in.
    /// </summary>
    [HttpGet("ClientScript")]
    [AllowAnonymous]
    public ActionResult GetClientScript()
    {
        using var stream = typeof(PauseTrackerController).Assembly
            .GetManifestResourceStream("Jellyfin.Plugin.PauseTracker.Web.pausetracker.js");
        if (stream is null)
        {
            return NotFound();
        }

        using var reader = new StreamReader(stream);
        var js = reader.ReadToEnd();

        // Bake the current plugin configuration into the script (JSON contains
        // no single quotes, so it is safe inside the JS string literal).
        var configJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            ShowExitSummary = Plugin.Instance?.Configuration.ShowExitSummary ?? true
        });
        js = js.Replace("__PAUSETRACKER_CONFIG__", configJson, StringComparison.Ordinal);

        // The script bakes in config and changes between plugin versions —
        // make sure browsers revalidate instead of serving a stale copy.
        Response.Headers.CacheControl = "no-cache";

        return Content(js, "application/javascript");
    }
}
