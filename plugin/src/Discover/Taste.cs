using System.Globalization;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>
/// Die „Welten“ des Geschmacks-Checks: Genres, Film und Serie zusammengelegt. Reihenfolge und Plakate sind fest und für
/// alle Konten gleich; eine Vorauswahl nach Konto würde nur bestätigen, was die Rangfolge ohnehin schon annimmt.
/// </summary>
internal static class Worlds
{
    public static readonly int[] Genres = { 28, 878, 14, 53, 80, 9648, 18, 35, 10749, 27, 16, 10751, 99, 36, 37, 10402 };

    /// <summary>Gewicht einer gewählten Welt für ihr Genre: etwa so viel wie zwei gemochte Titel dieses Genres einbringen.</summary>
    public const float GenreWeight = 4f;

    /// <summary>Für die Sicherheit über den Geschmack zählt eine Welt wie zwei Signale …</summary>
    public const int SignalsEach = 2;

    /// <summary>… alle Welten zusammen aber höchstens wie vier: Ein paar Klicks sind noch kein belegter Geschmack.</summary>
    public const int SignalsMax = 4;

    /// <summary>Gewicht eines solchen Signals: zwischen einem gesehenen und einem gemochten Titel.</summary>
    public const float SignalWeight = 2f;

    public static string Feature(int genre) => "genre:" + genre.ToString(CultureInfo.InvariantCulture);

