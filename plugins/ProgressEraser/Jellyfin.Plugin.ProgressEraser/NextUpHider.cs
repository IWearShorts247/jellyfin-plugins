using Jellyfin.Plugin.ProgressEraser.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.ProgressEraser;

/// <summary>
/// Per-user list of shows hidden from Next Up, stored in the plugin
/// configuration. Small lists, so a copy-on-write array is plenty.
/// </summary>
public class NextUpHider
{
    private readonly ILibraryManager _libraryManager;
    private readonly object _lock = new();

    public NextUpHider(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    public IReadOnlyList<HiddenSeries> All() => Plugin.Instance?.Configuration.HiddenNextUp ?? Array.Empty<HiddenSeries>();

    public IReadOnlyList<HiddenSeries> For(Guid userId) => All().Where(h => h.UserId == userId).ToList();

    public HashSet<Guid> HiddenIdsFor(Guid userId) => All().Where(h => h.UserId == userId).Select(h => h.SeriesId).ToHashSet();

    public bool IsHidden(Guid userId, Guid seriesId) => All().Any(h => h.UserId == userId && h.SeriesId == seriesId);

    /// <summary>Resolves an episode, season or series to its series.</summary>
    public Series? ResolveSeries(BaseItem item)
    {
        return item switch
        {
            Series series => series,
            IHasSeries hasSeries when hasSeries.SeriesId != Guid.Empty => _libraryManager.GetItemById<Series>(hasSeries.SeriesId),
            _ => null
        };
    }

    public HiddenSeries Hide(Guid userId, Series series)
    {
        lock (_lock)
        {
            var existing = All().FirstOrDefault(h => h.UserId == userId && h.SeriesId == series.Id);
            if (existing is not null)
            {
                return existing;
            }

            var entry = new HiddenSeries
            {
                UserId = userId,
                SeriesId = series.Id,
                SeriesName = series.Name,
                HiddenAt = DateTime.UtcNow
            };
            Save(All().Append(entry).ToArray());
            return entry;
        }
    }

    public bool Unhide(Guid userId, Guid seriesId)
    {
        lock (_lock)
        {
            var remaining = All().Where(h => !(h.UserId == userId && h.SeriesId == seriesId)).ToArray();
            if (remaining.Length == All().Count)
            {
                return false;
            }

            Save(remaining);
            return true;
        }
    }

    private static void Save(HiddenSeries[] list)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return;
        }

        plugin.Configuration.HiddenNextUp = list;
        plugin.SaveConfiguration();
    }
}
