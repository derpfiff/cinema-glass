using Jellyfin.Plugin.CinemaGlass.Discover;
using Jellyfin.Plugin.CinemaGlass.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.CinemaGlass;

/// <summary>Meldet die Einbindung in die Startseite der Weboberfläche, den HTTP-Client für die Weiterleitungen und die Empfehlungen an.</summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IStartupFilter, InjectStartupFilter>();
        serviceCollection.AddHttpClient("CinemaGlass", c => c.Timeout = TimeSpan.FromSeconds(40));
        // Setzt die im Theme gewählte Download-Qualität durch, wenn die Jellyfin-App selbst beim Server anfragt.
        serviceCollection.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o => o.Filters.Add<DownloadQualityFilter>());
        // „Entdecken“: ein Dienst für alle Konten. Als gehosteter Dienst angemeldet, damit laufende Berechnungen beim
        // Herunterfahren abbrechen; von sich aus rechnet er nie.
        serviceCollection.AddSingleton<DiscoverService>();
        serviceCollection.AddHostedService(provider => provider.GetRequiredService<DiscoverService>());
        // Plakate neuer Titel nach Originalsprache (Schalter „PosterLanguage“)
        serviceCollection.AddHostedService<PosterLanguage>();
    }
}
