using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ProgressEraser;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<NextUpHider>();
        serviceCollection.AddSingleton<IStartupFilter, NextUpStartupFilter>();
        serviceCollection.AddHostedService<ScriptInjectionService>();
        serviceCollection.AddHostedService<NextUpPlaybackWatcher>();
    }
}
