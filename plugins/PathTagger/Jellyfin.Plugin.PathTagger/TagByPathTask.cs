using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PathTagger;

/// <summary>
/// Scheduled task that sweeps the whole library and applies the configured
/// path-based tags to movies, series, seasons and episodes.
/// </summary>
public class TagByPathTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<TagByPathTask> _logger;

    public TagByPathTask(ILibraryManager libraryManager, ILogger<TagByPathTask> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public string Name => "Tag items by path";

    public string Key => "PathTaggerTask";

    public string Description => "Applies the Path Tagger plugin's folder-to-tag mappings to all library items.";

    public string Category => "Library";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
        };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var mappings = Tagging.ParseMappings();
        if (mappings.Count == 0)
        {
            _logger.LogInformation("Path Tagger: no mappings configured, nothing to do");
            return;
        }

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.Movie,
                BaseItemKind.Series,
                BaseItemKind.Season,
                BaseItemKind.Episode,
                BaseItemKind.Video
            },
            Recursive = true
        });

        var updated = 0;
        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[i];
            var missing = Tagging.GetMissingTags(item, mappings);
            if (missing.Count > 0)
            {
                foreach (var tag in missing)
                {
                    item.AddTag(tag);
                }

                await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                updated++;
            }

            progress.Report(100.0 * (i + 1) / items.Count);
        }

        _logger.LogInformation("Path Tagger: examined {Total} items, tagged {Updated}", items.Count, updated);
    }
}
