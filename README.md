<p align="center"><img src="docs/img/banner.svg" alt="Cinema Glass – Kino-Oberfläche für Jellyfin" width="100%"></p>

<p align="center">
  <a href="https://github.com/derpfiff/cinema-glass/releases/latest"><img alt="Version" src="https://img.shields.io/badge/Version-1.10.13-7a5cff?style=flat-square"></a>
  <img alt="Jellyfin" src="https://img.shields.io/badge/Jellyfin-12.4-22c1ff?style=flat-square">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512bd4?style=flat-square">
  <a href="https://github.com/derpfiff/jellyfin-plugins"><img alt="Katalog" src="https://img.shields.io/badge/Katalog-derpfiff%2Fjellyfin--plugins-ff5c8a?style=flat-square"></a>
  <a href="LICENSE"><img alt="Lizenz" src="https://img.shields.io/badge/Lizenz-MIT-9aa4b2?style=flat-square"></a>
</p>

# Cinema Glass – Kino-Oberfläche für Jellyfin

Eine Oberfläche für die Jellyfin-Weboberfläche (getestet mit Jellyfin 12.4) im Stil moderner Fernseh-Apps: Hero-Banner,
Glasflächen, ruhige Typografie. Dazu Funktionen, die über ein Theme hinausgehen und Schwächen von Jellyfin und anderen Plugins ausbügeln – alle einzeln schaltbar.

![Startseite mit Hero-Banner](docs/img/desktop-start.jpg)

## Was es kann

| | |
|---|---|
| **Serienseite mit Folgenregal** – Staffelwahl, Fortschritt, Besetzung<br>![Folgenregal](docs/img/desktop-folgen.jpg) | **Filmseite** – Logo, Bewertungen, Fassungen wie bei den Staffeln wählbar<br>![Filmseite mit Versionsauswahl](docs/img/desktop-film-fassungen.jpg) |
| **Suche findet auch, was fehlt** – Regal „Nicht in deiner Mediathek“ über Seerr<br>![Suche mit Seerr](docs/img/desktop-suche-seerr.jpg) | **Anfragen ohne Umweg** – Staffeln und Qualitätsprofil direkt wählen<br>![Anfrage](docs/img/desktop-anfrage.jpg) |
| **TV-Seite** – Live-Programm und Mediathek auf einer Seite (mit dem Plugin „Mediathek“)<br>![TV-Seite](docs/img/desktop-tv.jpg) | **Mediathek wie eine Bibliothek** – Reihen, Suche, Serien mit Staffeln; Karten spielen direkt ab<br>![Mediathek](docs/img/desktop-tv-mediathek.jpg) |
| **Regale auf der Startseite** – Karten spielen direkt ab<br>![Regale](docs/img/desktop-regale.jpg) | **Alles schaltbar** – eigene Einstellungsseite, direkt in der Seitenleiste des Dashboards<br>![Einstellungen](docs/img/desktop-einstellungen.jpg) |

<p align="center">
  <img src="docs/img/handy-start.jpg" alt="Startseite am Handy" width="19%">
  <img src="docs/img/handy-serie.jpg" alt="Serienseite am Handy" width="19%">
  <img src="docs/img/handy-film-fassungen.jpg" alt="Versionsauswahl am Handy" width="19%">
  <img src="docs/img/handy-suche-seerr.jpg" alt="Suche am Handy" width="19%">
  <img src="docs/img/handy-download.jpg" alt="Download am Handy" width="19%">
</p>

Die Film- und Folgenseite zeigt, ob ein Titel in 4K, HDR, Dolby Vision oder mit Dolby Atmos vorliegt.

## Entdecken: Empfehlungen, die dich kennen

