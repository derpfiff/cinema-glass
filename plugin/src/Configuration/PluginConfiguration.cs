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
    public bool CollectionShelf { get; set; } = true;  // Regal „Filmreihe“ auf der Filmseite (braucht die eigene Detailseite)
    public bool HomeCardPlay { get; set; } = true;
    public bool EpisodeMenu { get; set; } = true;
    public bool VersionPicker { get; set; } = true;
    public bool QualityBadges { get; set; } = true;    // Abzeichen 4K, HDR, Dolby Vision, Atmos (Detailseite und Karten)
    public bool TrailerModal { get; set; } = true;
    public bool IntroTransition { get; set; } = true;
    public bool PauseInfo { get; set; } = true;        // Pause mit Kontext: Kapitel, Besetzung, Endzeit
    public bool SwipeBack { get; set; } = true;
    public bool PageGate { get; set; } = true;
    public bool Downloads { get; set; } = true;
    public bool DownloadProgress { get; set; } = true;
    public bool ShelfWheel { get; set; } = true;       // Regale folgen dem Trackpad direkt (Safari, Chrome, JellyDesk)
    public bool NativeApp { get; set; } = true;        // Zusammenspiel mit der Mac-App JellyDesk (Downloads, Vollbild, Wiedergabe)
    public bool UpdateCheck { get; set; } = false;
    public bool PluginUpdates { get; set; } = true;    // Admins: beim Öffnen fragen, ob neue Plugin-Versionen installiert werden sollen

    // Anfragen über Seerr (Jellyseerr/Overseerr)
    public bool Seerr { get; set; } = false;
    public string SeerrUrl { get; set; } = string.Empty;
    public string SeerrApiKey { get; set; } = string.Empty;
    public string SeerrUserId { get; set; } = string.Empty;   // optional: Anfragen laufen als dieser Seerr-Nutzer
    public bool Discover { get; set; } = true;         // „Entdecken“: Empfehlungen für fehlende Titel (nur mit eingerichtetem Seerr)
    public bool HomeRows { get; set; } = true;          // Startseite: eigene Reihen „Weiterschauen“ (eine Karte je Serie, mit Verfall) und „Neu hinzugefügt“ statt Jellyfins

    public bool PosterLanguage { get; set; } = false;  // Plakate neuer Titel nach Originalsprache: de → deutsch, en → englisch, sonst deutsch (braucht Seerr)

    public bool RatePrompt { get; set; } = false;      // Nach dem Schauen eines Films nach Sternen fragen (Jellyfins Bewertung des Kontos, zählt fürs Profil)

    public bool Letterboxd { get; set; } = false;      // Letterboxd: eigene Bewertungen als Geschmack, Reihe „Von deinen Freunden“ (nur öffentliche Seiten)

    // Ruhezeit der Download-Dienste (freiwillig, alles leer = aus): In dieser Zeit zeigt der Stand angefragter Titel
    // „Startet heute/morgen gegen …“ statt „Wird gesucht“. Uhrzeiten als HH:mm in der Zeitzone des Servers.
    public string DownloadsQuietFrom { get; set; } = string.Empty;
    public string DownloadsQuietTo { get; set; } = string.Empty;
    public string DownloadsDeferMarker { get; set; } = string.Empty;   // Textstück im Namen von Qualitätsprofilen, die erst nach der Ruhezeit laden

    // Qualitätsprofil nach Originalsprache (freiwillig, leer = aus): Zeilen „Sprache=Textstück im Profilnamen“, z. B.
    // „de=[German]“, „en=[German]“, „*=[OG + EN]“. Ist es gesetzt, fragt das Theme nicht mehr nach dem Profil; in der Ruhezeit
    // nimmt das Plugin außerdem die Variante mit DownloadsDeferMarker, damit nachts nichts die Platten weckt.
    public string AutoProfileRules { get; set; } = string.Empty;

    // Bewertungen über MDBList
    public bool Ratings { get; set; } = false;
    public string MdbListApiKey { get; set; } = string.Empty;
}
