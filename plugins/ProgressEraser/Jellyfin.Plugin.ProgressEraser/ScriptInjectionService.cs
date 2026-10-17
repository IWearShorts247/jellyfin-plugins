using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProgressEraser;

/// <summary>
/// Injects the overlay script tag into jellyfin-web's index.html at startup.
/// Relative src ("../") so it resolves under reverse-proxy base paths.
/// </summary>
public class ScriptInjectionService : IHostedService
{
    private const string Marker = "ProgressEraser/ClientScript";
    private const string ScriptTag =
        "<script plugin=\"ProgressEraser\" defer src=\"../ProgressEraser/ClientScript\"></script>";

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
            return Task.CompletedTask;
        }

        try
        {
            var indexPath = Path.Combine(_appPaths.WebPath, "index.html");
            if (!File.Exists(indexPath))
            {
                _logger.LogWarning("ProgressEraser: {Path} not found; add the overlay script manually", indexPath);
                return Task.CompletedTask;
            }

            var html = File.ReadAllText(indexPath);
            if (html.Contains(Marker, StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyClose < 0)
            {
                _logger.LogWarning("ProgressEraser: could not find </body> in index.html; not injecting");
                return Task.CompletedTask;
            }

            File.WriteAllText(indexPath, html.Insert(bodyClose, ScriptTag));
            _logger.LogInformation("ProgressEraser: overlay script injected into {Path}", indexPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                ex,
                "ProgressEraser: could not inject the overlay script. Add this to index.html before </body> manually: {Tag}",
                ScriptTag);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
