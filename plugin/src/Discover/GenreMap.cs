using System.Collections.Concurrent;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>
/// Die Genres von TMDb. Filme und Serien haben teils eigene Nummern („Sci-Fi &amp; Fantasy“ gibt es nur bei Serien);
/// gerechnet wird deshalb in den Film-Genres, Serien-Genres werden darauf abgebildet. So hilft ein gesehener
/// Science-Fiction-Film auch bei Serien.
/// </summary>
internal static class GenreMap
{
    // Nummer, deutsch, englisch, ungefährer Anteil unter allen Titeln (häufige Genres sagen weniger über den Geschmack).
    private static readonly (int Id, string De, string En, float Share)[] Movie =
    {
        (28, "Action", "Action", 0.19f), (12, "Abenteuer", "Adventure", 0.12f), (16, "Animation", "Animation", 0.07f),
        (35, "Komödie", "Comedy", 0.28f), (80, "Krimi", "Crime", 0.12f), (99, "Dokumentarfilm", "Documentary", 0.05f),
        (18, "Drama", "Drama", 0.37f), (10751, "Familie", "Family", 0.09f), (14, "Fantasy", "Fantasy", 0.08f),
        (36, "Historie", "History", 0.04f), (27, "Horror", "Horror", 0.11f), (10402, "Musik", "Music", 0.03f),
        (9648, "Mystery", "Mystery", 0.07f), (10749, "Liebesfilm", "Romance", 0.13f), (878, "Science Fiction", "Science Fiction", 0.09f),
        (10770, "TV-Film", "TV Movie", 0.02f), (53, "Thriller", "Thriller", 0.20f), (10752, "Kriegsfilm", "War", 0.03f),
        (37, "Western", "Western", 0.015f),
    };

    private static readonly (int Id, string De, string En, float Share)[] Tv =
    {
        (10759, "Action & Adventure", "Action & Adventure", 0.15f), (16, "Animation", "Animation", 0.07f), (35, "Komödie", "Comedy", 0.28f),
        (80, "Krimi", "Crime", 0.12f), (99, "Dokumentarfilm", "Documentary", 0.05f), (18, "Drama", "Drama", 0.37f),
        (10751, "Familie", "Family", 0.09f), (10762, "Kids", "Kids", 0.05f), (9648, "Mystery", "Mystery", 0.07f),
        (10763, "News", "News", 0.01f), (10764, "Reality", "Reality", 0.04f), (10765, "Sci-Fi & Fantasy", "Sci-Fi & Fantasy", 0.10f),
        (10766, "Soap", "Soap", 0.01f), (10767, "Talk", "Talk", 0.01f), (10768, "War & Politics", "War & Politics", 0.03f),
        (37, "Western", "Western", 0.015f),
    };

    // Serien-Genre -> Film-Genres mit Anteil. Was hier fehlt, bleibt, was es ist.
    private static readonly Dictionary<int, (int Id, float Part)[]> Expand = new()
    {
        [10759] = new[] { (28, 0.6f), (12, 0.6f) },
        [10765] = new[] { (878, 0.6f), (14, 0.6f) },
        [10768] = new[] { (10752, 0.8f) },
        [10762] = new[] { (10751, 0.9f) },
        [10766] = new[] { (18, 0.5f), (10749, 0.5f) },
    };

    // Film-Genre -> nächstliegendes Serien-Genre für die Listenabrufe (0 = keines).
    private static readonly Dictionary<int, int> ToTv = new()
    {
        [28] = 10759, [12] = 10759, [878] = 10765, [14] = 10765, [10752] = 10768, [36] = 10768, [53] = 9648, [27] = 9648,
        [16] = 16, [35] = 35, [80] = 80, [99] = 99, [18] = 18, [10751] = 10751, [9648] = 9648, [37] = 37,
        [10763] = 10763, [10764] = 10764, [10766] = 10766, [10767] = 10767,
    };

    // Schreibweisen aus Bibliotheken, die nicht von TMDb stammen.
    private static readonly (string Name, int Id)[] Aliases =
    {
        ("scifi", 878), ("sciencefiction", 878), ("romanze", 10749), ("romantik", 10749), ("liebe", 10749), ("krieg", 10752),
        ("dokumentation", 99), ("doku", 99), ("geschichte", 36), ("kinder", 10751), ("children", 10751), ("musical", 10402),
        ("suspense", 53), ("filmnoir", 80), ("anime", 16), ("komodie", 35), ("comedy", 35), ("crime", 80),
    };

    private static readonly Dictionary<int, float> Idf = BuildIdf();
    private static readonly Dictionary<string, int> ByName = BuildNames();
    private static readonly ConcurrentDictionary<string, int> Learned = new(StringComparer.Ordinal);

    /// <summary>Genres, mit denen ein Konto ohne Signale anfängt (breit gestreut, Reihenfolge = Reihenfolge der Auswahl).</summary>
    public static readonly int[] Defaults = { 28, 35, 18, 878, 53, 16, 12, 80, 14, 27, 10751, 10749 };

    public static IEnumerable<int> MovieIds => Movie.Select(g => g.Id);

    public static IEnumerable<int> TvIds => Tv.Select(g => g.Id);

    /// <summary>Wie viel ein Genre über den Geschmack aussagt: seltene Genres zählen mehr als „Drama“.</summary>
    public static float Weight(int canonical) => Idf.TryGetValue(canonical, out var w) ? w : 1f;

    public static bool IsMovieGenre(int id) => Array.FindIndex(Movie, g => g.Id == id) >= 0;

