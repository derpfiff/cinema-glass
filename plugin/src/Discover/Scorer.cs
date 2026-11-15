namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>
/// Bewertet die Kandidaten eines Kontos. Punktzahl = α · Geschmack + (1 − α) · Qualität; α wächst mit den starken
/// Signalen, ohne Signale entscheidet allein die Qualität. Läuft ohne Abrufe und in wenigen Millisekunden, damit nach
/// jeder Rückmeldung sofort neu geordnet werden kann.
/// </summary>
internal static class Scorer
{
    // Anteile am Geschmack.
    public const float GenreShare = 0.34f;
    public const float SupportShare = 0.36f;
    public const float PeopleShare = 0.20f;
    public const float DecadeShare = 0.10f;

    /// <summary>
    /// Anteil des Geschmackswerts, der unabhängig von der Qualität zählt; der Rest wächst mit ihr. So steht Ähnliches,
    /// aber Mittelmäßiges nicht vor klar Besserem, das auch passt (im Messlauf waren 0,2 bis 0,7 gleichauf).
    /// </summary>
    public const float TasteFloor = 0.45f;

    /// <summary>
    /// Abzug für Serien: TMDb bewertet sie durchweg höher als Filme, und gerade viel gestimmte Jugendserien stünden sonst
    /// allein ihrer Bewertung wegen vor Filmen, die zum Konto passen. Im Messlauf (zurückgehaltene Signale) stand mit 0,6
    /// statt 0,4 die Mehrzahl der Titel etwas weiter vorn.
    /// </summary>
    public const float TvRatingOffset = 0.6f;

    /// <summary>
    /// Bei dieser Summe an Empfehlungs-Beiträgen ist der Bestandteil „empfohlen von“ zu rund zwei Dritteln gesättigt:
    /// ein Favorit allein reicht dafür nicht, ein Favorit und zwei gesehene Titel schon.
    /// </summary>
    public const float SupportScale = 3f;

    /// <summary>
    /// Anteil der Bekanntheit (Zahl der Stimmen) an der Qualität; der Rest ist die geglättete Bewertung. Was man später
    /// schaut, ist meist auch bekannt: Im Messlauf (zurückgehaltene Signale) lag der Median-Rang mit einem Viertel bei 147
    /// statt 176 ohne. Die Hälfte traf im Rahmen der Messunsicherheit gleich gut, machte die ersten Plätze aber zur
    /// Bestsellerliste; gewollt sind persönliche Entdeckungen.
    /// </summary>
    public const float VotesShare = 0.25f;

    /// <summary>Wie stark die Bekanntheit die ganze Punktzahl mitbestimmt (0 = gar nicht, 1 = Punktzahl mal Bekanntheit).</summary>
    public const float VotesFactor = 0f;

    /// <summary>
    /// Bekanntheit auf 0–1 nach der Zahl der Stimmen, logarithmisch: 100 Stimmen → 0, 1000 → 0,33, 10 000 → 0,67,
    /// 100 000 → 1. Was viele bewertet haben, haben auch viele gesehen – ein Vorwissen dafür, dass man einen Titel schaut.
    /// </summary>
    public static float Known(int votes) => Math.Clamp((MathF.Log10(1f + Math.Max(0, votes)) - 2f) / 3f, 0f, 1f);

    /// <summary>Faktor für Originalsprachen, die das Konto nie schaut (außer Englisch und der Sprache der Oberfläche).</summary>
    public const float ForeignFactor = 0.72f;

    /// <summary>Größter Abzug für Formate (Animation, Doku, Reality …), die im Geschauten des Kontos gar nicht vorkommen.</summary>
    public const float FormatPenalty = 0.2f;

    // Untergrenzen für fehlende Titel: (Stimmen, Bewertung) für Filme und Serien.
    public const int MovieVotes = 200;
    public const float MovieRating = 6.3f;
    public const int TvVotes = 100;
    public const float TvRating = 6.8f;

    // Formate statt Inhalte: Wer nie Animation oder Dokus schaut, bekommt sie sonst allein wegen ihrer hohen Bewertungen.
    private static readonly int[] FormatGenres = { 16, 10751, 10762, 99, 10764, 10767, 10763, 10766, 10770 };

