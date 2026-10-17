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

    /// <summary>
    /// Gets or sets a value indicating whether hidden shows are removed from
    /// the server's Next Up responses, which covers every client.
    /// </summary>
    public bool FilterNextUp { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a "hide from Next Up" button is
    /// added to Next Up cards and a toggle to series and episode pages.
    /// </summary>
    public bool NextUpButton { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether playing an episode of a hidden
    /// show puts the show back into Next Up.
    /// </summary>
    public bool UnhideOnPlayback { get; set; } = true;

    /// <summary>
    /// Gets or sets the shows each user has hidden from Next Up.
    /// </summary>
    public HiddenSeries[] HiddenNextUp { get; set; } = Array.Empty<HiddenSeries>();
}

public class HiddenSeries
{
    public Guid UserId { get; set; }

    public Guid SeriesId { get; set; }

    public string SeriesName { get; set; } = string.Empty;

    public DateTime HiddenAt { get; set; }
}
