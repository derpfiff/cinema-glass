using Jellyfin.Plugin.CinemaGlass.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.CinemaGlass;

/// <summary>Meldet die Einbindung in die Startseite der Weboberfläche und den HTTP-Client für die Weiterleitungen an.</summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IStartupFilter, InjectStartupFilter>();
        serviceCollection.AddHttpClient("CinemaGlass", c => c.Timeout = TimeSpan.FromSeconds(40));
        // Setzt die im Theme gewählte Download-Qualität durch, wenn die Jellyfin-App selbst beim Server anfragt.
        serviceCollection.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o => o.Filters.Add<DownloadQualityFilter>());
    }
}
