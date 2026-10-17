using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProgressEraser;

/// <summary>
/// Puts a show back into Next Up when one of its episodes is played by a
/// user who had hidden it.
/// </summary>
public class NextUpPlaybackWatcher : IHostedService
{
    private readonly ISessionManager _sessionManager;
    private readonly NextUpHider _hider;
    private readonly ILogger<NextUpPlaybackWatcher> _logger;

    public NextUpPlaybackWatcher(ISessionManager sessionManager, NextUpHider hider, ILogger<NextUpPlaybackWatcher> logger)
    {
        _sessionManager = sessionManager;
        _hider = hider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        return Task.CompletedTask;
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        if (Plugin.Instance?.Configuration.UnhideOnPlayback != true || e.Item is not Episode episode || episode.SeriesId == Guid.Empty)
        {
            return;
        }

        var userIds = (e.Users ?? []).Select(u => u.Id).ToList();
        if (e.Session is not null && e.Session.UserId != Guid.Empty)
        {
            userIds.Add(e.Session.UserId);
        }

        foreach (var userId in userIds.Distinct())
        {
            if (_hider.Unhide(userId, episode.SeriesId))
            {
                _logger.LogInformation("Progress Eraser: {Series} is back in Next Up for user {UserId} after playback", episode.SeriesName, userId);
            }
        }
    }
}
