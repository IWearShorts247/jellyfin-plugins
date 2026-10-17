using System.Collections.Concurrent;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PauseTracker;

/// <summary>
/// Listens to session playback events and tracks pause transitions per session.
/// Pause state arrives via progress reports (clients report immediately on
/// pause/unpause), so counting edges of <c>IsPaused</c> gives an accurate count
/// and near-accurate interval lengths.
/// </summary>
public class PauseTrackingService : IHostedService
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<PauseTrackingService> _logger;
    private readonly ConcurrentDictionary<string, SessionPauseState> _states = new();

    public PauseTrackingService(ISessionManager sessionManager, ILogger<PauseTrackingService> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _sessionManager.SessionEnded += OnSessionEnded;
        _logger.LogInformation("Pause Tracker started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _sessionManager.SessionEnded -= OnSessionEnded;
        return Task.CompletedTask;
    }

    private static bool IsUserDisabled(Guid userId)
    {
        var disabled = Plugin.Instance?.Configuration.DisabledUserIds;
        if (disabled is null || disabled.Length == 0 || userId == Guid.Empty)
        {
            return false;
        }

        foreach (var id in disabled)
        {
            if (Guid.TryParse(id, out var parsed) && parsed == userId)
            {
                return true;
            }
        }

        return false;
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        if (e.Session is null)
        {
            return;
        }

        if (IsUserDisabled(e.Session.UserId))
        {
            _states.TryRemove(e.Session.Id, out _);
            return;
        }

        _states[e.Session.Id] = new SessionPauseState
        {
            ItemId = e.Item?.Id ?? Guid.Empty,
            PlaySessionId = e.PlaySessionId,
            DeviceId = e.DeviceId
        };
    }

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        if (e.Session is null)
        {
            return;
        }

        if (IsUserDisabled(e.Session.UserId))
        {
            // Covers mid-playback config changes too: drop any existing state.
            _states.TryRemove(e.Session.Id, out _);
            return;
        }

        var itemId = e.Item?.Id ?? Guid.Empty;
        var state = _states.GetOrAdd(e.Session.Id, _ => new SessionPauseState
        {
            ItemId = itemId,
            PlaySessionId = e.PlaySessionId,
            DeviceId = e.DeviceId
        });

        lock (state.SyncRoot)
        {
            // New item (or replay of the same item) in the same session: start over.
            if (state.ItemId != itemId || state.PlaySessionId != e.PlaySessionId)
            {
                state.Reset(itemId, e.PlaySessionId, e.DeviceId);
            }

            var now = DateTime.UtcNow;
            if (e.IsPaused && state.PauseStartedUtc is null)
            {
                // Grace threshold: a pause that begins early in the media
                // (prerolls, waiting for everyone to sit down) is not counted.
                var thresholdTicks = (long)((Plugin.Instance?.Configuration.IgnorePausesBeforeMinutes ?? 3.0)
                    * TimeSpan.TicksPerMinute);
                var positionTicks = e.PlaybackPositionTicks ?? long.MaxValue;
                if (positionTicks >= thresholdTicks)
                {
                    state.PauseCount++;
                    state.PauseStartedUtc = now;
                }
            }
            else if (!e.IsPaused && state.PauseStartedUtc is { } started)
            {
                var lengthTicks = (now - started).Ticks;
                state.CompletedPausedTicks += lengthTicks;
                state.IntervalTicks.Add(lengthTicks);
                state.PauseStartedUtc = null;
            }
        }
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        if (e.Session is null)
        {
            return;
        }

        if (_states.TryRemove(e.Session.Id, out var state))
        {
            lock (state.SyncRoot)
            {
                var total = TimeSpan.FromTicks(state.CompletedPausedTicks
                    + (state.PauseStartedUtc is { } started ? (DateTime.UtcNow - started).Ticks : 0));
                _logger.LogDebug(
                    "Playback stopped for session {Session}: {Count} pauses, {Total} paused",
                    e.Session.Id,
                    state.PauseCount,
                    total);
            }
        }
    }

    private void OnSessionEnded(object? sender, SessionEventArgs e)
    {
        if (e.SessionInfo is not null)
        {
            _states.TryRemove(e.SessionInfo.Id, out _);
        }
    }

    /// <summary>
    /// Returns a live snapshot for the given device or play session, or null if
    /// nothing is being tracked. An in-progress pause is included in the total.
    /// </summary>
    public PauseStatsDto? GetStats(string? deviceId, string? playSessionId)
    {
        SessionPauseState? state = null;
        foreach (var candidate in _states.Values)
        {
            if (playSessionId is not null && candidate.PlaySessionId == playSessionId)
            {
                state = candidate;
                break;
            }

            if (deviceId is not null && candidate.DeviceId == deviceId)
            {
                state = candidate;
            }
        }

        if (state is null)
        {
            return null;
        }

        lock (state.SyncRoot)
        {
            var now = DateTime.UtcNow;
            var currentPauseTicks = state.PauseStartedUtc is { } started ? (now - started).Ticks : 0;
            return new PauseStatsDto
            {
                ItemId = state.ItemId == Guid.Empty ? null : state.ItemId.ToString("N"),
                PauseCount = state.PauseCount,
                IsPaused = state.PauseStartedUtc is not null,
                CurrentPauseMs = currentPauseTicks / TimeSpan.TicksPerMillisecond,
                TotalPausedMs = (state.CompletedPausedTicks + currentPauseTicks) / TimeSpan.TicksPerMillisecond,
                IntervalsMs = state.IntervalTicks.Select(t => t / TimeSpan.TicksPerMillisecond).ToArray()
            };
        }
    }

    private sealed class SessionPauseState
    {
        public object SyncRoot { get; } = new();

        public Guid ItemId { get; set; }

        public string? PlaySessionId { get; set; }

        public string? DeviceId { get; set; }

        public int PauseCount { get; set; }

        /// <summary>Sum of finished pause intervals, in TimeSpan ticks.</summary>
        public long CompletedPausedTicks { get; set; }

        /// <summary>Set while a pause is ongoing.</summary>
        public DateTime? PauseStartedUtc { get; set; }

        public List<long> IntervalTicks { get; } = new();

        public void Reset(Guid itemId, string? playSessionId, string? deviceId)
        {
            ItemId = itemId;
            PlaySessionId = playSessionId;
            DeviceId = deviceId;
            PauseCount = 0;
            CompletedPausedTicks = 0;
            PauseStartedUtc = null;
            IntervalTicks.Clear();
        }
    }
}
