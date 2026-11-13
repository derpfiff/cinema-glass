using Jellyfin.Plugin.CinemaGlass.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.CinemaGlass;

/// <summary>Cinema Glass: Oberfläche im Kino-Glas-Stil samt Zusatzfunktionen, alles über eine Einstellungsseite schaltbar.</summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Cinema Glass";

    public override Guid Id => Guid.Parse("6f2c1c0e-4c0b-4d57-9a55-3a1f0c7f9b21");

    public override string Description => "Kino-Oberfläche im Glas-Stil mit Hero, Folgenregal, Player-Feinschliff, Downloads, Seerr-Anfragen und Bewertungen. Jede Funktion einzeln schaltbar.";

    /// <summary>Kurzer Stempel der aktuellen Einstellungen: ändert sich bei jeder Änderung, damit Browser neu laden.</summary>
    public string ConfigurationStamp()
    {
        var c = Configuration;
        var text = string.Join('|', c.EnableTheme, c.EnableClient, c.Hero, c.DetailPage, c.HomeCardPlay, c.EpisodeMenu, c.VersionPicker, c.TrailerModal, c.IntroTransition, c.SwipeBack, c.PageGate, c.Downloads, c.DownloadProgress, c.NativeApp, c.UpdateCheck, c.Seerr, c.Ratings, c.SeerrUrl.Length, c.SeerrApiKey.Length, c.MdbListApiKey.Length, GetType().Assembly.ManifestModule.ModuleVersionId);
        return ((uint)StringComparer.Ordinal.GetHashCode(text) ^ 0x9e3779b9).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo { Name = "CinemaGlass", EmbeddedResourcePath = "CinemaGlass.configPage.html" };
    }
}
