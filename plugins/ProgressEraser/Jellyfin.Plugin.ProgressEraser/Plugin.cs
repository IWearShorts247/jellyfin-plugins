using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ProgressEraser;

/// <summary>
/// Adds an eraser button to media cards and the item page that clears the
/// current user's playback position, removing the item from Continue Watching.
/// </summary>
public class Plugin : BasePlugin<Configuration.PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Progress Eraser";

    public override Guid Id => Guid.Parse("e5fcf8d4-e7d4-46b0-9333-0e6afa02f589");

    public override string Description =>
        "Eraser button that resets watch progress, and per-user hiding of shows from Next Up.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        };
    }
}