    /// <summary>Höchstens so viele Kandidaten je Konto bleiben im Bestand.</summary>
    public const int PoolLimit = 3500;

    public static int Today(DateTime now) => (now.Year * 10000) + (now.Month * 100) + now.Day;

    public static int DayNumber(DateTime now) => DateOnly.FromDateTime(now).DayNumber;

    /// <summary>
    /// Geglättete Bewertung auf 0–1: wenige Stimmen ziehen zur Mitte (als kämen 500 bzw. 300 Durchschnittsstimmen dazu),
    /// Serien sind bei TMDb durchweg höher bewertet. Die Abbildung ist um 7,0 herum am steilsten, weil dort der
    /// Unterschied zwischen „ordentlich“ und „gut“ liegt: 6,0 → 0,14 · 7,0 → 0,50 · 8,0 → 0,86.
    /// </summary>
    public static float Quality(float rating, int votes, bool tv) => Quality(rating, votes, tv, TvRatingOffset);

    public static float Quality(float rating, int votes, bool tv, float tvOffset)
    {
        var prior = tv ? 300f : 500f;
        var mean = tv ? 7.0f : 6.6f;
        var smooth = ((votes * rating) + (prior * mean)) / (votes + prior);
        if (tv)
        {
            smooth -= tvOffset;
        }

        return 1f / (1f + MathF.Exp(-1.8f * (smooth - 7f)));
    }

    /// <summary>Welche Anfragen dem Konto gehören (als Signal, mit Zeitpunkt) und welche Titel für irgendwen unterwegs sind.</summary>
    public static void Requests(Guid user, RequestsInfo requests, StateView state, IReadOnlyDictionary<MediaKey, List<(Guid User, long At)>> theme, LibrarySnapshot lib, DateTime now, out Dictionary<MediaKey, DateTime> own, out HashSet<MediaKey> open)
    {
        var mine = RequestOwnership.For(user, requests, state, theme);
        own = new Dictionary<MediaKey, DateTime>(mine.Count);
        foreach (var m in mine)
        {
            own[m.Key] = m.Value.When;
        }

        // Was Jellyfin schon hat, ist nicht mehr unterwegs.
        open = RequestOwnership.Open(requests, mine, now);
        open.RemoveWhere(lib.Items.ContainsKey);
    }

