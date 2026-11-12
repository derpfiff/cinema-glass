# Server-Setup

Das Theme selbst braucht nur CSS + JS (siehe README). Diese Einstellungen am Jellyfin-Server gehören dazu, damit es so aussieht wie gedacht. Alles ist optional und unabhängig voneinander.

## 1. Plugins

| Plugin | Wozu | Quelle |
|---|---|---|
| Cinema Glass | Aussehen, Funktionen und Einstellungsseite (siehe [PLUGIN.md](PLUGIN.md)) | Katalog aus diesem Repository |
| JavaScript Injector | nur bei Handinstallation ohne Cinema Glass: lädt `js/appletv.js` | Plugin-Katalog |
| Fanart | zusätzliche Logos/Hintergründe von fanart.tv | Katalog „Jellyfin Stable“ |
| Local Intros | eigener Vorspann vor Filmen | Katalog „Jellyfin Stable“ |
| Intro Skipper | „Intro überspringen“ (das Theme bringt die Glas-Optik mit) | eigenes Repository des Plugins |

Nach der Installation Jellyfin neu starten. Fanart: unter *Plugins → Fanart* den persönlichen API-Schlüssel von fanart.tv eintragen (kostenlos).

## 2. Bibliothek: Bildquellen

*Dashboard → Bibliotheken → Bibliothek verwalten* (Filme, Serien, Sammlungen):

- Bildquellen: TMDb zuerst, **Fanart dahinter**. So füllt Fanart nur Lücken.
- Danach einmal fehlende Bilder suchen lassen (vorhandene nicht ersetzen).

Logos, die auf schwarzem Grund unlesbar sind (dunkle Schrift, farbiger Kasten), am Titel über *Bilder bearbeiten → Logo → Suchen* gegen ein helles tauschen. Hintergrundbilder unter 1920 px Breite ebenso.

## 3. Vorspann vor Filmen (Local Intros)

1. Videos in einen Ordner legen, den der Jellyfin-Container sieht (z. B. `/media/intros`).
2. *Plugins → Local Intros*: Ordner eintragen, speichern, dann **Load Videos** drücken. Ohne diesen Schritt bleibt die Liste leer und im Log steht „No intros found in library“. Eine eigene Bibliothek für den Ordner ist nicht nötig.
3. Unter „Default“ das gewünschte Video wählen. „Only show intros for movies“ einschalten, sonst läuft der Vorspann vor jeder Folge.

## 4. Trailer

Der Knopf **Trailer** auf der Detailseite spielt den in Jellyfin hinterlegten YouTube-Trailer in einem eigenen Fenster (eingebetteter Player von `youtube-nocookie.com`; das Gerät des Zuschauers lädt das Video direkt von YouTube). Dafür ist nichts einzurichten. Titel ohne YouTube-Adresse nutzen Jellyfins eigene Trailer-Wiedergabe.

## 5. Seerr und Bewertungen

Adresse und Schlüssel unter *Plugins → Cinema Glass* eintragen (siehe [PLUGIN.md](PLUGIN.md)). Beides gibt es nur mit dem Plugin.

## 6. Fassungen, Studio- und Personenseiten, Update-Hinweis

- **Fassungen:** Hat ein Film mehrere Versionen (in Jellyfin über „Versionen gruppieren“ oder das Plugin Merge Versions), zeigt die Detailseite eine Auswahl wie bei den Staffeln; „Abspielen“ startet die gewählte.
- **Herunterladen:** Auf Film-, Folgen- und Serienseiten gibt es einen Download-Knopf. In der offiziellen Jellyfin-App (iOS/Android) landet der Titel in deren Downloads; die Qualität bestimmt dort die App. In einer eigenen App mit dem Kanal `atvDownload` (siehe [NATIVE-BRIDGE.md](NATIVE-BRIDGE.md)) und im mobilen Browser fragt das Theme nach der Qualität: „Original“ oder vom Server kleiner gerechnet (1080p/720p/480p als H.264/AAC-MP4). Am Rechner im normalen Browser ist der Knopf ausgeblendet.
- **Studio:** Der Studioname im Infoblock ist ein Link auf alle Titel des Studios. **Regie/Darsteller** führen auf die Personenseite; mit dem Seerr-Extra steht dort zusätzlich „Nicht in deiner Mediathek“ zum Anfragen. Der Proxy muss dafür `GET /api/v1/person/*` durchlassen.
- **Update-Hinweis:** Admins sehen auf der Startseite einen Hinweis, wenn `version.json` im Repository eine höhere Version nennt als `VERSION` in `js/appletv.js` (ein Abruf pro Tag bei `raw.githubusercontent.com`). Abschalten: `updateCheck: false`. Wer das Theme in einem eigenen Repository pflegt, trägt es bei `repo` ein.

## 7. Nach Änderungen am Theme

Mit Plugin: neu bauen (`sh plugin/build.sh`), die DLL ersetzen und Jellyfin neu starten. Handinstallation: CSS unter *Dashboard → Allgemein → Eigenes CSS*, JS im JavaScript Injector ersetzen. Apps (iOS/TV) danach komplett beenden und neu öffnen – sie behalten das Skript sonst im Speicher.
