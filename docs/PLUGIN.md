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
| Qualitäts-Abzeichen | keine Marken für 4K, HDR, Dolby Vision und Atmos auf der Film- und Folgenseite |
| Trailer im eigenen Fenster | Jellyfins eigene Trailer-Wiedergabe |
| Übergang Vorspann → Film | Titel und Standbild wie von Jellyfin geliefert |
| Seiten erst fertig zeigen | Seiten erscheinen sofort und bauen sich sichtbar auf |
| Regale folgen dem Trackpad | Jellyfins eigene Behandlung: beim seitlichen Wischen ruckt das Regal und zieht nach (außer in Firefox) |
| Zurückwischen | keine Wischgeste in der Handy-App |
| Download-Knöpfe / Fortschritt | keine Download-Knöpfe bzw. keine Anzeige |
| JellyDesk unterstützen | in der Mac-App [JellyDesk](https://github.com/derpfiff/JellyDesk) keine Download-Knöpfe, Vollbild über WebKit statt über das Fenster der App, Wiedergabe im Web-Player statt im Player der App |
| Seerr | kein Regal „Nicht in deiner Mediathek“, keine Anfragen |
| Entdecken (braucht Seerr) | keine Empfehlungen: weder die Reihen auf der Startseite und auf der Film- und Serienseite noch die Berechnung auf dem Server – das Plugin fragt dafür dann nichts bei Seerr ab und merkt sich nichts je Konto (siehe [Entdecken](#entdecken-wie-die-empfehlungen-entstehen)) |
| Bewertungen | keine Bewertungs-Plaketten |
| Update-Hinweis | kein Abruf bei GitHub (mit Plugin unnötig, Updates kommen über den Katalog) |

### Eigene Zeichen für die Qualitäts-Marken

Das Theme zeichnet die Marken selbst (Schrift und ein einfaches Symbol). Wer auf seinem Server andere Zeichen verwenden
möchte und dazu berechtigt ist, legt sie als `.svg` oder `.png` in den Ordner `CinemaGlass-logos` neben den
Plugin-Einstellungen (`<Jellyfin-Konfiguration>/plugins/configurations/CinemaGlass-logos/`). Erkannt werden die Namen
`4k`, `hdr`, `hdr10`, `hdr10plus`, `hlg`, `dv` und `atmos`. Die Dateien werden einfarbig weiß dargestellt. Im Paket und
im Repository liegen keine solchen Dateien; die Zeichen von Dolby, UHD und HDR10+ sind Marken ihrer Inhaber.

Die bekannten Zeichen gibt es als SVG bei [Wikimedia Commons](https://commons.wikimedia.org); von dort holt man sie
sich selbst:

| Datei im Ordner | Datei bei Wikimedia Commons |
|---|---|
| `4k.svg` | `Ultra HD - Logo (transparent).svg` |
| `dv.svg` | `Dolby Vision 2021 logo.svg` |
| `atmos.svg` | `Logo Dolby Atmos.svg` |
| `hdr10.svg` | `HDR 10 logo (black).svg` |
| `hdr10plus.svg` | `HDR 10 plus logo (black).svg` |

```sh
cd "<Jellyfin-Konfiguration>/plugins/configurations" && mkdir -p CinemaGlass-logos && cd CinemaGlass-logos
get() { curl -fsSL -o "$1" "https://commons.wikimedia.org/wiki/Special:FilePath/$2"; }
get 4k.svg        "Ultra%20HD%20-%20Logo%20%28transparent%29.svg"
get dv.svg        "Dolby%20Vision%202021%20logo.svg"
get atmos.svg     "Logo%20Dolby%20Atmos.svg"
get hdr10.svg     "HDR%2010%20logo%20%28black%29.svg"
get hdr10plus.svg "HDR%2010%20plus%20logo%20%28black%29.svg"
```

Danach Jellyfin neu starten. Die Dateien müssen für das Konto lesbar sein, unter dem Jellyfin läuft. Ob und wofür man
die Zeichen verwenden darf, regeln ihre Inhaber; für die Anzeige auf dem eigenen Server ist das üblich.

### Eigene Avatare (Profilbild-Auswahl wie bei Netflix)

In den Einstellungen öffnet ein Tipp auf das Profilbild die Auswahl: Reihen mit Avataren, darunter Farben und „Eigenes
Foto“. Die Avatare kommen aus dem Ordner `CinemaGlass-avatars` neben den Plugin-Einstellungen
(`<Jellyfin-Konfiguration>/plugins/configurations/CinemaGlass-avatars/`). Jeder Unterordner wird eine Reihe, sein Name
die Überschrift (eine Zahl davor wie in `00 Klassiker` bestimmt nur die Reihenfolge und wird nicht angezeigt); Bilder
direkt im Ordner bilden die Reihe „Avatare“. Erkannt werden `.png`, `.jpg`, `.jpeg` und `.webp`,
höchstens 150 je Reihe, quadratisch am besten (sonst wird die Mitte genommen). Im Paket und im Repository liegen keine
Bilder: welche man verwendet und ob man das darf, entscheidet jeder für seinen Server selbst.

```
CinemaGlass-avatars/
├── Klassiker/
│   ├── rot.png
│   └── blau.png
├── Stranger Things/
│   ├── eleven.png
│   └── dustin.png
└── Pixel/
    └── ...
```

```sh
cd "<Jellyfin-Konfiguration>/plugins/configurations" && mkdir -p "CinemaGlass-avatars/Klassiker"
# Bilder hineinkopieren, z. B.:
cp ~/Downloads/avatare/*.png "CinemaGlass-avatars/Klassiker/"
```

**Netflix-Profilbilder:** Eine öffentliche Sammlung mit Links zu den Profilbildern von Netflix (nach Serie geordnet)
gibt es als [Google-Tabelle](https://docs.google.com/spreadsheets/d/1T5q4qNTFOiU-45iTDUDDkxsj_Hb9S9mZpg4GyLNYarw/htmlview).
Das Skript [`tools/avatars-aus-tabelle.py`](../tools/avatars-aus-tabelle.py) lädt sie in den Ordner, eine Reihe je Serie
(„00 Klassiker“ zuerst, dann Stranger Things, Wednesday, Squid Game …):

```sh
python3 tools/avatars-aus-tabelle.py 1T5q4qNTFOiU-45iTDUDDkxsj_Hb9S9mZpg4GyLNYarw \
  "<Jellyfin-Konfiguration>/plugins/configurations/CinemaGlass-avatars"
```

Die Bilder gehören Netflix; das Repository enthält keine davon, nur den Weg dorthin. Ob du sie auf deinem Server
verwendest, entscheidest du selbst.

Neue Bilder erscheinen sofort, ein Neustart ist nicht nötig. Die Dateien müssen für das Konto lesbar sein, unter dem
Jellyfin läuft. Das gewählte Bild wird als normales Jellyfin-Benutzerbild gespeichert und gilt damit in allen Apps.

### Seerr

- **Adresse:** so, wie der Jellyfin-Server Seerr erreicht (z. B. `http://seerr:5055`), nicht die Adresse aus dem Browser.
- **API-Schlüssel:** aus Seerr → Einstellungen → Allgemein.
- **Nutzernummer** (optional): Anfragen laufen unter diesem Seerr-Konto. Leer = Hauptkonto.

Durchgelassen wird nur, was die Oberfläche braucht: Status, Suche, Film-/Serien-/Personendaten, Plakate,
die Liste der Qualitätsprofile und das Absenden einer Anfrage. Alles andere beantwortet das Plugin mit 404.

### Bewertungen

Ein kostenloser Schlüssel von mdblist.com genügt. Die Ergebnisse werden im Browser einige Tage zwischengespeichert.

## Entdecken: wie die Empfehlungen entstehen

„Entdecken“ schlägt jedem Konto Filme und Serien vor, vor allem solche, die in der Bibliothek fehlen und sich über Seerr
anfragen lassen. Gerechnet wird im Plugin auf dem Server, getrennt je Konto. Voraussetzung: Seerr ist eingerichtet und
die Schalter „Seerr“ und „Entdecken“ sind an.

### Mit wem das Plugin dafür spricht

Nur mit dem eigenen Seerr, über die Adresse und den Schlüssel aus den Einstellungen. Seerr holt die Angaben seinerseits
bei TMDb. Das Plugin ruft keinen anderen Dienst auf und sendet nichts über die Konten nach außen: Die Abrufe nennen nur
Titelnummern, Genres, Personen und Stichworte, nie ein Konto. Aus Jellyfin liest es, welche Filme und Serien in der
Bibliothek liegen (mit ihrer TMDb-Nummer) und was ein Konto davon gesehen oder mit Herz versehen hat; es schreibt nichts
nach Jellyfin. Titel aus Kanälen (etwa einer Mediathek) zählen nicht.

### Wann gerechnet wird

Nur, wenn ein Konto die Seite „Entdecken“ oder die Startseite öffnet. Es gibt keine Zeitpläne und keine nächtlichen
Aufgaben. Die erste Berechnung läuft im Hintergrund und dauert einige Sekunden (abhängig davon, wie schnell Seerr
antwortet); danach kommen die Antworten aus dem Arbeitsspeicher. Ein Stand, der älter als zwölf Stunden ist oder nach dem
sich Gesehenes, Favoriten oder die Bibliothek geändert haben, wird noch einmal ausgeliefert und dabei im Hintergrund
erneuert. Rückmeldungen („Gefällt mir“, „Nicht meins“, „Kein Interesse“) ordnen den vorhandenen Bestand sofort neu, ohne
Abrufe. Nach einem Neustart von Jellyfin entsteht der Stand beim nächsten Öffnen neu.

### Woraus der Geschmack eines Kontos abgelesen wird

| Signal | Gewicht |
|---|---|
| Herz (Favorit) | +3 |
| Rückmeldung „Gefällt mir“ (auch für Titel außerhalb der Bibliothek) | +3 |
| Serie zu mindestens 80 % gesehen, Film mehrfach gesehen | +1,5 |
| Film gesehen | +1 |
| Serie teilweise gesehen | anteilig, mindestens +0,4 |
| über Seerr angefragt | +1 |
| in der Bibliothek, noch nicht gesehen | +0,1 – alle zusammen höchstens 30 % des positiven Gewichts |
| vor über zwei Wochen abgebrochen (Film nach unter 20 %, Serie nach höchstens drei Folgen und unter 20 %) | −0,5 |
| Rückmeldung „Kein Interesse“ | −0,5 |
| Rückmeldung „Nicht meins“ | −2 |

Jüngere Signale zählen etwas mehr (nach acht Monaten noch rund drei Viertel, nie unter gut der Hälfte). Aus den Titeln
der Signale entsteht ein Profil: Genres, Stichworte, Regie (bei Serien die Erfinder), die ersten fünf Darsteller,
Jahrzehnt und Originalsprache. Häufige Genres wie „Drama“ zählen dabei weniger als seltene. Was ein Konto unter „Dein
Geschmack“ stummschaltet, zählt nicht mehr mit.

### Wie ein Titel bewertet wird

**Punktzahl = α · Geschmack · (0,45 + 0,55 · Qualität) + (1 − α) · Qualität.** α wächst mit dem, was über das Konto
bekannt ist: α = n / (n + 12), höchstens 0,5; n ist die Summe der starken positiven Gewichte (gesehen, Herz, „Gefällt
mir“, angefragt) plus die Hälfte der Gewichte von „Nicht meins“. Ein neues Konto ohne Signale bekommt also Vorschläge
allein nach Qualität; mit jedem Signal bestimmt der Geschmack mehr, aber nie allein. Der Geschmacksanteil hängt selbst
von der Qualität ab, damit ein mittelmäßiger, aber ähnlicher Titel nicht vor einem klar besseren steht, der auch passt.

Im Geschmacks-Check gewählte „Welten“ (Genres) zählen als ausdrückliche Vorliebe für das Genre, etwa so stark wie zwei
gemochte Titel daraus, für jedes Genre gleich. Zu n tragen sie gedämpft bei: je Welt wie zwei Signale mit Gewicht 2,
alle Welten zusammen höchstens wie vier (α höchstens 0,40 allein durch Welten).

- **Geschmack** (0–1): Nähe der Genres zum Profil (34 %); wie viele und wie starke Signale den Titel empfohlen haben,
  nach den Empfehlungslisten von TMDb (36 %); Treffer bei Regie, Darstellern und Stichworten (20 %); Jahrzehnt (10 %).
- **Qualität** (0–1): zu drei Vierteln die Bewertung bei TMDb, geglättet – wenige Stimmen ziehen zur Mitte; Serien
  werden dabei um 0,6 Punkte niedriger angesetzt, weil TMDb sie durchweg höher bewertet als Filme. Zu einem Viertel die
  Bekanntheit nach der Zahl der Stimmen (logarithmisch: 100 Stimmen 0, 100 000 Stimmen 1) – was viele bewertet haben,
  haben auch viele gesehen. Die Werte (α höchstens 0,5, ein Viertel Bekanntheit, Serienabzug 0,6) sind mit dem
  Messlauf (`discover/eval`) an einem echten Konto abgestimmt; mehr Bekanntheit traf kaum besser, machte die
  Empfehlungen aber zur Bestsellerliste.
- **Sprache:** Titel in einer Originalsprache, in der das Konto nie etwas schaut, werden um gut ein Viertel abgewertet;
  ausgenommen sind Englisch, die Sprache der Oberfläche und Filme einer Regie, der das Konto folgt.
- **Formate:** Animation, Dokumentation, Reality und Ähnliches werden um bis zu ein Fünftel abgewertet, solange nichts
  davon unter den Signalen des Kontos ist.
- **Untergrenzen:** Fehlende Titel brauchen ein Plakat, mindestens 200 Stimmen und eine Bewertung ab 6,3 (Serien: 100
  Stimmen, ab 6,8); nicht jugendfreie Titel fallen heraus. Für Kommendes gilt die Untergrenze bei Stimmen und Bewertung
  nicht, für fehlende Teile angefangener Filmreihen eine niedrigere, für in den letzten drei Monaten Erschienenes ein
  Viertel der Stimmen.
- **Abwechslung:** höchstens zwei Titel derselben Filmreihe oder Regie dicht hintereinander, Genres gemischt. Die
  Reihe „Für dich ausgewählt“ (`top10`) folgt dagegen der reinen Rangfolge. Ein guter Titel abseits der Vorlieben steht
  bewusst auf einem der hinteren Plätze von „Für dich“ auf der Startseite und in der Reihe „Mal was anderes“. Eine Seite
  zeigt höchstens 14 Reihen; gestrichen wird von hinten, „Demnächst“ und „Mal was anderes“ zuletzt. Was einem Konto
  mehrfach gezeigt wurde, ohne dass es reagiert hat, rückt nach hinten (nach 60 Tagen vergessen). Die Reihenfolge
  verschiebt sich von Tag zu Tag leicht und bleibt innerhalb eines Tages gleich.

Die Übereinstimmung in Prozent (60–99) ergibt sich aus der Punktzahl im Vergleich zum Bestand des Kontos; bei einem
Konto ohne Signale sagt sie deshalb nur etwas über die Qualität.

### Woher die Kandidaten kommen

Alles über Seerr: Empfehlungen zu bis zu 70 Signalen (die stärkere Hälfte nach Gewicht, der Rest über die Zeit
gestreut, damit auch früher Geschautes zählt; bei den 20 stärksten auch die zweite Seite und „Ähnliches“); je
Lieblings-Genre die bestbewerteten und die neuen, beliebten Titel; Werke von Regisseuren (zuerst) und Darstellern aus
mindestens zwei starken Signalen; Titel zu wiederkehrenden Stichworten; die bekanntesten und die am besten bewerteten
Titel in den Originalsprachen, die das Konto außer Englisch schaut (schon ab einem kleinen Anteil); je Jahrzehnt, das
das Konto schaut, Bekanntes mit ordentlicher Bewertung und die am besten bewerteten mit mittlerer Bekanntheit; gerade
Angesagtes und Kommendes; eine zeitlose Bestenliste in zwei Stufen; alle Teile der Filmreihen, aus denen das Konto
etwas gesehen hat (bis zu 30 Reihen); die Liste der Anfragen. Je Konto bleiben höchstens rund 3500 Kandidaten. Für die besten holt das
Plugin die Details (Stichworte, Regie, Besetzung), je Berechnung für bis zu 60 weitere. Es sind nie mehr als vier
Abrufe gleichzeitig unterwegs; Listen werden für alle Konten gemeinsam einige Stunden bis Tage zwischengespeichert.
Fällt Seerr aus, pausiert das Plugin die Abrufe und liefert solange den letzten Stand.

Ob ein Titel vorhanden ist, entscheidet allein Jellyfin (über die TMDb-Nummer), nicht Seerr. Vorhandene, noch nicht
gesehene Titel stehen nur in der Reihe „Schon da“.

### Unterwegs zu dir: der Stand angefragter Titel

Was ein Konto angefragt hat, steht mit seinem Stand in der Reihe „Unterwegs zu dir“ (direkt nach der ersten Reihe,
schon ab einem Titel) und auf der Startseite; neu Angekommenes dort unter „Neu für dich“, bis es gesehen ist, längstens
14 Tage. Seerr sagt, was angefragt ist und was gerade lädt; ob ein Titel da ist und ob das Konto ihn gesehen hat,
entscheidet Jellyfin. Ein Titel gehört einem Konto, wenn der Seerr-Nutzer der Anfrage dieselbe Jellyfin-Kennung hat
oder wenn das Konto ihn über das Theme angefragt hat.

Die Phasen in der Reihenfolge der Liste: lädt (mit Prozent und Restzeit), wird eingeordnet, Warteschlange, teilweise
da (Serien: „Staffel 2 · 2 von 10 Folgen da · nächste am 15. Nov.“), wird gesucht (nach zwei Tagen: „Noch nichts
gefunden – Suche läuft weiter“), geplant (Ruhezeit der Download-Dienste, siehe Einstellungen), noch nicht erschienen
(„Erscheint voraussichtlich am 8. Dez.“, „Noch im Kino“, „Startet am 25. Dez.“), wartet auf Freigabe, da, abgelehnt.
Ob es einen Titel schon für zu Hause gibt, liest das Plugin aus den Terminen bei TMDb (frühester digitaler oder
Disc-Termin; ohne solchen gilt ein Film 90 Tage nach Kinostart als „im Kino“). Diese Termine holt es nur für offene
Titel ohne Download, höchstens einmal am Tag und im Hintergrund; bis sie da sind, steht „Wird gesucht“.

Laden die Download-Dienste nachts nicht, lässt sich das in den Einstellungen unter „Ruhezeit der Download-Dienste“
angeben (von/bis in der Zeit des Servers, dazu ein Kennzeichen im Namen von Qualitätsprofilen, die erst nach der
nächsten Ruhezeit laden). Statt „Wird gesucht“ steht dann „Startet heute gegen 07:30“ bzw. „Startet morgen gegen …“.

**Qualitätsprofil nach Originalsprache.** Unter „Qualitätsprofil nach Originalsprache“ lassen sich Regeln eintragen, eine
je Zeile: `Sprache=Textstück im Profilnamen`, `*` für alle übrigen Sprachen, z. B.

```
de=[German]
en=[German]
*=[OG + EN]
```

Dann wählt das Plugin bei jeder Anfrage über das Theme das erste Profil, dessen Name das Textstück enthält (Radarr für
Filme, Sonarr für Serien), und das Theme fragt nicht mehr nach dem Profil. In der Ruhezeit nimmt es außerdem die Variante
mit dem Kennzeichen (z. B. „… (morgen)“), sofern es eine gibt. Ein ausdrücklich mitgeschicktes Profil bleibt, nur nachts
gilt auch dafür die spätere Variante. Meldet Seerr eine Anfrage in der Ruhezeit als fehlgeschlagen, weil die Dienste
schlafen, zeigt der Stand „Startet … gegen …“ statt „Fehlgeschlagen“.

Die Liste der Anfragen holt das Plugin höchstens alle zehn Sekunden bei Seerr, gleich wie viele Konten wie oft
nachfragen; die Antwort nennt mit `poll`, nach wie vielen Sekunden sich Nachfragen lohnt (10 beim Laden, 30 bei Suche,
Warteschlange und Einordnen, 300 bei Wartendem, 0 sonst).

### Was gespeichert wird und wo

Im Ordner `CinemaGlass-discover` neben den Plugin-Einstellungen
(`<Jellyfin-Konfiguration>/plugins/configurations/CinemaGlass-discover/`):

| Datei | Inhalt |
|---|---|
| `<Kennung des Kontos>.json` | je Konto, wenige Kilobyte: Rückmeldungen (Titelnummer, Art, Zeitpunkt, wo gegeben), stummgeschaltete Merkmale, gewählte Welten, im Geschmacks-Check übersprungene Titel, wie oft welcher Titel gezeigt wurde, über das Theme gestellte Anfragen (Titelnummer, Zeitpunkt), seit wann angefragte Titel da sind und ob der Geschmacks-Check gemacht ist |
| `cache-details.json` | für alle Konten gemeinsam: verdichtete Angaben zu Titeln aus Seerr (Genres, Stichworte, Regie, Besetzung, Name, Kurztext, Termine). Enthält nichts über Konten und darf jederzeit gelöscht werden; höchstens 2000 Titel |

Anfragen über das Theme laufen bei Seerr unter einem festen Seerr-Konto; welches Jellyfin-Konto einen Titel wollte, steht
deshalb nur in der Datei des Kontos. Alles andere (Listen, Kandidaten, Profil) liegt nur im Arbeitsspeicher und ist
nach einem Neustart weg. Wer die Daten eines Kontos löschen will, löscht dessen Datei, solange Jellyfin steht. Aus dem
Gesehenen und den Favoriten selbst legt das Plugin keine Kopie an.

### Schnittstelle

Das Theme holt alles unter `/CinemaGlass/discover` ab. Jede Adresse verlangt eine Anmeldung und gilt für das
angemeldete Konto; `lang` ist `de` oder `en`.

| Adresse | Zweck |
|---|---|
| `GET discover?type=all\|movie\|tv&genre=<TMDb-Nummer>&lang=` | die Seite: `status` (`ready`, `building`, `empty`), großes Bild, Reihen, Genres zur Auswahl |
| `GET discover/home` | eine Reihe „Für dich“ für die Startseite, dazu `requests` („Unterwegs zu dir“) und `arrived` („Neu für dich“) |
| `GET discover/requests` | Stand der angefragten Titel des Kontos: `active`, `poll` und je Titel `request` mit `phase`, `percent`, `eta`, `text`, `short`, `availableFrom` … |
| `GET discover/similar?key=movie:603` | „Könnte dir auch gefallen“ zu einem Titel, nur Fehlendes |
| `GET discover/taste?groups=genre:878,genre:53` | Geschmacks-Check: `groups` (16 Welten mit je drei Plakaten, für alle gleich) und 48 bekannte Titel, über Genres, Jahrzehnte und Lager gestreut, ohne Bewertetes, Übersprungenes und Gesehenes; mit `groups` zu rund zwei Dritteln aus den genannten Welten; nach erledigtem Check halb nah am Geschmack, halb aus Bereichen mit wenig Signalen. Jeder Abruf nach neuen Antworten liefert neue Titel |
| `POST discover/taste/groups` | `{ "features": [ "genre:878" ] }` – die gewählten Welten (ersetzt die frühere Wahl, leer löscht sie) |
| `GET discover/profile` | „Dein Geschmack“: Genres, Personen, Jahrzehnte, Stichworte und alle Rückmeldungen |
| `POST discover/feedback` | `{ "source": "card", "items": [ { "key": "movie:603", "action": "like" } ] }` – `like`, `dislike`, `hide`, `clear` oder `skip` („Kenne ich nicht“: kein Signal, der Titel wird im Geschmacks-Check 180 Tage nicht mehr gefragt) |
| `POST discover/profile/mute` | `{ "feature": "genre:878", "muted": true }` |
| `POST discover/impressions` | `{ "keys": [ "movie:603" ] }` – was das Konto tatsächlich gesehen hat |
| `GET discover/debug?userId=&lang=&top=&find=&build=1` | nur für Administratoren: Signale mit Gewichten, Profil, Kandidaten je Quelle, Zeiten je Schritt und die besten Kandidaten mit den Bestandteilen ihrer Punktzahl; mit `requests=1` stattdessen der Stand der Anfragen (`at=2026-01-31T23:30` stellt die Uhrzeit, `refresh=1` holt die Termine neu) |
| `GET discover/eval?userId=&holdout=10&folds=3&set=alphaMax:0.5,heart:2&watched=1` | nur für Administratoren: Messlauf. Die jüngsten `holdout` positiven Signale (gesehen, Herz, „Gefällt mir“, Serie zu Ende) werden zurückgehalten, als hätte es sie nie gegeben; mit dem übrigen Verlauf entstehen Vorrat und Rangfolge wie im Betrieb (ohne tägliche Verschiebung und ohne Abzug für Gezeigtes). Ergebnis: Treffer unter den ersten 10/50/100, Anteil im Vorrat, mittlerer und mittlerer Rang (Median), MRR mit Standardfehler – für das Plugin und auf demselben Vorrat nur nach Qualität, nur nach Beliebtheit und nur nach Stimmen –, dazu je Titel der Rang. `folds` verschiebt das Fenster nach hinten und fasst zusammen; `set` überschreibt Gewichte für diesen Lauf; `watched=1` lässt „Gefällt mir“ aus dem Geschmacks-Check außen vor. Läuft im Hintergrund: derselbe Abruf liefert erst `status: "running"` mit `progress`, dann das Ergebnis; ändert nichts am Konto |
| `GET discover/eval/sweep?userId=&holdout=10&folds=3` | dasselbe für eine feste Reihe von Varianten (α-Grenze, Wachstum von α, Kopplung an die Qualität, Gewicht von Gesehen und Herz, Serien-, Format- und Sprachabzug) auf demselben Vorrat je Durchgang, mit Zahl der Titel, die je Variante besser bzw. schlechter stehen |

Ist „Entdecken“ aus oder Seerr nicht eingerichtet, antwortet die Seite mit `status: "empty"`, `error: "off"`; ist Seerr
nicht erreichbar und gibt es noch keinen Stand, mit `error: "seerr"`.

## Selbst bauen

Gebraucht wird nur Docker. Das Plugin wird gegen die Bibliotheken des eigenen Servers gebaut:

```sh
mkdir -p plugin/lib
for f in MediaBrowser.Common MediaBrowser.Controller MediaBrowser.Model Jellyfin.Data Jellyfin.Database.Implementations; do
  docker cp <jellyfin-container>:/jellyfin/$f.dll plugin/lib/
done
sh plugin/build.sh
```

Das Ergebnis `plugin/out/Jellyfin.Plugin.CinemaGlass.dll` in einen Ordner `plugins/CinemaGlass_<version>/`
im Konfigurationsverzeichnis von Jellyfin legen und den Server neu starten.
`sh plugin/package.sh` baut zusätzlich das Paket für den Katalog und trägt es in `manifest.json` ein.

## Entfernen

Dashboard → Plugins → Cinema Glass → deinstallieren, Jellyfin neu starten. Zurück bleiben nur die Ordner
`CinemaGlass-discover` (Rückmeldungen der Konten, siehe oben) und `CinemaGlass-logos` (eigene Zeichen) neben den
Plugin-Einstellungen; wer sie nicht mehr braucht, löscht sie von Hand.

## Aufbau

| Pfad | Inhalt |
|---|---|
| `css/appletv.css`, `js/*.js` | Aussehen und Funktionen – dieselben Dateien wie bei der Handinstallation |
| `plugin/src/Web/InjectStartupFilter.cs` | hängt Stylesheet und Skript in die Startseite der Weboberfläche und trägt das Wiedergabe-Plugin in deren Plugin-Liste (`config.json`) ein |
| `plugin/src/Web/CinemaGlassController.cs` | liefert beides aus (`/CinemaGlass/theme.css`, `/CinemaGlass/client.js`) und leitet an Seerr/MDBList weiter |
| `plugin/src/Discover/` | „Entdecken“: Schnittstelle (`DiscoverController`), Ablauf und Zwischenstände je Konto (`DiscoverService`), Abrufe bei Seerr (`SeerrClient`, `Builder`), Bibliothek und Signale (`LibraryReader`, `Profile`), Bewertung (`Scorer`), Reihen und Begründungen (`Pages`, `Texts`), Zustand der Konten (`UserState`) |
| `plugin/src/Configuration/` | Einstellungen und Einstellungsseite |

Die Schalter erreichen das Skript als `window.__cinemaGlass.features`. Ohne Plugin fehlt dieses Objekt, dann ist alles an.