    public static Snapshot Rank(RankInput input)
    {
        var now = DateTime.UtcNow;
        var local = DateTime.Now;
        var today = Today(local);
        var day = DayNumber(local);
        var lang = input.Lang;
        var catalog = input.Catalog;
        var state = input.State;
        var lib = input.Lib;

        Requests(input.User, input.Requests, state, input.Theme, lib, now, out var own, out var open);
        var tuning = input.Tuning ?? Tuning.Default;
        var signals = Profile.BuildSignals(lib, state, own, now, tuning);
        var profile = Profile.Build(signals, catalog, lang, state.Muted, state.Worlds, tuning);

        var bytes = input.User.ToByteArray();
        var seed = BitConverter.ToUInt64(bytes, 0) ^ BitConverter.ToUInt64(bytes, 8) ^ ((ulong)day * 0x9E3779B97F4A7C15UL);
        var ctx = new Context(profile, state, lang, seed, Catalog.Now, catalog) { Tuning = tuning };

        var ranked = new List<Scored>(input.Pool.Items.Count);
        var upcoming = new List<Scored>();
        var byKey = new Dictionary<MediaKey, Scored>(input.Pool.Items.Count + 64);
        foreach (var c in input.Pool.Items.Values)
        {
            if (lib.Items.ContainsKey(c.Key) || state.Feedback.ContainsKey(c.Key) || open.Contains(c.Key) || state.LetterboxdOwn.ContainsKey(c.Key))
            {
                continue;
            }

            var title = catalog.Get(lang, c.Key) ?? catalog.FromDetail(lang, c.Key);
            if (title is null || title.Adult || title.Poster is null)
            {
                continue;
            }

            var detail = catalog.Detail(c.Key);
            if (detail is not null && detail.Adult)
            {
                continue;
            }

            var date = title.Date > 0 ? title.Date : detail?.Date ?? 0;
            var isUpcoming = date > today || (date == 0 && (c.Sources & Src.Upcoming) != 0);
            if (!isUpcoming && !PassesFloor(title, c, today))
            {
                continue;
            }

            var s = new Scored { Key = c.Key, Title = title, Detail = detail, Candidate = c, Upcoming = isUpcoming, Missing = !isUpcoming };
            Score(s, ctx);
            byKey[c.Key] = s;
            (isUpcoming ? upcoming : ranked).Add(s);
        }

        ranked.Sort(ByScore);
        upcoming.Sort(ByScore);

        var library = new List<Scored>();
        foreach (var e in lib.Items.Values)
        {
            if (!e.Visible || !e.Untouched || state.Feedback.ContainsKey(e.Key) || state.LetterboxdOwn.ContainsKey(e.Key))
            {
                continue;
            }

            var detail = catalog.Detail(e.Key);
            var known = catalog.Get(lang, e.Key) ?? catalog.FromDetail(lang, e.Key);
            var genres = known?.Genres is { Length: > 0 } g ? g : detail?.Genres ?? GenreMap.FromNames(e.Genres, e.Key.Tv);
            // Der Name kommt aus der Bibliothek: so kennt das Konto den Titel.
            var title = new Title
            {
                Key = e.Key,
                Name = e.Name.Length > 0 ? e.Name : known?.Name ?? string.Empty,
                Overview = known?.Overview,
                Poster = known?.Poster ?? detail?.Poster,
                Backdrop = known?.Backdrop ?? detail?.Backdrop,
                Genres = genres,
                Rating = known?.Rating ?? detail?.Rating ?? 0f,
                Votes = known?.Votes ?? detail?.Votes ?? 0,
                Popularity = known?.Popularity ?? detail?.Popularity ?? 0f,
                Date = known?.Date > 0 ? known.Date : detail?.Date > 0 ? detail.Date : e.Year * 10000,
                Lang = known?.Lang ?? detail?.Lang ?? string.Empty,
                Sure = true,
            };
            var s = new Scored { Key = e.Key, Title = title, Detail = detail, Lib = e };
            Score(s, ctx);
            library.Add(s);
            byKey[e.Key] = s;
        }

        library.Sort(ByScore);

        // Übereinstimmung: die Spitze bei 98, der Bezugspunkt weit hinten bei 62, dazwischen leicht gebogen.
        var top = ranked.Count > 0 ? ranked[0].Score : 1f;
        var reference = ranked.Count > 0 ? ranked[Math.Min(ranked.Count - 1, Math.Max(60, (int)(ranked.Count * 0.4f)))].Score : 0f;
        var span = Math.Max(0.0001f, top - reference);
        foreach (var s in byKey.Values)
        {
            if (s.Upcoming)
            {
                // Noch ohne Bewertung und deshalb mit kleinerer Punktzahl: eigener Maßstab, sonst stünde überall der Mindestwert.
                s.Match = Math.Clamp((int)MathF.Round(60f + (38f * s.Score / Math.Max(0.0001f, top))), 60, 97);
                continue;
            }

            var rel = Math.Clamp((s.Score - reference) / span, 0f, 1f);
            s.Match = Math.Clamp((int)MathF.Round(62f + (36f * MathF.Pow(rel, 0.9f))), 60, 99);
        }

        return new Snapshot
        {
            BuiltAt = input.BuiltAt,
            Day = day,
            Lang = lang,
            StateRevision = state.Revision,
            Profile = profile,
            Lib = lib,
            Pool = input.Pool,
            State = state,
            Ranked = ranked.ToArray(),
            Upcoming = upcoming.ToArray(),
            Library = library.ToArray(),
            OpenRequests = open,
            ByKey = byKey,
            Info = input.Info,
        };
    }

