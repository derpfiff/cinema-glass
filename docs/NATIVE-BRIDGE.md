# Brücke zu einer eigenen nativen App

Das Theme kann Downloads an eine eigene App übergeben, die Jellyfin in einem `WKWebView` zeigt, und ihr das Vollbild überlassen. Die App dazu ist [JellyDesk](https://github.com/derpfiff/JellyDesk) für macOS. Die App muss dafür nur Nachrichtenkanäle anmelden; im Plugin gehört dazu der Schalter „JellyDesk unterstützen“ (ab Werk an).

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

In der App setzt das Theme außerdem die Klasse `atv-native` auf `<html>` und, solange ihr Fenster im Vollbild ist, `atv-native-fs`. Das Stylesheet rückt damit das ✕ des Players im Fenster unter die drei Fensterknöpfe und im Vollbild in die Ecke.

## Fortschritt zurückmelden (Sache der App)

Nach Offline-Wiedergabe an den Server, sobald er erreichbar ist (Pfade gegen `/api-docs/openapi.json` des Servers prüfen):

- Position: `POST /UserItems/{itemId}/UserData?userId={userId}` mit `{"PlaybackPositionTicks": …}`
- Gesehen: `POST /UserPlayedItems/{itemId}?userId={userId}`
- Kopfzeile: `Authorization: MediaBrowser Client="…", Device="…", DeviceId="…", Version="…", Token="<accessToken>"`

## Hinweis zur offiziellen Jellyfin-App (iOS/Android)

Dort übergibt das Theme den Download über `NativeShell.downloadFile`. Die App bildet die Lade-Adresse selbst aus der Titel-ID und ignoriert die mitgegebene; die Qualitätswahl des Themes greift dort deshalb nicht und wird nicht angezeigt. Ob umgerechnet wird, bestimmt die Einstellung der App.
