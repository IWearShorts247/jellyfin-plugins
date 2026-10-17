using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Showtime;

/// <summary>
/// Registers the plugin's singletons.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ContentIndex>();
        serviceCollection.AddSingleton<SequenceBuilder>();
        serviceCollection.AddSingleton<SequenceTracker>();
        serviceCollection.AddSingleton<ActionRunner>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<SequenceTracker>());
    }
}