    /// <summary>Bewertet einen einzelnen Titel außerhalb des Bestands (für „Könnte dir auch gefallen“).</summary>
    public static Scored ScoreLoose(Snapshot snapshot, Title title, Catalog catalog, Guid user, float support)
    {
        var bytes = user.ToByteArray();
        var seed = BitConverter.ToUInt64(bytes, 0) ^ BitConverter.ToUInt64(bytes, 8) ^ ((ulong)snapshot.Day * 0x9E3779B97F4A7C15UL);
        var ctx = new Context(snapshot.Profile, snapshot.State, snapshot.Lang, seed, Catalog.Now, catalog);
        snapshot.Pool.Items.TryGetValue(title.Key, out var known);
        var candidate = new Candidate
        {
            Key = title.Key,
            Support = Math.Max(support, known?.Support ?? 0f),
            Because = known?.Because ?? default,
            BecauseShare = known?.BecauseShare ?? 0f,
            Person = known?.Person,
            PersonDirects = known?.PersonDirects ?? false,
            Keyword = known?.Keyword ?? 0,
            Sources = known?.Sources ?? Src.None,
        };
        var s = new Scored { Key = title.Key, Title = title, Detail = catalog.Detail(title.Key), Candidate = candidate };
        Score(s, ctx);
        var top = snapshot.Ranked.Length > 0 ? snapshot.Ranked[0].Score : 1f;
        var reference = snapshot.Ranked.Length > 0 ? snapshot.Ranked[Math.Min(snapshot.Ranked.Length - 1, Math.Max(60, (int)(snapshot.Ranked.Length * 0.4f)))].Score : 0f;
        var rel = Math.Clamp((s.Score - reference) / Math.Max(0.0001f, top - reference), 0f, 1f);
        s.Match = Math.Clamp((int)MathF.Round(62f + (36f * MathF.Pow(rel, 0.9f))), 60, 99);
        return s;
    }

    /// <summary>
    /// Untergrenzen für fehlende Titel: zu wenige Stimmen oder eine schwache Bewertung fallen heraus. Fehlende Teile einer
    /// angefangenen Reihe und Werke einer geschätzten Person dürfen kleiner sein, und gerade erst Erschienenes hatte noch
    /// keine Zeit, Stimmen zu sammeln.
    /// </summary>
    public static bool PassesFloor(Title t, Candidate? c, int today)
    {
        var votes = t.Key.Tv ? TvVotes : MovieVotes;
        var rating = t.Key.Tv ? TvRating : MovieRating;
        if (c is not null && (c.Collection > 0 || (c.Sources & Src.Friends) != 0))
        {
            // Fehlende Teile einer Reihe und was Freunde auf Letterboxd gut fanden, dürfen auch kleiner sein.
            votes = 40;
            rating = 5.0f;
        }
        else
        {
            if (c?.Person is not null)
            {
                votes /= 2;
                rating -= 0.2f;
            }

            // Grob in Tagen gerechnet (Monat = 31 Tage); auf ein paar Tage kommt es hier nicht an.
            static int Days(int date) => (date / 10000 * 372) + (date / 100 % 100 * 31) + (date % 100);
            if (t.Date > 0 && Days(today) - Days(t.Date) is >= 0 and <= 90)
            {
                votes /= 4;
            }
        }

        return t.Votes >= votes && t.Rating >= rating;
    }

    private static int ByScore(Scored a, Scored b)
    {
        var c = b.Score.CompareTo(a.Score);
        return c != 0 ? c : a.Key.Id.CompareTo(b.Key.Id);
    }

