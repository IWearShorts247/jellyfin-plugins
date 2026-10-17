using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Showtime;

/// <summary>
/// Hands Jellyfin the pre-show. Clients with cinema mode enabled play these before the feature.
/// </summary>
public class IntroProvider : IIntroProvider
{
    private readonly SequenceBuilder _builder;
    private readonly SequenceTracker _tracker;
    private readonly ILogger<IntroProvider> _logger;

    public IntroProvider(SequenceBuilder builder, SequenceTracker tracker, ILogger<IntroProvider> logger)
    {
        _builder = builder;
        _tracker = tracker;
        _logger = logger;
    }

    public string Name => "Showtime";

    public Task<IEnumerable<IntroInfo>> GetIntros(BaseItem item, User user)
    {
        var plan = _builder.Build(item, user);
        if (plan is null)
        {
            return Task.FromResult(Enumerable.Empty<IntroInfo>());
        }

        _tracker.Register(plan);
        _logger.LogInformation(
            "Showtime: \"{Sequence}\" for {Feature} ({User}): {Count} clips",
            plan.SequenceName,
            item.Name,
            user.Username,
            plan.Clips.Count);

        return Task.FromResult(plan.Clips.Select(c => new IntroInfo { ItemId = c.Item.Id }));
    }
}
