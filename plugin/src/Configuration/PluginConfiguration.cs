using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.CinemaGlass.Configuration;

/// <summary>Einstellungen des Plugins. Schlüssel bleiben auf dem Server und werden nie an den Browser gegeben.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    // Grundschalter
    public bool EnableTheme { get; set; } = true;      // Aussehen (CSS)
    public bool EnableClient { get; set; } = true;     // Funktionen (JavaScript)

    // Funktionen der Oberfläche
    public bool Hero { get; set; } = true;
    public bool DetailPage { get; set; } = true;
    public bool HomeCardPlay { get; set; } = true;
    public bool EpisodeMenu { get; set; } = true;
    public bool VersionPicker { get; set; } = true;
    public bool TrailerModal { get; set; } = true;
    public bool IntroTransition { get; set; } = true;
    public bool SwipeBack { get; set; } = true;
    public bool PageGate { get; set; } = true;
    public bool Downloads { get; set; } = true;
    public bool DownloadProgress { get; set; } = true;
    public bool NativeApp { get; set; } = true;        // Zusammenspiel mit der Mac-App JellyDesk (Downloads, Vollbild)
    public bool UpdateCheck { get; set; } = false;

    // Anfragen über Seerr (Jellyseerr/Overseerr)
    public bool Seerr { get; set; } = false;
    public string SeerrUrl { get; set; } = string.Empty;
    public string SeerrApiKey { get; set; } = string.Empty;
    public string SeerrUserId { get; set; } = string.Empty;   // optional: Anfragen laufen als dieser Seerr-Nutzer

    // Bewertungen über MDBList
    public bool Ratings { get; set; } = false;
    public string MdbListApiKey { get; set; } = string.Empty;
}
