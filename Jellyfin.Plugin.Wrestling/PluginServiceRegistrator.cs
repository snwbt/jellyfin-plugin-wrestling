using Jellyfin.Plugin.Wrestling.Cagematch;
using Jellyfin.Plugin.Wrestling.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Wrestling;

/// <summary>
/// Registers plugin services with Jellyfin.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient<ICagematchClient, CagematchClient>();
        serviceCollection.AddSingleton<IWrestlingMatchCache, WrestlingMatchCache>();
        serviceCollection.AddSingleton<IWrestlingMatchService, WrestlingMatchService>();
    }
}
