using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PauseTracker.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the plugin should inject its
    /// overlay script into jellyfin-web's index.html at server startup.
    /// </summary>
    public bool InjectClientScript { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a fullscreen pause summary is
    /// shown in the web client when playback ends.
    /// </summary>
    public bool ShowExitSummary { get; set; } = true;

    /// <summary>
    /// Gets or sets user ids (Guid strings) whose playback is not tracked.
    /// Untracked users have no stats, so they see no overlay or summary.
    /// </summary>
    public string[] DisabledUserIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the grace threshold in minutes: pauses that begin within
    /// this far from the start of the media are not counted (prerolls /
    /// pausing until everyone is ready to watch). 0 counts every pause.
    /// </summary>
    public double IgnorePausesBeforeMinutes { get; set; } = 3;
}
