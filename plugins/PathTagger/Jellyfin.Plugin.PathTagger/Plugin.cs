using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.PathTagger;

/// <summary>
/// Tags library items based on their folder path, so per-user visibility can
/// be controlled with Jellyfin's built-in blocked-tags filter — e.g. two
/// people sharing combined libraries while each profile only shows their own
/// items.
/// </summary>
public class Plugin : BasePlugin<Configuration.PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Path Tagger";

    public override Guid Id => Guid.Parse("96b79415-7681-4f99-a01e-0bd5c0166860");

    public override string Description =>
        "Automatically tags items by folder path, for per-user visibility via blocked tags.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        };
    }
}
