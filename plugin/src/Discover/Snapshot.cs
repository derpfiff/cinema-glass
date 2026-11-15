namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>Die Werkliste einer Person aus dem Profil.</summary>
internal sealed record PersonList(string Feature, string Name, bool Directs, MediaKey[] Keys);

/// <summary>Eine Filmreihe, aus der das Konto schon etwas gesehen hat, mit allen Teilen.</summary>
internal sealed record CollectionList(int Id, string Name, MediaKey[] Parts);

/// <summary>Der Kandidatenbestand eines Kontos: was die Quellen geliefert haben, vor jeder Bewertung.</summary>
internal sealed class Pool
{
    public Dictionary<MediaKey, Candidate> Items { get; } = new();

    /// <summary>Je Signal die Titel, die Seerr dazu empfiehlt, in dessen Reihenfolge.</summary>
    public Dictionary<MediaKey, MediaKey[]> Recs { get; } = new();

    public Dictionary<string, PersonList> People { get; } = new(StringComparer.Ordinal);

    public Dictionary<int, CollectionList> Collections { get; } = new();

    /// <summary>Genres, zu denen schon eigene Listen geholt wurden.</summary>
    public HashSet<int> Genres { get; } = new();

    /// <summary>Davon die, die das Konto eigens aufgerufen hat; sie werden bei jeder Berechnung wieder mitgenommen.</summary>
    public HashSet<int> Deep { get; } = new();

    public Dictionary<string, int> SourceCounts { get; } = new(StringComparer.Ordinal);

    public Candidate Get(MediaKey key)
    {
        if (!Items.TryGetValue(key, out var c))
        {
            c = new Candidate { Key = key };
            Items[key] = c;
        }

        return c;
    }

    /// <summary>Eine Kopie zum Erweitern: der veröffentlichte Bestand wird nie verändert, während Antworten daraus entstehen.</summary>
    public Pool Copy()
    {
        var copy = new Pool();
        foreach (var item in Items)
        {
            copy.Items[item.Key] = item.Value;
        }

        foreach (var item in Recs)
        {
            copy.Recs[item.Key] = item.Value;
        }

        foreach (var item in People)
        {
            copy.People[item.Key] = item.Value;
        }

        foreach (var item in Collections)
        {
            copy.Collections[item.Key] = item.Value;
        }

        foreach (var item in SourceCounts)
        {
            copy.SourceCounts[item.Key] = item.Value;
        }

        copy.Genres.UnionWith(Genres);
        copy.Deep.UnionWith(Deep);
        return copy;
    }
}

/// <summary>Die Anfragen bei Seerr zum Zeitpunkt des letzten Abrufs.</summary>
internal sealed class RequestsInfo
{
    public static readonly RequestsInfo Empty = new();

    public DateTime At { get; init; }

    public List<SeerrRequest> Items { get; init; } = new();
}

/// <summary>Messwerte einer Berechnung, für die Diagnose.</summary>
internal sealed class BuildInfo
{
    public DateTime Started { get; set; }

    public string Reason { get; set; } = string.Empty;

    public Dictionary<string, long> Timings { get; } = new(StringComparer.Ordinal);

    public long SeerrRequests { get; set; }

    public long SeerrFailed { get; set; }

    public long CacheHits { get; set; }

    public int DetailsFetched { get; set; }

    public int PoolSize { get; set; }
}

/// <summary>Ein bewerteter Titel mit allen Bestandteilen seiner Punktzahl.</summary>
internal sealed class Scored
{
    public MediaKey Key { get; init; }

    public Title Title { get; init; } = null!;

    public Detail? Detail { get; init; }

    public Candidate? Candidate { get; init; }

    public LibEntry? Lib { get; init; }

    public float Score { get; set; }

    public float Taste { get; set; }

    public float Quality { get; set; }

    public float Genre { get; set; }

    public float Support { get; set; }

    public float People { get; set; }

    public float Decade { get; set; }

    public float LangFactor { get; set; } = 1f;

    public float FormatFactor { get; set; } = 1f;

    public float ShownFactor { get; set; } = 1f;

    public float Jitter { get; set; } = 1f;

    /// <summary>Welche Person aus dem Profil trifft (Anzeigename) und ob als Regie.</summary>
    public string? PersonHit { get; set; }

    public bool PersonDirects { get; set; }

    /// <summary>Das Genre des Titels, das am besten zum Profil passt (0 = keines).</summary>
    public int GenreHit { get; set; }

    /// <summary>Wie gut der Titel als bewusste Abwechslung taugt (0 = gar nicht).</summary>
    public float Explore { get; set; }

    public int Match { get; set; }

    public bool Upcoming { get; init; }

    /// <summary>Fehlt dem Konto, ist erschienen und darf empfohlen werden.</summary>
    public bool Missing { get; set; }

    /// <summary>Für die Abwechslung: Filmreihe bzw. Regie, erst bei Bedarf bestimmt.</summary>
    public string? FranchiseKey { get; set; }

    public string? DirectorKey { get; set; }

    public int[] Genres => Detail?.Genres is { Length: > 0 } g ? g : Title.Genres;
}

/// <summary>Der fertige Stand eines Kontos: daraus entstehen alle Antworten, ohne weitere Abrufe.</summary>
internal sealed class Snapshot
{
    public DateTime BuiltAt { get; init; }

    public int Day { get; init; }

    public string Lang { get; init; } = "de";

    public long StateRevision { get; init; }

    public Profile Profile { get; init; } = null!;

    public LibrarySnapshot Lib { get; init; } = LibrarySnapshot.Empty;

    public Pool Pool { get; init; } = null!;

    public StateView State { get; init; } = StateView.Empty;

    /// <summary>Fehlende, erschienene Titel, beste zuerst.</summary>
    public Scored[] Ranked { get; init; } = Array.Empty<Scored>();

    public Scored[] Upcoming { get; init; } = Array.Empty<Scored>();

    /// <summary>Vorhandene, noch nicht angefangene Titel, beste zuerst.</summary>
    public Scored[] Library { get; init; } = Array.Empty<Scored>();

    /// <summary>Alles, was für dieses Konto als angefragt gilt (auch Anfragen anderer): steht in keiner Empfehlung.</summary>
    public HashSet<MediaKey> OpenRequests { get; init; } = new();

    public Dictionary<MediaKey, Scored> ByKey { get; init; } = new();

    public BuildInfo Info { get; init; } = new();
}

/// <summary>Was eine Bewertung braucht.</summary>
internal sealed class RankInput
{
    public Guid User { get; init; }

    public string Lang { get; init; } = "de";

    public Pool Pool { get; init; } = null!;

    public LibrarySnapshot Lib { get; init; } = LibrarySnapshot.Empty;

    public RequestsInfo Requests { get; init; } = RequestsInfo.Empty;

    /// <summary>Über das Theme gestellte Anfragen aller Konten (wem eine Anfrage gehört).</summary>
    public IReadOnlyDictionary<MediaKey, List<(Guid User, long At)>> Theme { get; init; } = new Dictionary<MediaKey, List<(Guid User, long At)>>();

    public StateView State { get; init; } = StateView.Empty;

    public Catalog Catalog { get; init; } = null!;

    public DateTime BuiltAt { get; init; }

    public BuildInfo Info { get; init; } = new();
}
