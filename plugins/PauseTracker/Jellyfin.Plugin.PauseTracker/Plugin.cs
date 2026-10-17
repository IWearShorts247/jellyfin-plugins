using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.PauseTracker;

/// <summary>
/// The Pause Tracker plugin. Tracks pause counts and pause durations for active
/// playback sessions and exposes them to a small client-side overlay script.
/// </summary>
public class Plugin : BasePlugin<Configuration.PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Pause Tracker";

    public override Guid Id => Guid.Parse("7dfb5922-625a-420c-b4a4-a6d6e726977d");

    public override string Description =>
        "Tracks how many times playback is paused and for how long, and shows the totals in the video OSD.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        };
    }
}
