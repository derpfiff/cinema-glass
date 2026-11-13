# Brücke zu einer eigenen nativen App

Das Theme kann Downloads und die Wiedergabe an eine eigene App übergeben, die Jellyfin in einem `WKWebView` zeigt, und ihr das Vollbild überlassen. Die App dazu ist [JellyDesk](https://github.com/derpfiff/JellyDesk) für macOS. Die App muss dafür nur Nachrichtenkanäle anmelden; im Plugin gehört dazu der Schalter „JellyDesk unterstützen“ (ab Werk an).

## Was die App bereitstellt

Einen `WKScriptMessageHandler` mit dem Namen **`atvDownload`**:

```swift
config.userContentController.add(handler, name: "atvDownload")
```

Wichtig: **kein** `window.NativeShell` definieren. Jellyfin-Web hält die Seite dann für die offizielle Mobil-App, erwartet deren vollständige Schnittstelle (`NativeShell.AppHost` …) und bricht beim Start ab.

Sobald `window.webkit.messageHandlers.atvDownload` existiert, zeigt das Theme den Download-Knopf (Film, Folge, Staffel, ganze Serie) auch am Rechner und fragt vor dem Laden nach der Qualität.

## Was das Theme schickt

Pro Titel eine Nachricht (bei Staffeln mehrere nacheinander, ca. 150 ms Abstand):

```json
{
  "type": "download",
  "version": 1,
  "url": "https://server/Items/<id>/Download?api_key=…",
  "filename": "Titel (2001).mkv",
  "title": "Titel",
  "itemId": "…",
  "itemType": "Movie | Episode | Video",
  "serverId": "…",
  "serverUrl": "https://server",
  "userId": "…",
  "accessToken": "…",
  "runTimeTicks": 137000000000,
  "quality": "orig | 1080 | 720 | 480",
  "estimatedBytes": 2300000000,
  "container": "mkv | mp4",
  "seriesName": "",
  "seasonNumber": null,
  "episodeNumber": null,
  "year": 2001,
  "imageUrl": "https://server/Items/<id>/Images/Primary?…",
  "backdropUrl": "https://server/Items/<id>/Images/Backdrop/0?…",
  "positionTicks": 0
}
```

- `url` ist fertig zum Laden und enthält das Token als `api_key`. `accessToken` steht zusätzlich für spätere API-Aufrufe (Fortschritt melden) drin – in den Schlüsselbund legen, nicht in Klartext-Dateien.
- **`quality: "orig"`**: `/Items/{id}/Download`, die Datei wie auf dem Server, mit `Content-Length`, fortsetzbar (Range).
- **Andere Qualitäten**: `/Videos/{id}/stream.mp4?static=false&videoCodec=h264&audioCodec=aac&videoBitRate=…&maxHeight=…`. Der Server rechnet während des Ladens um und schickt einen fortlaufenden MP4-Strom: **kein `Content-Length`, kein Range, nicht fortsetzbar**. Fortschritt über `estimatedBytes` schätzen; bricht die Verbindung ab, von vorn laden. Das Ergebnis ist H.264/AAC in MP4 und läuft mit AVKit.
- Originale sind oft MKV mit DTS/TrueHD – dafür braucht die App einen Player wie VLCKit oder MPVKit.
- Zeiten sind Jellyfin-Ticks (10 000 000 pro Sekunde).

## Vollbild über das Fenster der App

Das Element-Vollbild von WebKit hängt die Web-Ansicht in ein zweites Fenster um; dabei springt das Bild. Meldet die App zusätzlich einen Kanal **`atvWindow`** mit Antwort an (`WKScriptMessageHandlerWithReply`), leitet das Theme jede Vollbild-Anfrage der Seite dorthin um – Knopf im Player, Doppelklick, Taste F:

```json
{ "type": "fullscreen", "version": 1, "action": "enter | exit | toggle | state" }
```

Die App schaltet ihr Fenster um und antwortet mit dem Zustand, der danach gilt: `{ "fullscreen": true }`. `state` ändert nichts und fragt nur ab; das Theme nutzt es nach Größenänderungen, um mitzubekommen, wenn das Vollbild am Fenster selbst beendet wurde. Die Seite erhält weiter `fullscreenchange` und sieht `document.fullscreenElement`, Jellyfin-Web verhält sich also wie im Browser. Esc beendet das Vollbild.

Über denselben Kanal meldet das Theme das Seitenverhältnis des laufenden Videos (`{ "type": "video", "version": 1, "aspect": 1.78 }`), sobald es bekannt ist oder sich ändert. Die App passt damit die Darstellung während des Wechsels an.

In der App setzt das Theme außerdem die Klasse `atv-native` auf `<html>` und, solange ihr Fenster im Vollbild ist, `atv-native-fs`. Das Stylesheet rückt damit das ✕ des Players im Fenster unter die drei Fensterknöpfe und im Vollbild in die Ecke.

## Wiedergabe im Player der App

Meldet die App einen Kanal **`atvPlay`** mit Antwort an (`WKScriptMessageHandlerWithReply`), fragt das Theme sie vor jeder Wiedergabe, ob sie übernimmt. Technisch meldet sich das Theme bei Jellyfin-Web als Plugin der Art `preplayintercept` an; dafür trägt das Server-Plugin den Namen `CinemaGlassBridge` in die Plugin-Liste der Weboberfläche (`/web/config.json`) ein und stellt in der Startseite eine gleichnamige Funktion bereit. Im Browser und ohne den Kanal antwortet das Plugin sofort mit „weiter“.

```json
{
  "type": "play",
  "version": 1,
  "serverUrl": "https://server",
  "serverId": "…",
  "userId": "…",
  "accessToken": "…",
  "startIndex": 0,
  "startPositionTicks": 0,
  "mediaSourceId": null,
  "audioStreamIndex": null,
  "subtitleStreamIndex": null,
  "items": [
    { "itemId": "…", "title": "Titel", "itemType": "Movie | Episode | Video", "seriesName": "",
      "seasonNumber": null, "episodeNumber": null, "year": 2001, "runTimeTicks": 137000000000, "isIntro": false }
  ]
}
```

- `items` ist die Warteschlange, wie Jellyfin-Web sie abspielen wollte, ab dem ersten Titel: bei Serien die folgenden Folgen, davor gegebenenfalls ein Vorspann des Servers (`isIntro`). Als Vorspann gilt, was vor dem ersten Film bzw. der ersten Folge steht und selbst keins von beiden ist.
- `startPositionTicks`, `mediaSourceId` und die Spurnummern gelten für den ersten eigentlichen Titel. Die Spurnummern zählen wie beim Server (`MediaStreams[].Index`).
- Antwort der App: `{ "handled": true }` – das Theme bricht die Wiedergabe im Web ohne Fehlermeldung ab – oder `{ "handled": false }` bzw. keine Antwort: der Web-Player spielt wie gewohnt.
- Übergeben werden nur Videos, die auf dem Server liegen. Live-TV, entfernte Trailer, Platzhalter und gemischte Warteschlangen bleiben im Web-Player.
- Die App holt die Datei unverändert über `GET /Videos/{itemId}/stream?static=true&mediaSourceId=…` und meldet die Wiedergabe über `POST /Sessions/Playing`, `/Sessions/Playing/Progress` und `/Sessions/Playing/Stopped`.

## Fortschritt zurückmelden (Sache der App)

Nach Offline-Wiedergabe an den Server, sobald er erreichbar ist (Pfade gegen `/api-docs/openapi.json` des Servers prüfen):

- Position: `POST /UserItems/{itemId}/UserData?userId={userId}` mit `{"PlaybackPositionTicks": …}`
- Gesehen: `POST /UserPlayedItems/{itemId}?userId={userId}`
- Kopfzeile: `Authorization: MediaBrowser Client="…", Device="…", DeviceId="…", Version="…", Token="<accessToken>"`

## Hinweis zur offiziellen Jellyfin-App (iOS/Android)

Dort übergibt das Theme den Download über `NativeShell.downloadFile`. Die App bildet die Lade-Adresse selbst aus der Titel-ID und ignoriert die mitgegebene; die Qualitätswahl des Themes greift dort deshalb nicht und wird nicht angezeigt. Ob umgerechnet wird, bestimmt die Einstellung der App.
