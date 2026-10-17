using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DoesTheDogDie.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the DoesTheDogDie.com API key (free — from your profile
    /// page on the site).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how long the warnings overlay stays on screen, in seconds.
    /// </summary>
    public int OverlaySeconds { get; set; } = 12;

    /// <summary>
    /// Gets or sets the minimum number of "yes" votes a topic needs before it
    /// is shown (filters out noise on rarely-voted titles).
    /// </summary>
    public int MinYesVotes { get; set; } = 3;

    /// <summary>
    /// Gets or sets the maximum number of warnings shown per title.
    /// </summary>
    public int MaxTopics { get; set; } = 8;

    /// <summary>
    /// Gets or sets a value indicating whether the overlay script is injected
    /// into jellyfin-web's index.html at startup.
    /// </summary>
    public bool InjectClientScript { get; set; } = true;
}