    /// <summary>Liest eine Auswahl (<c>genre:878</c>). Unbekanntes und Doppeltes fällt weg, die Reihenfolge ist die feste.</summary>
    public static int[] Parse(IEnumerable<string>? features)
    {
        var wanted = new HashSet<int>();
        foreach (var feature in features ?? Array.Empty<string>())
        {
            var text = feature?.Trim() ?? string.Empty;
            if (text.StartsWith("genre:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(text.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var genre))
            {
                wanted.Add(genre);
            }
        }

        return Genres.Where(wanted.Contains).ToArray();
    }
}

/// <summary>Ein „Lager“: eine Liste sehr bekannter Titel einer Art, aus der der Geschmacks-Check einige Plätze füllt.</summary>
/// <param name="Id">Kennung, unter der die Liste geführt wird.</param>
/// <param name="Path">Abruf bei Seerr, ohne Seitenzahl.</param>
/// <param name="Tv">Ob die Liste Serien enthält.</param>
/// <param name="Genre">Das Genre, für das die Liste steht (0 = keines): Titel, die es vorn führen, sind typischer.</param>
/// <param name="Avoid">Genre, das in diesem Lager nicht gemeint ist (0 = keines).</param>
internal sealed record TasteCamp(string Id, string Path, bool Tv, int Genre = 0, int Avoid = 0);

/// <summary>Wie viele Plätze ein Lager füllt und ob es zu einer gewählten Welt gehört (sonst ist es Kontrast).</summary>
internal sealed record TasteSlot(TasteCamp Camp, int Slots, bool Focus = false);

/// <summary>
/// Wählt die Titel für den Geschmacks-Check. Gefragt wird nicht nach den 48 beliebtesten Filmen (die mag fast jeder, die
/// Antwort sagt wenig), sondern quer durch Genres, Jahrzehnte und „Lager“: sehr bekannte Titel, zu denen viele eine
/// Meinung haben, darunter bewusst solche, an denen sich Geschmäcker scheiden (Horror, Musical, Anime, Western …).
/// </summary>
internal static class TastePicker
{
    public const int Count = 48;

    /// <summary>Mit gewählten Welten stammen rund zwei Drittel der Titel aus ihnen, der Rest ist Kontrast.</summary>
    private const int FromWorlds = 32;

    /// <summary>
    /// Mehr Superhelden kommen nicht in die Auswahl: Sie führen fast jede Liste nach Stimmen an (Action, Abenteuer,
    /// Science-Fiction, Serien) und nähmen sonst ein Fünftel der Plätze ein, obwohl sie nur ein Lager sind.
    /// </summary>
    private const int MaxHeroes = 3;

    // Stichworte bei TMDb für Lager, die kein Genre sind.
    private static readonly TasteCamp Superhero = new("kw:superhero", "discover/movies?keywords=9715&sortBy=vote_count.desc", false);
    private static readonly TasteCamp Musical = new("kw:musical", "discover/movies?keywords=4344&sortBy=vote_count.desc", false, 0, 16);
    private static readonly TasteCamp Anime = new("kw:anime", "discover/movies?keywords=210024&sortBy=vote_count.desc", false);
    private static readonly TasteCamp AnimeSeries = new("kw:anime-tv", "discover/tv?keywords=210024&sortBy=vote_count.desc", true);
    private static readonly TasteCamp AllSeries = new("tv", "discover/tv?sortBy=vote_count.desc", true);

    /// <summary>Nur zum Erkennen: Serien mit Superhelden (die Filme stehen im Lager <see cref="Superhero"/>).</summary>
    private static readonly TasteCamp SuperheroSeries = new("kw:superhero-tv", "discover/tv?keywords=9715&sortBy=vote_count.desc", true);
    private static readonly TasteCamp Older = new("years:1970", "discover/movies?primaryReleaseDateGte=1970-01-01&primaryReleaseDateLte=1989-12-31&sortBy=vote_count.desc", false);
    private static readonly TasteCamp Nineties = new("years:1990", "discover/movies?primaryReleaseDateGte=1990-01-01&primaryReleaseDateLte=1999-12-31&sortBy=vote_count.desc", false);
    private static readonly TasteCamp Noughties = new("years:2000", "discover/movies?primaryReleaseDateGte=2000-01-01&primaryReleaseDateLte=2009-12-31&sortBy=vote_count.desc", false);

    // Ohne gewählte Welten: Plätze je Lager, zusammen 48. Große Genres bekommen mehr, jedes Lager mindestens einen.
    private static readonly TasteSlot[] Spread =
    {
        new(Movies(28), 2), new(Movies(878), 3), new(Movies(14), 2), new(Movies(53), 2), new(Movies(80), 2), new(Movies(9648), 1), new(Movies(18), 3), new(Movies(35), 3),
        new(Movies(10749), 2), new(Movies(27), 2), new(Movies(16), 2), new(Movies(10751), 1), new(Movies(99), 1), new(Movies(36), 1), new(Movies(37), 1), new(Movies(10402), 1),
        new(Movies(12), 1), new(Movies(10752), 1), new(Superhero, 2), new(Musical, 1), new(Anime, 1), new(AnimeSeries, 1), new(AllSeries, 9), new(Older, 1), new(Nineties, 1), new(Noughties, 1),
    };

    // Kontrast zu gewählten Welten, in dieser Reihenfolge je ein Platz: zuerst, woran sich Geschmäcker am ehesten scheiden.
    private static readonly TasteCamp[] Contrast =
    {
        Superhero, Movies(27), Movies(35), Movies(10749), AnimeSeries, Movies(18), AllSeries, Movies(878), Movies(37), Musical, Movies(28), Movies(16),
        AllSeries, Movies(53), Movies(99), Movies(14), Movies(80), Movies(10751), Older, Movies(36), Movies(10402), Movies(9648), AllSeries, Movies(12),
        Movies(10752), Anime, Nineties, Noughties,
    };

    // Wörter, an denen sich eine Filmreihe nicht erkennen lässt.
    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "the", "der", "die", "das", "und", "and", "eine", "einer", "marvel", "marvels", "from", "with", "that", "this", "eines", "mein", "meine",
    };

    // Wie sich die Auswahl über die Jahrzehnte verteilen soll: bis 1989, 1990er, 2000er, 2010er, ab 2020.
    private static readonly float[] DecadeShare = { 0.12f, 0.17f, 0.23f, 0.29f, 0.19f };

    public static TasteCamp Movies(int genre) =>
        new("genre:" + Num(genre), "discover/movies?genre=" + Num(genre) + "&sortBy=vote_count.desc", false, genre);

    /// <summary>Die Serien einer Welt, sofern es dafür bei TMDb ein passendes Serien-Genre gibt.</summary>
    public static TasteCamp? Series(int genre) => GenreMap.TvListGenre(genre) is > 0 and var tv
        ? new TasteCamp("tv:" + Num(genre), "discover/tv?genre=" + Num(tv) + "&sortBy=vote_count.desc", true, genre)
        : null;

    /// <summary>Listen, die zusätzlich zu denen des Plans gebraucht werden (zum Erkennen der Superhelden).</summary>
    public static IEnumerable<TasteCamp> Guards => new[] { Superhero, SuperheroSeries };

    /// <summary>Welche Lager wie viele Plätze füllen: breit gestreut oder, mit gewählten Welten, zu zwei Dritteln aus diesen.</summary>
    public static List<TasteSlot> Plan(int[] worlds)
    {
        if (worlds.Length == 0)
        {
            return Spread.ToList();
        }

        var plan = new List<TasteSlot>();
        void Add(TasteCamp camp, int slots, bool focus = true)
        {
            var at = plan.FindIndex(p => p.Camp.Id == camp.Id);
            if (at >= 0)
            {
                plan[at] = plan[at] with { Slots = plan[at].Slots + slots };
            }
            else if (slots > 0)
            {
                plan.Add(new TasteSlot(camp, slots, focus));
            }
        }

        var own = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < worlds.Length; i++)
        {
            var genre = worlds[i];
            var slots = (FromWorlds / worlds.Length) + (i < FromWorlds % worlds.Length ? 1 : 0);

            // Was zur Welt gehört, aber kein eigenes Genre ist, bekommt darin seinen Platz.
            var extra = genre switch { 28 => Superhero, 10402 => Musical, 16 => Anime, _ => null };
            if (extra is not null && slots >= 3)
            {
                Add(extra, 1);
                slots--;
            }

            if (genre == 16 && slots >= 3)
            {
                Add(AnimeSeries, 1);
                slots--;
            }

            var series = Series(genre);
            var seriesSlots = series is null ? 0 : (int)MathF.Round(slots * 0.3f);
            if (series is not null)
            {
                Add(series, seriesSlots);
            }

            Add(Movies(genre), slots - seriesSlots);
        }

        own.UnionWith(plan.Select(p => p.Camp.Id));
        var left = Count - plan.Sum(p => p.Slots);
        foreach (var camp in Contrast)
        {
            if (left <= 0)
            {
                break;
            }

            // Was schon zu einer gewählten Welt gehört, ist kein Kontrast.
            if (own.Contains(camp.Id) || (camp.Genre > 0 && Array.IndexOf(worlds, camp.Genre) >= 0))
            {
                continue;
            }

            Add(camp, 1, false);
            left--;
        }

        return plan;
    }

