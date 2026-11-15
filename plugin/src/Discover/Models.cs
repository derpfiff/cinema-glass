using System.Globalization;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>Ein Titel bei TMDb: Film oder Serie samt Nummer, geschrieben als <c>movie:603</c> oder <c>tv:1399</c>.</summary>
internal readonly record struct MediaKey(bool Tv, int Id)
{
    public string Type => Tv ? "tv" : "movie";

    public override string ToString() => Type + ":" + Id.ToString(CultureInfo.InvariantCulture);

    public static bool TryParse(string? text, out MediaKey key)
    {
        key = default;
        if (string.IsNullOrEmpty(text) || text.Length > 24)
        {
            return false;
        }

        var cut = text.IndexOf(':', StringComparison.Ordinal);
        if (cut <= 0)
        {
            return false;
        }

        var type = text.AsSpan(0, cut);
        bool tv;
        if (type.Equals("tv", StringComparison.OrdinalIgnoreCase))
        {
            tv = true;
        }
        else if (type.Equals("movie", StringComparison.OrdinalIgnoreCase))
        {
            tv = false;
        }
        else
        {
            return false;
        }

        if (!int.TryParse(text.AsSpan(cut + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            return false;
        }

        key = new MediaKey(tv, id);
        return true;
    }
}

/// <summary>
/// Was die Listen von Seerr über einen Titel sagen, auf das Nötige gekürzt. Diese Datensätze teilen sich alle Konten;
/// sie werden nach dem Anlegen nicht mehr verändert, nur ersetzt.
/// </summary>
internal sealed class Title
{
    public MediaKey Key { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Overview { get; init; }

    public string? Poster { get; init; }

    public string? Backdrop { get; init; }

    /// <summary>Genre-Nummern von TMDb, wie geliefert (Filme und Serien haben teils eigene).</summary>
    public int[] Genres { get; init; } = Array.Empty<int>();

    public float Rating { get; init; }

    public int Votes { get; init; }

    public float Popularity { get; init; }

    /// <summary>Erscheinungstag als Zahl JJJJMMTT, 0 wenn unbekannt.</summary>
    public int Date { get; init; }

    public string Lang { get; init; } = string.Empty;

    public bool Adult { get; init; }

    /// <summary>Name und Text stammen aus einem Abruf, der die gewünschte Sprache sicher beachtet.</summary>
    public bool Sure { get; init; }

    public int Year => Date / 10000;
}

/// <summary>Name und Beschreibung eines Titels in einer Sprache.</summary>
internal sealed class DetailText
{
    public string Name { get; set; } = string.Empty;

    public string? Overview { get; set; }

    public string? Collection { get; set; }
}

/// <summary>
/// Die Merkmale eines Titels aus den Seerr-Details. Eine Antwort ist rund 150 KB groß; behalten wird nur, was die
/// Rechnung und die Anzeige brauchen (wenige hundert Byte). Personen und Stichworte stehen als Nummern hier, ihre Namen
/// einmal in den Tabellen des Zwischenspeichers.
/// </summary>
internal sealed class Detail
{
    public MediaKey Key { get; set; }

    /// <summary>Abrufzeit in Unix-Sekunden.</summary>
    public long At { get; set; }

    /// <summary>Zuletzt gebraucht (Unix-Sekunden); danach wird aufgeräumt.</summary>
    public long Used { get; set; }

    public int[] Genres { get; set; } = Array.Empty<int>();

    public int[] Keywords { get; set; } = Array.Empty<int>();

    /// <summary>Regie (Filme) bzw. Erfinder der Serie.</summary>
    public int[] Directors { get; set; } = Array.Empty<int>();

    /// <summary>Die ersten fünf Darsteller.</summary>
    public int[] Cast { get; set; } = Array.Empty<int>();

    public string Lang { get; set; } = string.Empty;

    public int Date { get; set; }

    public float Rating { get; set; }

    public int Votes { get; set; }

    public float Popularity { get; set; }

    public int Runtime { get; set; }

    /// <summary>Schon ausgestrahlte Staffeln ohne Specials.</summary>
    public int Seasons { get; set; }

    /// <summary>
    /// Je Staffel (Stelle = Staffelnummer) die Zahl der schon ausgestrahlten Folgen; <c>null</c> bei Filmen und bei
    /// Einträgen aus der Zeit, bevor das festgehalten wurde.
    /// </summary>
    public int[]? Episodes { get; set; }

    /// <summary>Je Staffel die Zahl aller Folgen, auch der noch nicht ausgestrahlten.</summary>
    public int[]? EpisodeCounts { get; set; }

    /// <summary>Die nächste Folge laut TMDb: Tag (JJJJMMTT), Staffel und Nummer; 0, wenn keine angekündigt ist.</summary>
    public int NextDate { get; set; }

    public int NextSeason { get; set; }

    public int NextEpisode { get; set; }

    /// <summary>
    /// Film: frühester Termin für zu Hause (digital oder auf Disc) über alle Länder als JJJJMMTT, 0 = keiner bekannt;
    /// <c>null</c> = nicht erhoben (Serien und ältere Einträge).
    /// </summary>
    public int? HomeRelease { get; set; }

    public int Collection { get; set; }

    public bool Adult { get; set; }

    public string? Poster { get; set; }

    public string? Backdrop { get; set; }

    public Dictionary<string, DetailText> Texts { get; set; } = new(StringComparer.Ordinal);

    public int Year => Date / 10000;
}

/// <summary>Woher ein Kandidat stammt; ein Titel kommt oft aus mehreren Quellen.</summary>
[Flags]
internal enum Src
{
    None = 0,
    Recommendation = 1,
    Similar = 2,
    GenreTop = 4,
    GenreNew = 8,
    Person = 16,
    Keyword = 32,
    Trending = 64,
    Upcoming = 128,
    Classic = 256,
    Collection = 512,
    Gem = 1024,
    Deep = 2048,

    /// <summary>Bekanntes in einer Originalsprache, die das Konto schaut (außer Englisch).</summary>
    Language = 4096,

    /// <summary>Gut Bewertetes aus einem Jahrzehnt, das das Konto schaut.</summary>
    Decade = 8192,

    /// <summary>Von Freunden auf Letterboxd gut bewertet oder gemocht.</summary>
    Friends = 16384,
}

/// <summary>Ein Kandidat im Bestand eines Kontos: nur, was die Quellen über ihn sagen. Die Punktzahl steht woanders.</summary>
internal sealed class Candidate
{
    public MediaKey Key { get; init; }

    public Src Sources { get; set; }

    /// <summary>Summe der Beiträge aller Signale, die diesen Titel empfohlen haben.</summary>
    public float Support { get; set; }

    /// <summary>Das Signal mit dem größten Beitrag und dessen Anteil.</summary>
    public MediaKey Because { get; set; }

    public float BecauseShare { get; set; }

    /// <summary>Platz in der Liste „gerade angesagt“, 0 = nicht dabei.</summary>
    public int TrendRank { get; set; }

    /// <summary>Filmreihe, aus der der Titel dem Konto fehlt.</summary>
    public int Collection { get; set; }

    /// <summary>Person, über deren Werkliste der Titel kam (Merkmal wie im Profil), und ob sie Regie führte.</summary>
    public string? Person { get; set; }

    public bool PersonDirects { get; set; }

    /// <summary>Stichwort, über dessen Liste der Titel kam.</summary>
    public int Keyword { get; set; }
}

internal enum SignalKind
{
    Favorite,
    Rewatched,
    Seen,
    Partial,
    Like,
    Requested,
    Unseen,
    Abandoned,
    Hidden,
    Dislike,
}

/// <summary>Ein Hinweis auf den Geschmack des Kontos: ein Titel mit Gewicht (negativ = abgelehnt).</summary>
internal sealed class Signal
{
    public MediaKey Key { get; init; }

    public string Name { get; set; } = string.Empty;

    public SignalKind Kind { get; init; }

    /// <summary>Gewicht aus der Tabelle, vor dem Abklingen über die Zeit.</summary>
    public float Base { get; init; }

    public float Weight { get; set; }

    public DateTime? When { get; init; }

    /// <summary>Starke Signale bestimmen, wie viel der Geschmack gegenüber der Qualität zählt.</summary>
    public bool Strong { get; init; }

    public LibEntry? Lib { get; init; }
}

/// <summary>Ein Film oder eine Serie aus der Bibliothek, so wie das Konto ihn sieht.</summary>
internal sealed class LibEntry
{
    public MediaKey Key { get; init; }

    public Guid ItemId { get; init; }

    public string Name { get; init; } = string.Empty;

    public int Year { get; init; }

    public bool Visible { get; init; }

    public bool Played { get; set; }

    public int PlayCount { get; set; }

    public bool Favorite { get; set; }

    public DateTime? LastPlayed { get; set; }

    /// <summary>Film: Anteil der gesehenen Laufzeit, wenn angefangen.</summary>
    public float Progress { get; set; }

    public int EpisodesPlayed { get; set; }

    public int EpisodesTotal { get; set; }

    public int Seasons { get; set; }

    public int RuntimeMinutes { get; init; }

    public string[] Genres { get; init; } = Array.Empty<string>();

    public int Collection { get; init; }

    public string[] Directors { get; set; } = Array.Empty<string>();

    public string[] Cast { get; set; } = Array.Empty<string>();

    public DateTime Added { get; init; }

    /// <summary>Nur beim gezielten Nachschlagen für angefragte Serien: je Staffel, was da ist und was davon gesehen wurde.</summary>
    public Dictionary<int, SeasonHave>? SeasonsHave { get; set; }

    /// <summary>Noch nichts davon gesehen und nicht angefangen: Kandidat für die Reihe „Schon da“.</summary>
    public bool Untouched => Key.Tv ? EpisodesPlayed == 0 && !Played : !Played && Progress <= 0f;
}

/// <summary>Was von einer Staffel in der Bibliothek liegt: Folgen, davon vom Konto gesehene, und wann die jüngste dazukam.</summary>
internal readonly record struct SeasonHave(int Count, int Played, DateTime Newest);

/// <summary>Die Bibliothek aus Sicht eines Kontos zum Zeitpunkt der letzten Berechnung.</summary>
internal sealed class LibrarySnapshot
{
    public static readonly LibrarySnapshot Empty = new();

    public Dictionary<MediaKey, LibEntry> Items { get; } = new();

    /// <summary>Prüfsumme über alles, was die Signale bestimmt; ändert sie sich nicht, bleibt die Rangfolge stehen.</summary>
    public ulong Fingerprint { get; set; }

    public int Movies { get; set; }

    public int Series { get; set; }
}

/// <summary>Ein laufender Download zu einer Anfrage, wie Seerr ihn von Radarr oder Sonarr weiterreicht.</summary>
internal sealed class DownloadEntry
{
    public double Size { get; init; }

    public double SizeLeft { get; init; }

    /// <summary>Stand laut Download-Dienst, klein geschrieben: <c>downloading</c>, <c>queued</c>, <c>paused</c>, <c>completed</c>, <c>failed</c> …</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Voraussichtliches Ende; aus <c>estimatedCompletionTime</c>, sonst aus dem Abrufzeitpunkt plus <c>timeLeft</c>.</summary>
    public DateTime? Eta { get; init; }

    /// <summary>Bei Serien die Staffel der Folge, sonst -1.</summary>
    public int Season { get; init; } = -1;

    /// <summary>Fertig geladen, wartet nur noch auf das Einordnen.</summary>
    public bool Done => Status is "completed" or "importpending" or "importing" or "imported"
        || (Size > 0 && SizeLeft <= 0 && Status is not ("queued" or "paused" or "delay"));

    public bool Loading => !Done && Status == "downloading";

    public bool Failed => !Done && Status is "failed" or "failedpending";
}

/// <summary>Eine Anfrage aus der Liste von Seerr.</summary>
internal sealed class SeerrRequest
{
    public int Id { get; init; }

    public MediaKey Key { get; init; }

    /// <summary>1 wartet auf Freigabe, 2 freigegeben, 3 abgelehnt, 4 fehlgeschlagen, 5 erledigt.</summary>
    public int Status { get; init; }

    /// <summary>Stand des Titels laut Seerr: 2 ausstehend, 3 in Arbeit, 4 teilweise, 5 vorhanden.</summary>
    public int MediaStatus { get; init; }

    public DateTime Created { get; init; }

    public DateTime Updated { get; init; }

    /// <summary>Wann der Titel laut Seerr in die Bibliothek kam (kann das Dateidatum sein und damit weit zurückliegen).</summary>
    public DateTime? MediaAdded { get; init; }

    /// <summary>Jellyfin-Kennung des anfragenden Seerr-Nutzers (32 Hex-Zeichen), wenn Seerr sie kennt.</summary>
    public string? JellyfinUser { get; init; }

    public bool Is4k { get; init; }

    /// <summary>Dienst und Qualitätsprofil der Anfrage; -1 = Voreinstellung.</summary>
    public int ServerId { get; init; } = -1;

    public int ProfileId { get; init; } = -1;

    /// <summary>Angefragte Staffeln mit ihrem Stand bei Seerr (5 = vollständig da).</summary>
    public (int Number, int Status)[] Seasons { get; init; } = Array.Empty<(int, int)>();

    public DownloadEntry[] Downloads { get; init; } = Array.Empty<DownloadEntry>();

    /// <summary>Noch nicht entschieden oder erledigt: wartet, ist freigegeben oder hängt.</summary>
    public bool Pending => Status is 1 or 2 or 4;
}
