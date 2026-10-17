using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.PauseTracker;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Singleton so the API controller can query the same instance the
        // hosted service updates.
        serviceCollection.AddSingleton<PauseTrackingService>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<PauseTrackingService>());
        serviceCollection.AddHostedService<ScriptInjectionService>();
    }
}