    /// <summary>
    /// Zum Verfeinern nach erledigtem Check: die Hälfte aus den Genres, die das Konto am liebsten hat (dort trennt ein Urteil
    /// Gutes von sehr Gutem), die andere aus denen, zu denen es noch am wenigsten Signale gibt.
    /// </summary>
    public static List<TasteSlot> Refine(Profile profile)
    {
        var plan = new List<TasteSlot>();
        var near = profile.TopGenres.Where(g => Array.IndexOf(Worlds.Genres, g) >= 0 || g is 12 or 10752).Take(4).ToList();
        foreach (var genre in near)
        {
            var slots = Count / 2 / near.Count;
            var series = Series(genre);
            var seriesSlots = series is null || plan.Any(p => p.Camp.Id == series.Id) ? 0 : slots / 3;
            if (seriesSlots > 0)
            {
                plan.Add(new TasteSlot(series!, seriesSlots, true));
            }

            plan.Add(new TasteSlot(Movies(genre), slots - seriesSlots, true));
        }

        var left = Count - plan.Sum(p => p.Slots);
        var far = Worlds.Genres.Concat(new[] { 12, 10752 }).Where(g => !near.Contains(g))
            .OrderBy(g => Math.Abs(profile.GenreRaw.GetValueOrDefault(g))).ToList();
        var special = new[] { AllSeries, Superhero, Musical, AnimeSeries, Older, Nineties };
        for (var round = 0; left > 0 && round < 4; round++)
        {
            foreach (var camp in far.Take(10).Select(Movies).Concat(round == 0 ? special : Array.Empty<TasteCamp>()))
            {
                if (left <= 0)
                {
                    break;
                }

                var at = plan.FindIndex(p => p.Camp.Id == camp.Id);
                if (at >= 0)
                {
                    plan[at] = plan[at] with { Slots = plan[at].Slots + 1 };
                }
                else
                {
                    plan.Add(new TasteSlot(camp, 1));
                }

                left--;
            }
        }

        return plan;
    }

