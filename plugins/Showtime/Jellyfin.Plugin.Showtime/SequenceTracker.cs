using System.Collections.Concurrent;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Showtime;

/// <summary>
/// Remembers the sequence handed to each viewer and fires step actions as its clips start playing.
/// </summary>
public sealed class SequenceTracker : IHostedService
{
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromHours(8);

    private readonly ISessionManager _sessionManager;
    private readonly ActionRunner _actions;
    private readonly ILogger<SequenceTracker> _logger;
    private readonly ConcurrentDictionary<Guid, TrackedPlan> _plans = new();

    public SequenceTracker(ISessionManager sessionManager, ActionRunner actions, ILogger<SequenceTracker> logger)
    {
        _sessionManager = sessionManager;
        _actions = actions;
        _logger = logger;
    }

    public void Register(SequencePlan plan)
    {
        _plans[plan.User.Id] = new TrackedPlan(plan, DateTime.UtcNow);
        foreach (var stale in _plans.Where(p => DateTime.UtcNow - p.Value.CreatedUtc > PlanLifetime).Select(p => p.Key).ToList())
        {
            _plans.TryRemove(stale, out _);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        return Task.CompletedTask;
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        foreach (var tracked in PlansFor(e))
        {
            var plan = tracked.Plan;
            if (e.Item.Id == plan.Feature.Id)
            {
                Fire(tracked, "feature", plan.FeatureAction, "Feature", e);
                continue;
            }

            var clip = plan.Clips.FirstOrDefault(c => c.Item.Id == e.Item.Id);
            if (clip is not null)
            {
                // Several clips can share a step (e.g. two trailers); fire once per step.
                Fire(tracked, "step" + clip.StepIndex, clip.Action, clip.Label, e);
            }
        }
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        foreach (var tracked in PlansFor(e))
        {
            if (e.Item.Id == tracked.Plan.Feature.Id)
            {
                Fire(tracked, "end", tracked.Plan.FeatureEndAction, "Feature end", e);
                _plans.TryRemove(tracked.Plan.User.Id, out _);
            }
        }
    }

    private IEnumerable<TrackedPlan> PlansFor(PlaybackProgressEventArgs e)
    {
        if (e.Item is null || e.Users is null)
        {
            return Enumerable.Empty<TrackedPlan>();
        }

        return e.Users
            .Select(u => _plans.TryGetValue(u.Id, out var p) ? p : null)
            .OfType<TrackedPlan>()
            .ToList();
    }

    private void Fire(TrackedPlan tracked, string key, string actionName, string label, PlaybackProgressEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(actionName) || !tracked.Fired.TryAdd(key, true))
        {
            return;
        }

        _logger.LogInformation("Showtime: {Label} started, firing action \"{Action}\"", label, actionName);
        _ = _actions.RunAsync(actionName, new ActionContext(tracked.Plan.Feature, tracked.Plan.User.Username, label, e.DeviceName));
    }

    private sealed record TrackedPlan(SequencePlan Plan, DateTime CreatedUtc)
    {
        public ConcurrentDictionary<string, bool> Fired { get; } = new();
    }
}