    private static void Score(Scored s, Context ctx)
    {
        var p = ctx.Profile;
        var tune = ctx.Tuning;
        var title = s.Title;
        var detail = s.Detail;
        var c = s.Candidate;

        // Nähe der Genres zum Profil: Kosinus zwischen den Genres des Titels und den Vorlieben, auf 0–1 gelegt.
        var canonical = ctx.Canonical;
        GenreMap.Canonical(s.Genres, canonical);
        float dot = 0f, norm = 0f, bestGenre = 0f;
        foreach (var g in canonical)
        {
            norm += g.Part * g.Part;
            if (p.Genres.TryGetValue(g.Id, out var pref))
            {
                dot += g.Part * pref;
                if (g.Part * pref > bestGenre)
                {
                    bestGenre = g.Part * pref;
                    s.GenreHit = g.Id;
                }
            }
        }

        var cos = norm > 0f && p.GenreNorm > 0f ? dot / (MathF.Sqrt(norm) * p.GenreNorm) : 0f;
        s.Genre = Math.Clamp(0.5f + (0.5f * cos), 0f, 1f);

        // Wie viele und wie starke Signale den Titel empfohlen haben, sättigend.
        s.Support = c is not null && c.Support > 0f ? 1f - MathF.Exp(-c.Support / SupportScale) : 0f;

        // Regie, Darsteller, Stichworte.
        float people = 0f, keywords = 0f, bestPerson = 0f, director = 0f;
        void Person(string? name, bool directs)
        {
            if (name is null || !p.People.TryGetValue(name.Trim().ToLowerInvariant(), out var pref))
            {
                return;
            }

            var value = pref.Weight * (directs ? 1f : 0.6f);
            people += value;
            if (directs)
            {
                director = Math.Max(director, pref.Weight);
            }

            if (value > bestPerson)
            {
                bestPerson = value;
                s.PersonHit = pref.Name;
                s.PersonDirects = directs;
            }
        }

        if (p.People.Count > 0)
        {
            if (detail is not null)
            {
                foreach (var id in detail.Directors)
                {
                    Person(ctx.Catalog.Person(id), true);
                }

                var counted = 0;
                foreach (var id in detail.Cast)
                {
                    var before = people;
                    Person(ctx.Catalog.Person(id), false);
                    if (people > before && ++counted >= 2)
                    {
                        break;
                    }
                }
            }
            else if (s.Lib is not null)
            {
                foreach (var name in s.Lib.Directors)
                {
                    Person(name, true);
                }

                foreach (var name in s.Lib.Cast)
                {
                    Person(name, false);
                }
            }
            else if (c?.Person is not null)
            {
                Person(c.Person, c.PersonDirects);
            }
        }

        if (p.Keywords.Count > 0)
        {
            if (detail is not null)
            {
                foreach (var k in detail.Keywords)
                {
                    keywords += p.Keywords.GetValueOrDefault(k);
                }
            }
            else if (c is not null && c.Keyword > 0)
            {
                keywords = p.Keywords.GetValueOrDefault(c.Keyword);
            }
        }

        // Stichworte kennt nur, wer Details hat; sie zählen deshalb höchstens knapp halb so viel wie eine Person, damit die
        // Titel mit Details den übrigen nicht allein dadurch davonziehen.
        var peoplePart = 1f - MathF.Exp(-1.8f * people);
        var keywordPart = 1f - MathF.Exp(-0.5f * Math.Min(keywords, 4f));
        s.People = 1f - ((1f - peoplePart) * (1f - (0.45f * keywordPart)));

        // Jahrzehnt: das eigene, ersatzweise abgeschwächt die Nachbarn. Bewusst flach (nie unter 0,35): Aus wenigen Titeln
        // lässt sich eine Vorliebe für eine Zeit nur grob ablesen.
        var year = title.Year > 0 ? title.Year : detail?.Year ?? 0;
        if (p.Decades.Count == 0 || year < 1900)
        {
            s.Decade = 0.6f;
        }
        else
        {
            var decade = year / 10 * 10;
            var near = Math.Max(
                MathF.Sqrt(p.Decades.GetValueOrDefault(decade)),
                0.7f * MathF.Sqrt(Math.Max(p.Decades.GetValueOrDefault(decade - 10), p.Decades.GetValueOrDefault(decade + 10))));
            s.Decade = 0.35f + (0.65f * near);
        }

        s.Taste = (GenreShare * s.Genre) + (SupportShare * s.Support) + (PeopleShare * s.People) + (DecadeShare * s.Decade);

        if (s.Upcoming)
        {
            // Noch ohne Bewertung: stattdessen zählt zur Hälfte, wie sehr der Titel erwartet wird.
            var buzz = Math.Clamp(MathF.Log10(1f + title.Popularity) / 2.4f, 0f, 1f);
            s.Quality = (0.5f * Quality(title.Rating, title.Votes, title.Key.Tv, tune.TvRatingOffset)) + (0.5f * buzz);
        }
        else
        {
            s.Quality = Quality(title.Rating, title.Votes, title.Key.Tv, tune.TvRatingOffset);
            if (tune.VotesShare > 0f)
            {
                s.Quality = ((1f - tune.VotesShare) * s.Quality) + (tune.VotesShare * Known(title.Votes));
            }
        }

        // Sprachen, die das Konto nie schaut, deutlich abwerten; jedes positive Signal in der Sprache hebt das wieder auf.
        // Wer der Regie folgt, schaut ihre Filme auch in einer fremden Sprache.
        var language = detail?.Lang is { Length: > 0 } dl ? dl : title.Lang;
        if (language.Length > 0 && language != "en" && language != ctx.Lang)
        {
            var known = Math.Max(director, Math.Min(1f, p.Langs.GetValueOrDefault(language) / 2f));
            s.LangFactor = tune.ForeignFactor + ((1f - tune.ForeignFactor) * known);
        }

        // Formate, die im Geschauten nicht vorkommen: Abzug, der mit der Sicherheit über den Geschmack wächst und mit
        // jedem positiven Signal in diesem Format schwindet (ein gesehener Animationsfilm halbiert ihn etwa).
        var format = 0f;
        foreach (var g in s.Genres)
        {
            if (Array.IndexOf(FormatGenres, g) >= 0)
            {
                // „Kids“ gibt es nur bei Serien; im Profil steht dafür „Familie“.
                var seen = Math.Max(0f, p.GenreRaw.GetValueOrDefault(g == 10762 ? 10751 : g));
                format = Math.Max(format, 1f - Math.Min(1f, seen));
            }
        }

        s.FormatFactor = 1f - (tune.FormatPenalty * Math.Min(1f, p.N / 10f) * format);

        // Oft gezeigt, nie darauf reagiert: ab dem dritten Mal je 4 % nach hinten, höchstens 24 %; nach 60 Tagen vergessen.
        if (tune.Shown && ctx.State.Shown.TryGetValue(s.Key, out var shown) && ctx.NowUnix - shown.At < 60L * 86400L)
        {
            s.ShownFactor = 1f - Math.Min(0.24f, 0.04f * Math.Max(0, shown.Count - 2));
        }

        // Kleine Verschiebung, die von Konto, Titel und Tag abhängt: jeden Tag etwas anders, innerhalb des Tages gleich.
        s.Jitter = tune.Jitter ? 1f + (0.03f * Noise(ctx.Seed, s.Key)) : 1f;

        // Der Geschmacksanteil hängt selbst von der Qualität ab: Sehr Ähnliches, aber Mittelmäßiges soll nicht vor klar
        // Besserem stehen, das auch passt. Ein Titel mit Qualität 0 brächte nur 45 % seines Geschmackswerts ein.
        s.Score = ((p.Alpha * s.Taste * (tune.TasteFloor + ((1f - tune.TasteFloor) * s.Quality))) + ((1f - p.Alpha) * s.Quality))
            * s.LangFactor * s.FormatFactor * s.ShownFactor * s.Jitter;
        if (tune.VotesFactor > 0f && !s.Upcoming)
        {
            s.Score *= (1f - tune.VotesFactor) + (tune.VotesFactor * Known(title.Votes));
        }

        // „Mal was anderes“: gut, in vertrauter Sprache, aber abseits der Vorlieben und von niemandem empfohlen.
        if (!s.Upcoming && s.Lib is null && p.TopGenres.Count > 0 && cos is > -0.15f and < 0.2f && s.Support <= 0f && people <= 0f && s.LangFactor >= 1f && s.FormatFactor >= 1f && s.Quality >= 0.62f)
        {
            s.Explore = s.Quality * s.ShownFactor * s.Jitter;
        }
    }

    private static float Noise(ulong seed, MediaKey key)
    {
        var x = seed ^ ((ulong)(uint)key.Id * 0xBF58476D1CE4E5B9UL) ^ (key.Tv ? 0xD1B54A32D192ED03UL : 0UL);
        x ^= x >> 33;
        x *= 0xFF51AFD7ED558CCDUL;
        x ^= x >> 33;
        x *= 0xC4CEB9FE1A85EC53UL;
        x ^= x >> 33;
        return ((x >> 11) / (float)(1UL << 53) * 2f) - 1f;
    }

    private sealed class Context
    {
        public Context(Profile profile, StateView state, string lang, ulong seed, long nowUnix, Catalog catalog)
        {
            Profile = profile;
            State = state;
            Lang = lang;
            Seed = seed;
            NowUnix = nowUnix;
            Catalog = catalog;
        }

        public Profile Profile { get; }

        public StateView State { get; }

        public string Lang { get; }

        public ulong Seed { get; }

        public long NowUnix { get; }

        public Catalog Catalog { get; }

        public List<(int Id, float Part)> Canonical { get; } = new(8);

        public Tuning Tuning { get; init; } = Tuning.Default;
    }
}
