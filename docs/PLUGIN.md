# Cinema Glass – das Plugin

Das Plugin liefert Aussehen und Funktionen in einem Stück aus und bringt eine Einstellungsseite mit.
Es ersetzt die Handinstallation (eigenes CSS im Branding + JavaScript Injector). Seerr und Bewertungen brauchen keinen Reverse-Proxy mehr.

## Was es tut

- hängt Stylesheet und Skript selbst in die Weboberfläche ein (es werden keine Dateien von Jellyfin verändert)
- Einstellungsseite unter **Dashboard → Plugins → Cinema Glass**: jede Funktion lässt sich einzeln ein- und ausschalten
- reicht Abrufe an Seerr und MDBList selbst weiter – nur für angemeldete Jellyfin-Nutzer, die Schlüssel bleiben auf dem Server
- kommt über einen Plugin-Katalog, Updates laufen wie bei jedem anderen Plugin

## Installieren

1. Dashboard → Plugins → Kataloge (Repositories) → hinzufügen:
   `https://raw.githubusercontent.com/derpfiff/cinema-glass/main/manifest.json`
2. Im Katalog **Cinema Glass** installieren und Jellyfin neu starten.
3. Falls vorher von Hand installiert: das eigene CSS im Branding leeren und die Skripte im JavaScript Injector
   deaktivieren, sonst wird alles doppelt geladen.
4. Browser neu laden; Apps einmal ganz schließen und wieder öffnen.

Das Paket ist gegen Jellyfin 12.4 gebaut. Für andere Serverversionen selbst bauen (siehe unten).

## Einstellungen

| Schalter | Wirkung, wenn aus |
|---|---|
| Aussehen | kein Stylesheet – Jellyfin sieht aus wie ab Werk |
| Funktionen | kein Skript – nur das Aussehen bleibt |
| Hero-Banner | Startseite ohne Hero-Banner, Jellyfins eigene Regale |
| Eigene Film- und Serienseite | Jellyfins eigene Detailseite |
| Regal „Filmreihe“ | kein Regal mit den Teilen der Reihe auf der Filmseite |
| Karten spielen direkt ab | Klick auf eine Karte öffnet wie gewohnt die Seite |
| „···“-Menü bei Folgen | „···“ öffnet die Seite der Folge |
| Versionsauswahl | Jellyfins eigene Auswahl |
| Qualitäts-Abzeichen | keine Marken für 4K, HDR, Dolby Vision und Atmos – weder auf Karten noch auf der Film- und Folgenseite |
| Trailer im eigenen Fenster | Jellyfins eigene Trailer-Wiedergabe |
| Übergang Vorspann → Film | Titel und Standbild wie von Jellyfin geliefert |
| Seiten erst fertig zeigen | Seiten erscheinen sofort und bauen sich sichtbar auf |
| Regale folgen dem Trackpad | Jellyfins eigene Behandlung: beim seitlichen Wischen ruckt das Regal und zieht nach (außer in Firefox) |
| Zurückwischen | keine Wischgeste in der Handy-App |
| Download-Knöpfe / Fortschritt | keine Download-Knöpfe bzw. keine Anzeige |
| JellyDesk unterstützen | in der Mac-App [JellyDesk](https://github.com/derpfiff/JellyDesk) keine Download-Knöpfe, Vollbild über WebKit statt über das Fenster der App, Wiedergabe im Web-Player statt im Player der App |
| Seerr | kein Regal „Nicht in deiner Mediathek“, keine Anfragen |
| Bewertungen | keine Bewertungs-Plaketten |
| Update-Hinweis | kein Abruf bei GitHub (mit Plugin unnötig, Updates kommen über den Katalog) |

### Seerr

- **Adresse:** so, wie der Jellyfin-Server Seerr erreicht (z. B. `http://seerr:5055`), nicht die Adresse aus dem Browser.
- **API-Schlüssel:** aus Seerr → Einstellungen → Allgemein.
- **Nutzernummer** (optional): Anfragen laufen unter diesem Seerr-Konto. Leer = Hauptkonto.

Durchgelassen wird nur, was die Oberfläche braucht: Status, Suche, Film-/Serien-/Personendaten, Plakate,
die Liste der Qualitätsprofile und das Absenden einer Anfrage. Alles andere beantwortet das Plugin mit 404.

### Bewertungen

Ein kostenloser Schlüssel von mdblist.com genügt. Die Ergebnisse werden im Browser einige Tage zwischengespeichert.

## Selbst bauen

Gebraucht wird nur Docker. Das Plugin wird gegen die Bibliotheken des eigenen Servers gebaut:

```sh
mkdir -p plugin/lib
for f in MediaBrowser.Common MediaBrowser.Controller MediaBrowser.Model; do
  docker cp <jellyfin-container>:/jellyfin/$f.dll plugin/lib/
done
sh plugin/build.sh
```

Das Ergebnis `plugin/out/Jellyfin.Plugin.CinemaGlass.dll` in einen Ordner `plugins/CinemaGlass_<version>/`
im Konfigurationsverzeichnis von Jellyfin legen und den Server neu starten.
`sh plugin/package.sh` baut zusätzlich das Paket für den Katalog und trägt es in `manifest.json` ein.

## Entfernen

Dashboard → Plugins → Cinema Glass → deinstallieren, Jellyfin neu starten. Es bleibt nichts zurück.

## Aufbau

| Pfad | Inhalt |
|---|---|
| `css/appletv.css`, `js/*.js` | Aussehen und Funktionen – dieselben Dateien wie bei der Handinstallation |
| `plugin/src/Web/InjectStartupFilter.cs` | hängt Stylesheet und Skript in die Startseite der Weboberfläche und trägt das Wiedergabe-Plugin in deren Plugin-Liste (`config.json`) ein |
| `plugin/src/Web/CinemaGlassController.cs` | liefert beides aus (`/CinemaGlass/theme.css`, `/CinemaGlass/client.js`) und leitet an Seerr/MDBList weiter |
| `plugin/src/Configuration/` | Einstellungen und Einstellungsseite |

Die Schalter erreichen das Skript als `window.__cinemaGlass.features`. Ohne Plugin fehlt dieses Objekt, dann ist alles an.
