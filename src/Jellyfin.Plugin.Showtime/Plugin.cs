using Jellyfin.Plugin.Showtime.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Showtime;

/// <summary>
/// Showtime: a CinemaVision-style pre-show for Jellyfin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Showtime";

    public override Guid Id => Guid.Parse("7c3e5b2a-41d6-4f0e-9b8a-5d2c6e1f9a37");

    public override string Description => "CinemaVision-style pre-show sequences: trivia, trailers, bumpers, ratings and audio-format cards, and light cues.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "Showtime",
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
        };
        yield return new PluginPageInfo
        {
            Name = "showtimejs",
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.js",
        };
    }
}