    /// <summary>
    /// Füllt die Plätze. Innerhalb eines Lagers zählt zuerst die Bekanntheit; dazu kommt, ob der Titel für das Lager
    /// typisch ist, ob er die Meinungen teilt (viele Stimmen bei nur mittlerer bis guter Bewertung) und ob sein Jahrzehnt
    /// in der Auswahl noch fehlt. Aus einer Filmreihe kommt höchstens ein Titel.
    /// </summary>
    /// <param name="plan">Lager und Plätze.</param>
    /// <param name="lists">Die Listen der Lager, bekannteste Titel zuerst.</param>
    /// <param name="usable">Ob ein Titel für das Konto in Frage kommt (nicht schon bewertet oder gesehen).</param>
    /// <param name="collection">Nummer der Filmreihe eines Titels, soweit bekannt (sonst 0).</param>
    /// <param name="fill">Bleiben Plätze frei, weil ein Lager erschöpft ist: mit den bekanntesten übrigen Titeln auffüllen.</param>
    public static List<Title> Pick(IReadOnlyList<TasteSlot> plan, IReadOnlyDictionary<string, List<Title>> lists, Func<Title, bool> usable, Func<Title, int> collection, bool fill)
    {
        var taken = new HashSet<MediaKey>();
        var series = new HashSet<string>(StringComparer.Ordinal);
        var decades = new int[DecadeShare.Length];
        var favorites = 0;
        var heroes = 0;
        var picks = new List<(Title Title, float Position, bool Focus)>(Count);
        var heroKeys = new HashSet<MediaKey>();
        foreach (var guard in Guards)
        {
            if (lists.TryGetValue(guard.Id, out var known))
            {
                heroKeys.UnionWith(known.Select(t => t.Key));
            }
        }

        bool Free(Title t)
        {
            if (taken.Contains(t.Key) || series.Contains(Franchise(t)) || (heroes >= MaxHeroes && heroKeys.Contains(t.Key)))
            {
                return false;
            }

            var col = collection(t);
            return col <= 0 || !series.Contains("col:" + Num(col));
        }

        void Take(Title t, float position, bool focus)
        {
            taken.Add(t.Key);
            series.Add(Franchise(t));
            if (collection(t) is > 0 and var col)
            {
                series.Add("col:" + Num(col));
            }

            decades[Bucket(t.Year)]++;
            if (t.Rating >= 8.3f)
            {
                favorites++;
            }

            if (heroKeys.Contains(t.Key))
            {
                heroes++;
            }

            picks.Add((t, position, focus));
        }

        // Kleine Lager zuerst: Sie haben die wenigste Auswahl, die großen kommen mit dem Rest zurecht. Die Superhelden
        // kommen ganz zuerst, damit ihre wenigen Plätze nicht schon von anderen Lagern verbraucht sind.
        var order = plan.Select((p, i) => (p.Camp, p.Slots, p.Focus, Index: i))
            .OrderBy(p => p.Camp.Id == Superhero.Id ? 0 : 1).ThenBy(p => p.Slots).ThenBy(p => p.Index).ToList();
        foreach (var (camp, slots, focus, index) in order)
        {
            if (!lists.TryGetValue(camp.Id, out var list))
            {
                continue;
            }

            var candidates = list.Where(t => usable(t) && (camp.Avoid == 0 || Array.IndexOf(t.Genres, camp.Avoid) < 0)).Take(40).ToList();

            // Wo ein Lager in der fertigen Liste auftaucht: gleichmäßig verteilt, jedes Lager etwas versetzt, damit weder
            // alle Serien noch alle Nischen beieinander stehen.
            var phase = (0.5f + (index * 0.618034f)) % 1f;
            for (var slot = 0; slot < slots; slot++)
            {
                Title? best = null;
                var bestScore = float.MinValue;
                for (var i = 0; i < candidates.Count; i++)
                {
                    var t = candidates[i];
                    if (!Free(t))
                    {
                        continue;
                    }

                    var score = (1f - (i / 30f)) + Typical(t, camp) + Divisive(t, slot, favorites) + Balance(t, decades);
                    if (score > bestScore)
                    {
                        best = t;
                        bestScore = score;
                    }
                }

                if (best is null)
                {
                    break;
                }

                Take(best, (slot + phase) / slots, focus);
            }
        }

        // Reicht ein Lager nicht (alles schon bewertet), füllen die bekanntesten übrigen Titel auf.
        if (fill && picks.Count < Count)
        {
            var rest = plan.SelectMany(p => lists.TryGetValue(p.Camp.Id, out var l) ? l : new List<Title>()).Where(usable).OrderByDescending(t => t.Votes);
            foreach (var t in rest)
            {
                if (picks.Count >= Count)
                {
                    break;
                }

                if (Free(t))
                {
                    Take(t, 1f + (picks.Count / 1000f), false);
                }
            }
        }

        var ordered = picks.OrderBy(p => p.Position).ToList();
        if (!ordered.Any(p => p.Focus))
        {
            return ordered.Select(p => p.Title).ToList();
        }

        // Mit Schwerpunkt: im Wechsel mit dem Rest, im selben Verhältnis wie die Plätze (zwei zu eins bei gewählten Welten).
        var own = new Queue<Title>(ordered.Where(p => p.Focus).Select(p => p.Title));
        var other = new Queue<Title>(ordered.Where(p => !p.Focus).Select(p => p.Title));
        var result = new List<Title>(ordered.Count);
        var run = Math.Max(1, (int)MathF.Round(own.Count / (float)Math.Max(1, other.Count)));
        while (own.Count > 0 || other.Count > 0)
        {
            for (var i = 0; i < run && own.Count > 0; i++)
            {
                result.Add(own.Dequeue());
            }

            if (other.Count > 0)
            {
                result.Add(other.Dequeue());
            }
        }

        return result;
    }

