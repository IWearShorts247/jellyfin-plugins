using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PathTagger;

/// <summary>
/// Tags items as soon as they are added to the library, so new media doesn't
/// wait for the scheduled sweep (and doesn't briefly appear on profiles that
/// should not see it).
/// </summary>
public class NewItemHandler : IHostedService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<NewItemHandler> _logger;

    public NewItemHandler(ILibraryManager libraryManager, ILogger<NewItemHandler> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        return Task.CompletedTask;
    }

    private async void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        try
        {
            if (Plugin.Instance?.Configuration.TagNewItems == false)
            {
                return;
            }

            var mappings = Tagging.ParseMappings();
            if (mappings.Count == 0)
            {
                return;
            }

            var missing = Tagging.GetMissingTags(e.Item, mappings);
            if (missing.Count == 0)
            {
                return;
            }

            foreach (var tag in missing)
            {
                e.Item.AddTag(tag);
            }

            await e.Item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Path Tagger: failed to tag newly added item {Path}", e.Item?.Path);
        }
    }
}