Mit [Seerr](https://github.com/seerr-team/seerr) bekommt jedes Konto eine eigene Seite „Entdecken“. Die Empfehlungen
lernen aus dem, was du schaust, abbrichst, mit Herz markierst oder als „Nicht meins“ aussortierst, auch aus deinen
Letterboxd-Bewertungen. Jede Empfehlung sagt, warum sie da ist („Weil dir … gefällt“, Übereinstimmung in Prozent).

![Entdecken](docs/img/desktop-entdecken.jpg)

| | |
|---|---|
| **Hitlisten, Studios und Sender** – mit echten Logos; eine Liste öffnet sich als Raster, Fehlendes ist mit „+“ markiert<br>![Hitlisten und Studios](docs/img/desktop-listen.jpg) | **Eine Liste** – Trends, Beliebtes, Demnächst im Kino, Studios, Sender<br>![Liste](docs/img/desktop-liste.jpg) |
| **Dein Geschmack** – woraus die Empfehlungen entstehen; ein Tipp schaltet ein Merkmal stumm<br>![Dein Geschmack](docs/img/desktop-geschmack.jpg) | **Filmreihe** – alle Teile, fehlende auf einmal anfragen<br>![Filmreihe](docs/img/desktop-filmreihe.jpg) |

Dazu gehören:
- **Kategorien:** am Handy als Blatt.
- **Filter „Nur Mediathek“:** zeigt nur, was schon da ist.
- **Trailer:** im eigenen Fenster, auch am iPhone und quer.
- **„Von deinen Freunden“:** was deine Letterboxd-Freunde gut fanden.
- **Anfragen zurückziehen:** Solange noch keine Datei da ist, lässt sich eine eigene Anfrage zurückziehen. Laufende Downloads werden dabei abgebrochen.

## Bewerten nach dem Schauen

Hast du einen Film zu Ende geschaut oder als gesehen abgehakt, fragt Cinema Glass gleich danach nach deiner Bewertung, wie bei Letterboxd: Datum, halbe Sterne, Herz,
Kommentar, „schon mal gesehen“. „Überspringen“ fragt nicht nochmal. Ältere Filme bewertest du jederzeit über das Menü
„···“. Ist das Plugin [Jellyscribe (Fork)](https://github.com/derpfiff/jellyscribe-fork) installiert und dein Konto
verknüpft, landet „Speichern“ zusätzlich in deinem Letterboxd-Tagebuch. Ohne dein „Speichern“ wird nichts eingetragen.

| | |
|---|---|
| ![Bewerten am Rechner](docs/img/desktop-bewerten.jpg) | <img src="docs/img/handy-bewerten.jpg" alt="Bewerten am Handy" width="300"> |

## Startseite

Eigene Reihen ersetzen Jellyfins „Weiterschauen“ und „Kürzlich hinzugefügt“:
- **Weiterschauen:** eine Karte je Serie, mit der nächsten Folge. Ältere Titel fallen heraus, einzelne lassen sich entfernen.
- **Neu hinzugefügt:** Filme und Serien gemischt, sortiert nach Ankunft.

![Weiterschauen](docs/img/desktop-weiterschauen.jpg)

<p align="center">
  <img src="docs/img/handy-entdecken.jpg" alt="Entdecken am Handy" width="24%">
  <img src="docs/img/handy-kategorien.jpg" alt="Kategorien am Handy" width="24%">
  <img src="docs/img/handy-geschmack.jpg" alt="Dein Geschmack am Handy" width="24%">
  <img src="docs/img/handy-bewerten.jpg" alt="Bewerten am Handy" width="24%">
</p>

Auf Wunsch bekommen neue Titel ein Plakat in ihrer Originalsprache: deutsch bei deutschen, englisch bei englischen und
sonst deutsch.

## Mehr als ein Theme: was es an Jellyfin und anderen Plugins glattzieht

Ein Teil der Funktionen ist kein Aussehen, sondern behebt Stellen, an denen Jellyfin, seine Apps oder andere Plugins haken.

| Wo es hakt | Ohne Cinema Glass | Mit Cinema Glass |
|---|---|---|
| **Vorspann ([Local Intros](https://github.com/dkanada/jellyfin-plugin-intros))** | Während des Vorspanns steht dessen Dateiname im Player. Zwischen Vorspann und Film bleibt das letzte Bild des Vorspanns stehen oder es wird schwarz. Eine gewählte Fassung wird ignoriert. | Kein Titel während des Vorspanns, in der Lücke steht das Hintergrundbild des Films, der Film wird schon vorgeladen, die gewählte Fassung startet. |
| **Trailer** | Jellyfins eigene Trailer-Wiedergabe endet in den Apps oft mit „Wiedergabefehler“. | Der hinterlegte Trailer läuft in einem eigenen Fenster. Gibt es keinen, erscheint der Knopf gar nicht erst. |
| **Mac-App [JellyDesk](https://github.com/derpfiff/JellyDesk)** | Am Rechner gibt es keine Downloads und keine Offline-Wiedergabe. | Download-Knöpfe auch am Mac, die App lädt und spielt offline ab; das Vollbild läuft über das Fenster der App und damit ohne Sprung. Der Play-Knopf startet den Player der App, der jede Datei unverändert abspielt (DTS, TrueHD, Bild-Untertitel), ohne dass der Server umrechnet. |
| **Downloads in der Jellyfin-App** | Die App kann herunterladen, der Befehl steckt aber im Menü einzelner Titel – nicht für ganze Staffeln oder Serien – und es gibt keinen Fortschritt, solange der Server umwandelt. | Download-Knopf bei Film, Folge, Staffel und ganzer Serie, dazu eine Fortschrittsanzeige. Die gewählte Qualität gilt auch, wenn in der App „transkodierte Downloads“ eingeschaltet ist (nur dann spielt die App Geladenes selbst ab): Das Plugin setzt sie in der Anfrage der App durch. |
| **Mehrere Fassungen ([Merge Versions](https://github.com/danieladov/jellyfin-plugin-mergeversions))** | Die Auswahl steckt in einem unscheinbaren Aufklappfeld. | Auswahl wie bei den Staffeln, mit Laufzeit je Fassung. |
| **Fehlende Titel ([Seerr](https://github.com/seerr-team/seerr))** | Eigene Webseite, eigene Anmeldung. | Regal „Nicht in deiner Mediathek“ in der Suche und auf Personenseiten, Anfrage mit Staffeln und Qualitätsprofil an Ort und Stelle. |
| **Filmreihen** | Welche Teile es gibt und welche schon gesehen sind, zeigt nur die Seite der Sammlung. | Regal „Filmreihe“ auf der Filmseite: alle Teile der Sammlung in Erscheinungsreihenfolge, gesehene mit Haken; mit Seerr stehen auch die fehlenden Teile da und lassen sich anfragen. |
| **Empfehlungen** | „Ähnliche Titel“ schlägt nur vor, was schon in der Bibliothek liegt. | Seite „Entdecken“ (mit Seerr) im Netflix-Stil: persönliche Empfehlungen je Konto auch für Titel, die fehlen – großes Bild, „Für dich ausgewählt“, Reihen nach Genre, Personen und „Weil dir … gefällt“, mit Übereinstimmung in Prozent und Begründung. Geschmacks-Check beim ersten Besuch und „Geschmack verfeinern“ jederzeit; „Dein Geschmack“ zeigt, woraus die Empfehlungen entstehen. Fehlendes ist überall dezent mit „+“ gekennzeichnet; Serien fragt man über ein Staffel-Fenster an. Auf der Startseite „Unterwegs zu dir“ (Stand angefragter Titel mit Prozent, Restzeit oder Erscheinungstermin) und „Neu für dich“. |
| **„···“ bei Folgen** | Öffnet eine eigene Seite, von der man wieder zurück muss. | Kleines Menü an Ort und Stelle, „Details“ als Infofenster. |
| **Zurück in der iOS-App** | Keine Wischgeste – nur der Zurück-Knopf. | Vom Rand wischen, die vorherige Seite liegt sichtbar darunter. |
| **Seitenaufbau** | Seiten erscheinen halb fertig, Regale und Bilder springen nach. | Die Seite erscheint erst, wenn sie fertig aufgebaut ist. |
| **Karten auf der Startseite** | Ein Tipp auf eine Folge führt erst zur Serienseite. | Bild und ▶-Zeile spielen direkt ab, die Titelzeile öffnet die Seite. |
| **Player und [Intro Skipper](https://github.com/intro-skipper/intro-skipper)** | Standard-Bedienleiste; der Überspringen-Knopf passt optisch nicht dazu. | Bedienleiste, Titelzeile und Überspringen-Knopf im selben Glas-Stil. |

| | |
|---|---|
| **Zurückwischen** – die Startseite liegt unter der Serienseite<br><img src="docs/img/handy-wischen.jpg" alt="Zurückwischen" width="260"> | **Folgenmenü und Infofenster** statt Seitenwechsel<br><img src="docs/img/handy-folgenmenue.jpg" alt="Folgenmenü" width="260"> <img src="docs/img/handy-folgeninfo.jpg" alt="Infofenster einer Folge" width="260"> |
| **Player** im selben Stil<br>![Player](docs/img/desktop-player.jpg) | **Anmeldung**<br>![Anmeldung](docs/img/desktop-anmeldung.jpg) |

Jeder dieser Punkte hat einen eigenen Schalter in den Einstellungen. Vorspann, Trailer und Seitenaufbau lassen sich auf
einem Standbild nicht zeigen – dafür gibt es keine Bildschirmfotos.

<sub>Die Bildschirmfotos stammen aus einer Testinstanz mit leeren Platzhalter-Videos. Plakate und Texte kommen von TMDb und gehören den jeweiligen Rechteinhabern; sie sind nicht Teil dieses Projekts.</sub>

Das Plugin selbst enthält keine fremden Grafiken oder Schriften. Die Systemschrift wird nur per Name angefordert.

## Installation (empfohlen): Plugin

1. Dashboard → Plugins → Kataloge → hinzufügen:
   `https://raw.githubusercontent.com/derpfiff/jellyfin-plugins/main/manifest.json`
   Das ist der gemeinsame Katalog mit Mediathek und Jellyscribe (Fork). Nur Cinema Glass liefert
   `https://raw.githubusercontent.com/derpfiff/cinema-glass/main/manifest.json`.
2. **Cinema Glass** installieren, Jellyfin neu starten.
3. Dashboard → Plugins → Cinema Glass: Funktionen wählen, bei Bedarf Seerr und MDBList eintragen.

Alles Weitere: [docs/PLUGIN.md](docs/PLUGIN.md).

## Passende Plugins (alle freiwillig)

Cinema Glass läuft allein. Diese Plugins und Dienste ergänzen einzelne Funktionen. Fehlt eines, bleibt nur die
jeweilige Funktion unsichtbar – es gibt keine Fehlermeldung und nichts geht kaputt.

| Plugin / Dienst | Wofür | Ohne |
|---|---|---|
| **[Fanart](https://github.com/jellyfin/jellyfin-plugin-fanart)** (Katalog „Jellyfin Stable“) | Titel-Logos und zusätzliche Hintergrundbilder | Titel stehen als Text da, weniger Bilder |
| **[Local Intros](https://github.com/dkanada/jellyfin-plugin-intros)** (Katalog „Jellyfin Stable“) | eigener Vorspann vor Filmen; Cinema Glass sorgt für den sauberen Übergang | kein Vorspann, der Film startet direkt |
| **[Intro Skipper](https://github.com/intro-skipper/intro-skipper)** (eigenes Repository des Plugins) | Knopf „Intro überspringen“ bei Serien, im Stil der Oberfläche | kein Überspringen-Knopf |
| **[Merge Versions](https://github.com/danieladov/jellyfin-plugin-mergeversions)** (eigenes Repository des Plugins) | fasst mehrere Fassungen eines Films zusammen; Cinema Glass zeigt dann die Versionsauswahl | keine Versionsauswahl (geht auch von Hand über „Versionen gruppieren“) |
| **[Mediathek](https://github.com/derpfiff/jellyfin-mediathek)** (eigenes Repository des Plugins) | Live-TV und Mediathek der öffentlich-rechtlichen Sender; Cinema Glass macht daraus die TV-Seite (Sender je Konto aus- und einblendbar, „Weiterschauen“ mit gemerkter Stelle, auf dem Handy über den Chip „TV“ der Startseite, Herunterladen in JellyDesk und in der Jellyfin-App) und zeigt Treffer in der Suche als „Aus der Mediathek“ | kein Eintrag „TV“ (bzw. nur Jellyfins eigenes Live-TV) |
| **[Seerr](https://github.com/seerr-team/seerr)** (Jellyseerr/Overseerr, eigener Dienst) | fehlende Titel in der Suche finden und anfragen | kein Regal „Nicht in deiner Mediathek“ |
| **[MDBList](https://mdblist.com)** (kostenloser API-Schlüssel) | Bewertungen von IMDb, Rotten Tomatoes usw. | keine Bewertungs-Plaketten |
| **[Jellyscribe (Fork)](https://github.com/derpfiff/jellyscribe-fork)** (gemeinsamer Katalog) | „Speichern“ im Bewerten-Dialog trägt ins Letterboxd-Tagebuch ein | Bewertungen bleiben in Jellyfin und im Geschmacksprofil |

Der **[JavaScript Injector](https://github.com/n00bcodr/Jellyfin-JavaScript-Injector)** wird mit dem Plugin nicht mehr gebraucht. Einrichtung der einzelnen Plugins: [docs/SETUP.md](docs/SETUP.md).

## Installation von Hand (ohne Plugin)

Geht weiterhin, dann aber ohne Einstellungsseite – alle Funktionen sind an. Seerr-Anfragen und Bewertungen gibt es nur mit dem Plugin.

| Datei | Zweck |
|---|---|
| `css/appletv.css` | gesamtes Styling |
| `js/appletv.js` | Hero-Banner, Regale, Detailseite, Player, Downloads usw. |


1. **CSS:** Dashboard → Allgemein → Branding → *Eigenes CSS*: Inhalt von `css/appletv.css` einfügen und speichern.
2. **JS:** Das Plugin **[JavaScript Injector](https://github.com/n00bcodr/Jellyfin-JavaScript-Injector)** installieren (Katalog: Dashboard → Plugins) und Jellyfin neu starten.
   Im Plugin ein Skript anlegen, den Inhalt von `js/appletv.js` einfügen und aktivieren.
3. Browser neu laden (Strg+F5). Auf Smart-TV- oder Handy-Apps, die den Web-Client nutzen, die App neu starten.

## Server-Setup

Plugins, Bildquellen (Fanart), Vorspann und Trailer: siehe [docs/SETUP.md](docs/SETUP.md).

## Entfernen

Plugin: deinstallieren und Jellyfin neu starten. Handinstallation: das eigene CSS im Branding leeren und die Skripte im JavaScript Injector deaktivieren oder löschen. Danach den Browser neu laden.

## Verwandte Projekte

| Projekt | Was es tut |
|---|---|
| [jellyfin-plugins](https://github.com/derpfiff/jellyfin-plugins) | ein Katalog für alle drei Plugins |
| [Mediathek](https://github.com/derpfiff/jellyfin-mediathek) | ARD-Mediathek und Live-TV der öffentlich-rechtlichen Sender |
| [Jellyscribe (Fork)](https://github.com/derpfiff/jellyscribe-fork) | Letterboxd-Tagebuch aus dem Bewerten-Dialog |
| [JellyDesk](https://github.com/derpfiff/JellyDesk) | Mac-App mit Downloads und Offline-Wiedergabe |

Dieses Projekt ist kein offizielles Angebot von Jellyfin oder Apple.