    /// <summary>Drei Plakate je Welt: die bekanntesten Filme, die das Genre vorn führen; kein Plakat steht in zwei Welten.</summary>
    public static Dictionary<int, List<string>> Posters(IReadOnlyDictionary<string, List<Title>> lists)
    {
        var result = new Dictionary<int, List<string>>();
        var taken = new HashSet<MediaKey>();
        foreach (var genre in Worlds.Genres)
        {
            var posters = new List<string>(3);
            var series = new HashSet<string>(StringComparer.Ordinal);
            if (lists.TryGetValue(Movies(genre).Id, out var list))
            {
                // Erst die Titel, die das Genre an erster Stelle führen, dann an zweiter, dann alle übrigen.
                for (var pass = 0; pass < 3 && posters.Count < 3; pass++)
                {
                    foreach (var t in list)
                    {
                        if (posters.Count >= 3)
                        {
                            break;
                        }

                        var place = Array.IndexOf(t.Genres, genre);
                        if (t.Poster is null || t.Adult || taken.Contains(t.Key) || (pass < 2 && place != pass) || series.Contains(Franchise(t)))
                        {
                            continue;
                        }

                        taken.Add(t.Key);
                        series.Add(Franchise(t));
                        posters.Add(t.Poster);
                    }
                }
            }

            result[genre] = posters;
        }

        return result;
    }

    /// <summary>Wie typisch ein Titel für sein Lager ist: Das Genre des Lagers steht bei ihm vorn.</summary>
    private static float Typical(Title t, TasteCamp camp)
    {
        if (camp.Genre == 0)
        {
            return 0f;
        }

        for (var i = 0; i < t.Genres.Length; i++)
        {
            if (t.Genres[i] == camp.Genre || (camp.Tv && GenreMap.TvListGenre(camp.Genre) == t.Genres[i]))
            {
                return i == 0 ? 0.35f : i == 1 ? 0.2f : 0.05f;
            }
        }

        return -0.2f;
    }

    /// <summary>
    /// Titel mit sehr vielen Stimmen, aber nur mittlerer bis guter Bewertung teilen die Meinungen und sagen deshalb mehr
    /// als Allzeit-Lieblinge. Der erste Platz eines Lagers darf ein Liebling sein, insgesamt aber nicht zu viele.
    /// </summary>
    private static float Divisive(Title t, int slot, int favorites)
    {
        if (t.Rating is >= 6f and <= 7.6f)
        {
            return slot == 0 ? 0.15f : 0.45f;
        }

        if (t.Rating >= 8.3f)
        {
            return favorites >= Count / 4 ? -0.6f : slot == 0 ? 0f : -0.25f;
        }

        return 0f;
    }

    private static float Balance(Title t, int[] decades)
    {
        var bucket = Bucket(t.Year);
        var target = DecadeShare[bucket] * Count;
        var have = decades[bucket];
        return have < target ? 0.3f * (1f - (have / target)) : have >= target + 1f ? -0.4f : 0f;
    }

    private static int Bucket(int year) => year <= 0 ? 3 : year < 1990 ? 0 : year < 2000 ? 1 : year < 2010 ? 2 : year < 2020 ? 3 : 4;

    /// <summary>Das erste aussagekräftige Wort des Namens: Daran lassen sich die Teile einer Reihe meist erkennen.</summary>
    private static string Franchise(Title t)
    {
        foreach (var raw in t.Name.Split(new[] { ' ', '-', ':', ',', '.', '\'', '’', '–', '!', '?' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw.ToLowerInvariant();
            if (word.Length >= 4 && !Filler.Contains(word))
            {
                return word;
            }
        }

        return t.Name.ToLowerInvariant();
    }

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);
}
