using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PauseTracker;

/// <summary>
/// Injects the overlay script tag into jellyfin-web's index.html at startup,
/// the same approach Intro Skipper uses. The src is relative ("../") so it
/// resolves correctly under a reverse-proxy base path: index.html is served
/// from {base}/web/, the API from {base}/.
/// </summary>
public class ScriptInjectionService : IHostedService
{
    private const string Marker = "PauseTracker/ClientScript";
    private const string ScriptTag =
        "<script plugin=\"PauseTracker\" defer src=\"../PauseTracker/ClientScript\"></script>";

    private readonly IApplicationPaths _appPaths;
    private readonly ILogger<ScriptInjectionService> _logger;

    public ScriptInjectionService(IApplicationPaths appPaths, ILogger<ScriptInjectionService> logger)
    {
        _appPaths = appPaths;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.InjectClientScript == false)
        {
            _logger.LogInformation("Pause Tracker: client script injection disabled in plugin settings");
            return Task.CompletedTask;
        }

        try
        {
            var indexPath = Path.Combine(_appPaths.WebPath, "index.html");
            if (!File.Exists(indexPath))
            {
                _logger.LogWarning(
                    "Pause Tracker: {Path} not found; add the overlay script to your web client manually",
                    indexPath);
                return Task.CompletedTask;
            }

            var html = File.ReadAllText(indexPath);
            if (html.Contains(Marker, StringComparison.Ordinal))
            {
                _logger.LogDebug("Pause Tracker: overlay script already injected");
                return Task.CompletedTask;
            }

            var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyClose < 0)
            {
                _logger.LogWarning("Pause Tracker: could not find </body> in index.html; not injecting");
                return Task.CompletedTask;
            }

            html = html.Insert(bodyClose, ScriptTag);
            File.WriteAllText(indexPath, html);
            _logger.LogInformation("Pause Tracker: overlay script injected into {Path}", indexPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                ex,
                "Pause Tracker: could not inject the overlay script (web folder not writable?). "
                + "Add this line to index.html before </body> manually: {Tag}",
                ScriptTag);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
