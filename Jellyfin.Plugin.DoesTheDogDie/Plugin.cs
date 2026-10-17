using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.DoesTheDogDie;

/// <summary>
/// Shows community-sourced content warnings from DoesTheDogDie.com at the
/// start of playback.
/// </summary>
public class Plugin : BasePlugin<Configuration.PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Does the Dog Die";

    public override Guid Id => Guid.Parse("b8034a9d-1ab4-41f6-b6eb-6c6647a4e8fc");

    public override string Description =>
        "Shows community-sourced content warnings from DoesTheDogDie.com when playback starts.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        };
    }
}
