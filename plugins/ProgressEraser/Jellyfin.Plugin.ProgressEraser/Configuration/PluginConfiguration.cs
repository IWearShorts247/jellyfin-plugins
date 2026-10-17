using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ProgressEraser.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the client script is injected
    /// into jellyfin-web's index.html at startup.
    /// </summary>
    public bool InjectClientScript { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the eraser shows on every card's
    /// hover menu, or only on cards that currently show a progress bar.
    /// </summary>
    public bool ShowOnAllCards { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the eraser also appears in the
    /// button row of the item details page when the item is resumable.
    /// </summary>
    public bool ShowOnDetailPage { get; set; } = true;
}