    public static bool IsTvGenre(int id) => Array.FindIndex(Tv, g => g.Id == id) >= 0;

    public static int TvListGenre(int canonical) => ToTv.TryGetValue(canonical, out var tv) ? tv : 0;

    /// <summary>Name eines Genres; Serien-Genres nur, wenn es die Nummer bei Filmen nicht gibt oder ausdrücklich gewünscht.</summary>
    public static string Name(int id, string lang, bool preferTv = false)
    {
        var en = lang == "en";
        if (!preferTv)
        {
            foreach (var g in Movie)
            {
                if (g.Id == id)
                {
                    return en ? g.En : g.De;
                }
            }
        }

        foreach (var g in Tv)
        {
            if (g.Id == id)
            {
                return en ? g.En : g.De;
            }
        }

        foreach (var g in Movie)
        {
            if (g.Id == id)
            {
                return en ? g.En : g.De;
            }
        }

        return string.Empty;
    }

    /// <summary>Für Überschriften und Begründungen: im Deutschen mit Bindestrich.</summary>
    public static string Nice(int id, string lang, bool preferTv = false)
    {
        var name = Name(id, lang, preferTv);
        return lang != "en" && name == "Science Fiction" ? "Science-Fiction" : name;
    }

    /// <summary>Verteilt die Genres eines Titels auf die Film-Genres, in denen gerechnet wird.</summary>
    public static void Canonical(int[] genres, List<(int Id, float Part)> into)
    {
        into.Clear();
        foreach (var g in genres)
        {
            if (Expand.TryGetValue(g, out var parts))
            {
                foreach (var p in parts)
                {
                    Add(into, p.Id, p.Part);
                }
            }
            else
            {
                Add(into, g, 1f);
            }
        }
    }

    /// <summary>Ob ein Titel zu einem gewählten Genre gehört; Film- und Serien-Nummern gelten über Kreuz.</summary>
    public static bool Matches(int[] titleGenres, int wanted)
    {
        foreach (var g in titleGenres)
        {
            if (g == wanted)
            {
                return true;
            }
        }

        var wantedParts = Expand.TryGetValue(wanted, out var w) ? w : null;
        foreach (var g in titleGenres)
        {
            if (wantedParts is not null)
            {
                foreach (var p in wantedParts)
                {
                    if (p.Id == g)
                    {
                        return true;
                    }
                }
            }

            if (Expand.TryGetValue(g, out var parts))
            {
                foreach (var p in parts)
                {
                    if (p.Id == wanted)
                    {
                        return true;
                    }

                    if (wantedParts is not null)
                    {
                        foreach (var q in wantedParts)
                        {
                            if (q.Id == p.Id)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>Genre-Namen aus der Bibliothek (in der Sprache der Bibliothek) auf Nummern abbilden.</summary>
    public static int[] FromNames(string[] names, bool tv)
    {
        if (names.Length == 0)
        {
            return Array.Empty<int>();
        }

        var ids = new List<int>(names.Length);
        foreach (var name in names)
        {
            var norm = Normalize(name);
            if (norm.Length == 0)
            {
                continue;
            }

            if (!Learned.TryGetValue((tv ? "t" : "m") + norm, out var id) && !Learned.TryGetValue((tv ? "m" : "t") + norm, out id)
                && !ByName.TryGetValue((tv ? "t" : "m") + norm, out id) && !ByName.TryGetValue("a" + norm, out id))
            {
                continue;
            }

            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids.ToArray();
    }

    /// <summary>Namen aus den Genre-Listen von Seerr dazulernen (für Bibliotheken in weiteren Sprachen).</summary>
    public static void Learn(bool tv, int id, string? name)
    {
        var norm = Normalize(name ?? string.Empty);
        if (norm.Length > 0 && id > 0)
        {
            Learned[(tv ? "t" : "m") + norm] = id;
        }
    }

    private static void Add(List<(int Id, float Part)> into, int id, float part)
    {
        for (var i = 0; i < into.Count; i++)
        {
            if (into[i].Id == id)
            {
                into[i] = (id, Math.Max(into[i].Part, part));
                return;
            }
        }

        into.Add((id, part));
    }

    private static string Normalize(string name)
    {
        Span<char> buffer = stackalloc char[Math.Min(name.Length, 48)];
        var n = 0;
        foreach (var ch in name.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (n >= buffer.Length)
            {
                break;
            }

            if (char.IsLetterOrDigit(ch))
            {
                buffer[n++] = char.ToLowerInvariant(ch);
            }
        }

        return new string(buffer[..n]);
    }

    private static Dictionary<int, float> BuildIdf()
    {
        var map = new Dictionary<int, float>();
        foreach (var g in Movie.Concat(Tv))
        {
            map[g.Id] = Math.Clamp((float)(Math.Log(1.0 / g.Share) / 2.3), 0.45f, 1.5f);
        }

        return map;
    }

    private static Dictionary<string, int> BuildNames()
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var g in Movie)
        {
            map["m" + Normalize(g.De)] = g.Id;
            map["m" + Normalize(g.En)] = g.Id;
            map.TryAdd("a" + Normalize(g.De), g.Id);
            map.TryAdd("a" + Normalize(g.En), g.Id);
        }

        foreach (var g in Tv)
        {
            map["t" + Normalize(g.De)] = g.Id;
            map["t" + Normalize(g.En)] = g.Id;
            map.TryAdd("a" + Normalize(g.De), g.Id);
            map.TryAdd("a" + Normalize(g.En), g.Id);
        }

        foreach (var a in Aliases)
        {
            map.TryAdd("a" + a.Name, a.Id);
        }

        return map;
    }
}
