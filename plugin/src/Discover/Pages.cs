using System.Globalization;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>
/// Baut aus dem fertigen Stand eines Kontos die Antworten: Seite mit Reihen, Reihe für die Startseite, Begründungen.
/// Reine Rechenarbeit auf wenigen tausend Datensätzen, deshalb bei jedem Abruf frisch und ohne eigenen Zwischenspeicher.
/// </summary>
internal static class Pages
{
    private const int RowSize = 18;
    private const int MinRow = 5;

    /// <summary>Mehr Reihen zeigt eine Seite nicht; was in der Rangfolge der Reihen hinten steht, fällt weg.</summary>
    private const int MaxRows = 14;

    /// <summary>Unter so vielen der stärksten Signale wird das für die Reihe „Weil …“ ausgesucht.</summary>
    private const int BecauseSignals = 8;

    /// <summary>In „Weil …“ bleiben nach den ersten acht Titeln nur solche mit mindestens dieser Übereinstimmung.</summary>
    private const int BecauseMatch = 68;

    private const int BecauseKeep = 8;

    /// <summary>Beschreibungen in Reihen sind nur Beiwerk und werden gekürzt; das große Bild oben zeigt mehr.</summary>
    private const int RowOverview = 220;
    private const int HeroOverview = 400;

    // Rangfolge der Reihen: so erscheinen sie, und von hinten wird gestrichen. Vergeben werden die Titel in einer anderen
    // Reihenfolge (siehe Rows): schmale Quellen zuerst.
    private static readonly string[] Order =
    {
        "top10", "requests", "library", "genre-1", "because-1", "person-1", "trending", "genre-2", "collections", "classics",
        "tv", "movies", "gems", "person-2", "genre-3", "because-2", "upcoming", "explore",
    };

    /// <param name="requests">Angefragte Titel des Kontos, die noch unterwegs sind (für die Reihe „Unterwegs zu dir“).</param>
    public static PageDto Page(Snapshot snap, string type, int genre, IReadOnlyList<RequestEntry>? requests = null)
    {
        var profile = snap.Profile;
        var c = new PageContext(snap, type, genre, requests ?? Array.Empty<RequestEntry>());
        var page = new PageDto
        {
            Status = snap.Ranked.Length + snap.Library.Length + snap.Upcoming.Length + c.Requests.Count == 0 ? "empty" : "ready",
            GeneratedAt = snap.BuiltAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            Confidence = Math.Round(profile.Alpha, 2),
            Signals = profile.StrongCount,
            NeedsTaste = NeedsTaste(snap),
            Chips = Chips(snap, type),
        };
        if (page.Status == "empty")
        {
            page.Error = "none";
            return page;
        }

        // Erst sehen, welche Reihen zustande kommen; sind es zu viele, noch einmal ohne die gestrichenen vergeben, damit
        // deren Titel den übrigen Reihen zugutekommen.
        var rows = Rows(c, null, out var top);
        var kept = Kept(rows.Keys);
        if (kept.Count < rows.Count)
        {
            rows = Rows(c, kept, out top);
        }

        foreach (var slot in Order)
        {
            if (rows.TryGetValue(slot, out var row))
            {
                page.Rows.Add(row);
            }
        }

        if (page.Rows.Count == 0)
        {
            // Für diese Auswahl (etwa ein seltenes Genre) kommt keine Reihe zusammen; die Genres zur Auswahl bleiben stehen.
            page.Status = "empty";
            page.Error = "none";
            return page;
        }

        // Großes Bild: die Besten mit Querbild und Text, jede Filmreihe höchstens einmal.
        var heroFranchise = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in top.Concat(c.Ranked.Take(40)))
        {
            if (page.Hero.Count >= 5)
            {
                break;
            }

            if (s.Title.Backdrop is null || string.IsNullOrEmpty(s.Title.Overview) || !heroFranchise.Add(Franchise(s)))
            {
                continue;
            }

            page.Hero.Add(Item(s, snap, "hero", genre, HeroOverview));
        }

        return page;
    }

    /// <summary>
    /// „Nur aus meiner Mediathek“: was schon da ist und noch nicht angefangen wurde, nach Geschmack geordnet. Oben die
    /// besten, darunter je Genre eine Reihe (ohne Genre-Wahl); Filme/Serien und Genre filtern wie sonst.
    /// </summary>
    public static PageDto LibraryPage(Snapshot snap, string type, int genre)
    {
        var de = snap.Lang != "en";
        IEnumerable<Scored> pool = snap.Library;
        if (type == "movie")
        {
            pool = pool.Where(s => !s.Key.Tv);
        }
        else if (type == "tv")
        {
            pool = pool.Where(s => s.Key.Tv);
        }

        if (genre > 0)
        {
            pool = pool.Where(s => s.Genres.Contains(genre));
        }

        var list = pool.ToList();
        var page = new PageDto
        {
            Status = list.Count == 0 ? "empty" : "ready",
            GeneratedAt = snap.BuiltAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            Confidence = Math.Round(snap.Profile.Alpha, 2),
            Signals = snap.Profile.StrongCount,
            NeedsTaste = false,
            Chips = Chips(snap, type),
        };
        if (list.Count == 0)
        {
            page.Error = "none";
            return page;
        }

        var used = new HashSet<MediaKey>();
        var top = list.Take(24).ToList();
        used.UnionWith(top.Select(s => s.Key));
        page.Rows.Add(new RowDto
        {
            Id = "have",
            Kind = "poster",
            Title = de ? "Aus deiner Mediathek – passt zu dir" : "From your library – picked for you",
            Items = top.Select(s => Item(s, snap, "library", genre)).ToList(),
        });

        if (genre == 0)
        {
            var genres = list.SelectMany(s => s.Genres.Take(2)).GroupBy(g => g).OrderByDescending(g => g.Count()).Select(g => g.Key).Take(8).ToList();
            foreach (var g in genres)
            {
                var items = list.Where(s => s.Genres.Contains(g) && !used.Contains(s.Key)).Take(18).ToList();
                if (items.Count < 4)
                {
                    continue;
                }

                used.UnionWith(items.Select(s => s.Key));
                page.Rows.Add(new RowDto { Id = "have-" + g.ToString(CultureInfo.InvariantCulture), Kind = "poster", Title = GenreMap.Nice(g, snap.Lang, type == "tv"), Items = items.Select(s => Item(s, snap, "library", g)).ToList() });
            }
        }

        var rest = list.Where(s => !used.Contains(s.Key)).Take(40).ToList();
        if (rest.Count >= 4)
        {
            page.Rows.Add(new RowDto { Id = "have-more", Kind = "poster", Title = de ? "Außerdem in deiner Mediathek" : "Also in your library", Items = rest.Select(s => Item(s, snap, "library", genre)).ToList() });
        }

        foreach (var s in list)
        {
            if (page.Hero.Count >= 5)
            {
                break;
            }

            if (s.Title.Backdrop is not null && !string.IsNullOrEmpty(s.Title.Overview))
            {
                page.Hero.Add(Item(s, snap, "hero", genre, HeroOverview));
            }
        }

        return page;
    }

    /// <summary>Die Reihe „Für dich“ der Startseite: die Rangfolge mit etwas Abwechslung, weit hinten ein bewusst anderer Titel.</summary>
    public static HomeDto Home(Snapshot snap)
    {
        var ranked = snap.Ranked;
        var items = Pick(ranked, 14, new HashSet<MediaKey>());
        Scored? other = null;
        if (snap.Profile.Alpha >= 0.15f && items.Count >= 12)
        {
            other = ranked.Where(s => s.Explore > 0f && !items.Contains(s)).OrderByDescending(s => s.Explore).FirstOrDefault();
            if (other is not null)
            {
                items.RemoveAt(items.Count - 1);
                items.Insert(11, other);
            }
        }

        var home = new HomeDto { Status = items.Count > 0 ? "ready" : "empty", Title = Texts.Home(snap.Lang) };
        foreach (var s in items)
        {
            home.Items.Add(Item(s, snap, ReferenceEquals(s, other) ? "explore" : "top10", 0, RowOverview));
        }

        return home;
    }

    /// <summary>Ein angefragter Titel mit seinem Stand, wie ihn das Theme unter „Unterwegs zu dir“ zeigt.</summary>
    public static ItemDto RequestItem(RequestEntry e, string lang, DateTime nowLocal)
    {
        var title = e.Title!;
        var item = Base(new Scored { Key = e.Key, Title = title, Detail = e.Detail, Lib = e.Lib }, lang, 0, RowOverview);
        item.Match = 0;
        item.Reason = string.Empty;
        item.ReasonKind = "request";

        // Was in der Bibliothek liegt, entscheidet Jellyfin: ganz da, teilweise da (Serie) oder noch gar nicht.
        if (e.Phase == RequestPhase.Available && e.Lib is not null)
        {
            item.State = "available";
        }
        else if (e.Lib is not null && e.Key.Tv && e.Active)
        {
            item.State = "partial";
        }
        else
        {
            item.State = e.Active ? "requested" : "missing";
            item.ItemId = null;
        }

        var utc = "yyyy-MM-ddTHH:mm:ssZ";
        var inv = CultureInfo.InvariantCulture;
        int? minutes = e.Eta is { } eta ? Math.Max(1, (int)Math.Ceiling((eta - nowLocal.ToUniversalTime()).TotalMinutes)) : null;
        item.Request = new RequestDto
        {
            Phase = e.Phase.ToString().ToLowerInvariant(),
            Percent = e.Percent,
            EtaMinutes = minutes,
            Eta = e.Eta?.ToString(utc, inv),
            Text = Texts.RequestText(e, lang, nowLocal),
            Short = Texts.RequestShort(e, lang, nowLocal),
            RequestedAt = e.RequestedAt > DateTime.MinValue ? e.RequestedAt.ToString(utc, inv) : null,
            AvailableAt = e.AvailableAt?.ToString(utc, inv),
            AvailableFrom = e.AvailableFrom > 0
                ? (e.AvailableFrom / 10000).ToString("0000", inv) + "-" + (e.AvailableFrom / 100 % 100).ToString("00", inv) + "-" + (e.AvailableFrom % 100).ToString("00", inv)
                : null,
            IsNew = e.IsNew,
            Seasons = e.Key.Tv ? Texts.RequestSeasons(e.Seasons, e.Detail?.Seasons ?? 0, lang) : null,
        };
        return item;
    }

    /// <summary>Welche der zustande gekommenen Reihen bleiben: höchstens 14, gestrichen wird von hinten, „Demnächst“ und „Mal was anderes“ zuletzt.</summary>
    private static HashSet<string> Kept(IEnumerable<string> present)
    {
        var have = new HashSet<string>(present, StringComparer.Ordinal);
        var kept = Order.Where(have.Contains).ToList();
        for (var i = kept.Count - 1; i >= 0 && kept.Count > MaxRows; i--)
        {
            // Die beiden letzten Reihen sind die einzigen, die über den Geschmack hinausführen bzw. nach vorn schauen; sie
            // bleiben, solange noch etwas anderes weichen kann.
            if (kept[i] is not ("upcoming" or "explore"))
            {
                kept.RemoveAt(i);
            }
        }

        while (kept.Count > MaxRows)
        {
            kept.RemoveAt(kept.Count - 1);
        }

        return new HashSet<string>(kept, StringComparer.Ordinal);
    }

    /// <summary>
    /// Verteilt die Titel auf die Reihen; kein Titel steht zweimal. Schmale Quellen (Filmreihen, „Weil …“, Personen)
    /// bedienen sich vor den breiten, sonst blieben für sie keine Titel übrig.
    /// </summary>
    /// <param name="only">Nur diese Reihen bilden (zweiter Durchgang nach dem Streichen); <c>null</c> = alle.</param>
    private static Dictionary<string, RowDto> Rows(PageContext c, HashSet<string>? only, out List<Scored> top)
    {
        var snap = c.Snap;
        var lang = snap.Lang;
        var profile = snap.Profile;
        var ranked = c.Ranked;
        var used = new HashSet<MediaKey>();
        var rows = new Dictionary<string, RowDto>(StringComparer.Ordinal);
        bool Wanted(string slot) => only is null || only.Contains(slot);

        void Add(string slot, string id, string kind, string title, List<Scored> items, string context, int min = MinRow)
        {
            if (items.Count < min)
            {
                return;
            }

            var row = new RowDto { Id = id, Kind = kind, Title = title };
            foreach (var s in items)
            {
                used.Add(s.Key);
                row.Items.Add(Item(s, snap, context, c.Genre, RowOverview));
            }

            rows[slot] = row;
        }

        // Die zehn Besten in reiner Rangfolge: nur ein dritter Titel derselben Filmreihe oder Regie wird übergangen.
        top = Pick(ranked, 10, used, null, true);
        top.Sort((a, b) => b.Score.CompareTo(a.Score));
        Add("top10", "top10", "top10", Texts.Row("top10", lang, c.GenreName), top, "top10");

        if (Wanted("collections"))
        {
            var parts = ranked.Where(s => s.Candidate is { Collection: > 0 } && !used.Contains(s.Key))
                .OrderByDescending(s => BestOfCollection(ranked, s.Candidate!.Collection)).ThenBy(s => s.Candidate!.Collection).ThenBy(s => s.Title.Date)
                .Take(RowSize).ToList();
            Add("collections", "collections", "poster", Texts.Row("collections", lang, c.GenreName), parts, "collections");
        }

        // „Weil …“: Was Seerr zu einem Signal empfiehlt, ist nach den zehn Besten oft nur noch Mittelmaß. Unter den stärksten
        // Signalen kommen deshalb die zuerst, zu denen noch am meisten Gutes übrig ist; schwache Reste bleiben weg.
        var because = 0;
        var seenFranchise = new HashSet<string>(StringComparer.Ordinal);
        var options = new List<(Signal Signal, List<Scored> Items, float Value)>();
        var topWeight = 0f;
        foreach (var signal in profile.Signals)
        {
            if (options.Count >= BecauseSignals || !Wanted("because-1"))
            {
                break;
            }

            if (!signal.Strong || signal.Weight <= 0f || signal.Name.Length == 0 || !snap.Pool.Recs.TryGetValue(signal.Key, out var recs))
            {
                continue;
            }

            var items = new List<Scored>();
            foreach (var key in recs)
            {
                if (snap.ByKey.TryGetValue(key, out var s) && s.Missing && c.Fits(s) && !used.Contains(key))
                {
                    items.Add(s);
                }
            }

            items.Sort((a, b) => b.Score.CompareTo(a.Score));
            var cut = items.Count > BecauseKeep ? items.FindIndex(BecauseKeep, s => s.Match < BecauseMatch) : -1;
            if (cut >= 0)
            {
                items.RemoveRange(cut, items.Count - cut);
            }

            if (items.Count < MinRow)
            {
                continue;
            }

            // Wert einer Reihe: wie gut ihre ersten sechs Titel im Schnitt sind; das Gewicht des Signals zählt leicht mit.
            topWeight = Math.Max(topWeight, signal.Weight);
            options.Add((signal, items, items.Take(6).Average(s => s.Score) * (0.85f + (0.15f * signal.Weight / topWeight))));
        }

        foreach (var (signal, items, _) in options.OrderByDescending(o => o.Value))
        {
            var slot = "because-" + (because + 1).ToString(CultureInfo.InvariantCulture);
            if (because >= 2 || !Wanted(slot))
            {
                break;
            }

            // Zwei Teile derselben Reihe ergäben fast dieselbe Liste.
            var franchise = FranchiseOf(signal.Name, snap.Lib.Items.TryGetValue(signal.Key, out var entry) ? entry.Collection : 0);
            if (seenFranchise.Contains(franchise))
            {
                continue;
            }

            var free = items.Where(s => !used.Contains(s.Key)).ToList();
            if (free.Count < MinRow)
            {
                continue;
            }

            seenFranchise.Add(franchise);
            because++;
            Add(slot, "because-" + signal.Key, "poster", Texts.BecauseRow(signal.Kind, signal.Name, lang), Pick(free, RowSize, used), "because");
        }

        var people = 0;
        foreach (var list in snap.Pool.People.Values.OrderByDescending(l => profile.People.TryGetValue(l.Feature, out var pref) ? pref.Weight : 0f))
        {
            var slot = "person-" + (people + 1).ToString(CultureInfo.InvariantCulture);
            if (people >= 2 || !Wanted(slot) || !profile.People.ContainsKey(list.Feature))
            {
                break;
            }

            var items = new List<Scored>();
            foreach (var key in list.Keys)
            {
                if (snap.ByKey.TryGetValue(key, out var s) && s.Missing && c.Fits(s) && !used.Contains(key))
                {
                    items.Add(s);
                }
            }

            if (items.Count < MinRow)
            {
                continue;
            }

            items.Sort((a, b) => b.Score.CompareTo(a.Score));
            people++;
            Add(slot, "person-" + list.Feature.Replace(' ', '-'), "poster", Texts.PersonRow(list.Name, lang), Pick(items, RowSize, used), "person");
        }

        if (Wanted("trending"))
        {
            // Angesagt und passend: Was weit hinten liegt, bleibt weg, solange die Reihe auch so voll genug wird.
            var trending = Pick(ranked.Where(s => s.Candidate is { TrendRank: > 0 }), RowSize, used);
            while (trending.Count > 8 && trending[^1].Match <= 64)
            {
                trending.RemoveAt(trending.Count - 1);
            }

            Add("trending", "trending", "poster", Texts.Row("trending", lang, c.GenreName), trending, "trending");
        }

        if (Wanted("classics"))
        {
            Add("classics", "classics", "poster", Texts.Row("classics", lang, c.GenreName), Pick(ranked.Where(s => IsClassic(s, c.Year)), RowSize, used), "classics");
        }

        if (Wanted("gems"))
        {
            Add("gems", "gems", "poster", Texts.Row("gems", lang, c.GenreName), Pick(ranked.Where(s => IsGem(s, c.Year)), RowSize, used), "gems");
        }

        if (Wanted("explore"))
        {
            Add("explore", "explore", "poster", Texts.Row("explore", lang, c.GenreName), c.ExplorePool.Where(s => !used.Contains(s.Key)).Take(RowSize).ToList(), "explore");
        }

        if (c.Type == "all" && Wanted("tv"))
        {
            // Serien sind im Bestand knapper als Filme; ihre Reihe bedient sich deshalb vor den Genre-Reihen.
            Add("tv", "tv", "poster", Texts.Row("tv", lang, c.GenreName), Pick(ranked.Where(s => s.Key.Tv), RowSize, used), "tv");
        }

        if (c.Genre == 0)
        {
            var count = 0;
            foreach (var g in GenreOrder(profile))
            {
                var slot = "genre-" + (count + 1).ToString(CultureInfo.InvariantCulture);
                if (count >= 3 || !Wanted(slot))
                {
                    break;
                }

                // Innerhalb eines Genres zählt auch, wie sehr der Titel dazugehört: vorn genannte Genres prägen ihn stärker.
                var items = Pick(ranked.Where(s => GenreMap.Matches(s.Genres, g)), RowSize, used, s => s.Score * GenreFit(s, g));
                if (items.Count < MinRow)
                {
                    continue;
                }

                count++;
                Add(slot, "genre-" + g.ToString(CultureInfo.InvariantCulture), "poster", Texts.GenreRow(GenreMap.Nice(g, lang), lang), items, "genre:" + g.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (c.Type == "all" && Wanted("movies"))
        {
            Add("movies", "movies", "poster", Texts.Row("movies", lang, c.GenreName), Pick(ranked.Where(s => !s.Key.Tv), RowSize, used), "movies");
        }

        // Diese drei schöpfen aus eigenen Beständen und nehmen niemandem etwas weg.
        if (Wanted("library"))
        {
            Add("library", "library", "wide", Texts.Row("library", lang, c.GenreName), snap.Library.Where(s => s.Title.Backdrop is not null && c.Fits(s)).Take(16).ToList(), "library");
        }

        if (Wanted("upcoming"))
        {
            // Die 14 passendsten, dann der Reihe nach, wie sie erscheinen.
            var soon = snap.Upcoming.Where(c.Fits).Take(14).OrderBy(s => s.Title.Date == 0 ? int.MaxValue : s.Title.Date).ToList();
            Add("upcoming", "upcoming", "poster", Texts.Row("upcoming", lang, c.GenreName), soon, "upcoming");
        }

        if (Wanted("requests") && c.Requests.Count > 0)
        {
            // Der Stand der Anfragen: dieselben Einträge wie unter „Unterwegs zu dir“, schon ab einem Titel.
            var row = new RowDto { Id = "requests", Kind = "poster", Title = Texts.Row("requests", lang, c.GenreName) };
            foreach (var e in c.Requests)
            {
                if (e.Active && e.Title is not null && c.Fits(e.Key, e.Detail?.Genres is { Length: > 0 } g ? g : e.Title.Genres) && row.Items.Count < RowSize)
                {
                    row.Items.Add(RequestItem(e, lang, c.NowLocal));
                }
            }

            if (row.Items.Count > 0)
            {
                rows["requests"] = row;
            }
        }

        return rows;
    }

    /// <summary>Was beim Bauen einer Seite feststeht: Stand, Auswahl und die dazu passenden Kandidaten.</summary>
    private sealed class PageContext
    {
        public PageContext(Snapshot snap, string type, int genre, IReadOnlyList<RequestEntry> requests)
        {
            Snap = snap;
            Type = type;
            Genre = genre;
            Requests = requests;
            Underway = new HashSet<MediaKey>(requests.Where(e => e.Active).Select(e => e.Key));
            NowLocal = DateTime.Now;
            Year = NowLocal.Year;
            GenreName = genre > 0 ? GenreMap.Nice(genre, snap.Lang, type == "tv" || !GenreMap.IsMovieGenre(genre)) : null;
            Ranked = new List<Scored>(snap.Ranked.Length);
            foreach (var s in snap.Ranked)
            {
                if (Fits(s))
                {
                    Ranked.Add(s);
                }
            }

            ExplorePool = Ranked.Where(s => s.Explore > 0f).OrderByDescending(s => s.Explore).ToList();
        }

        public Snapshot Snap { get; }

        public string Type { get; }

        public int Genre { get; }

        public IReadOnlyList<RequestEntry> Requests { get; }

        /// <summary>Was gerade unterwegs ist, steht nur in der Reihe der Anfragen, auch wenn die Rangfolge älter ist als die Anfrage.</summary>
        public HashSet<MediaKey> Underway { get; }

        public DateTime NowLocal { get; }

        public int Year { get; }

        public string? GenreName { get; }

        public List<Scored> Ranked { get; }

        public List<Scored> ExplorePool { get; }

        public bool Fits(Scored s) => !Underway.Contains(s.Key) && Fits(s.Key, s.Genres);

        public bool Fits(MediaKey key, int[] genres) =>
            (Type == "all" || (Type == "tv") == key.Tv) && (Genre == 0 || GenreMap.Matches(genres, Genre));
    }

    public static bool NeedsTaste(Snapshot snap) => !snap.State.TasteDone && snap.Profile.StrongCount < 8;

    /// <summary>Wie viele empfehlbare Titel es zu einem Genre gibt (entscheidet, ob eigene Listen nachgeholt werden).</summary>
    public static int Depth(Snapshot snap, string type, int genre)
    {
        var n = 0;
        foreach (var s in snap.Ranked)
        {
            if ((type == "all" || (type == "tv") == s.Key.Tv) && GenreMap.Matches(s.Genres, genre))
            {
                n++;
            }
        }

        return n;
    }

    public static ProfileDto Profile(Snapshot? snap, StateView state, Catalog catalog, string lang)
    {
        var dto = new ProfileDto();
        if (snap is not null)
        {
            var p = snap.Profile;
            dto.Confidence = Math.Round(p.Alpha, 2);
            dto.Signals = p.StrongCount;
            dto.Genres.AddRange(p.ViewGenres.Select(f => Feature(f, null)));
            dto.People.AddRange(p.ViewPeople.Select(f => Feature(f, Texts.Role(f.Directs, f.Tv, lang))));
            dto.Decades.AddRange(p.ViewDecades.Select(f => Feature(f, null)));
            dto.Keywords.AddRange(p.ViewKeywords.Select(f => Feature(f, null)));
        }

        foreach (var f in state.Feedback.OrderByDescending(f => f.Value.At))
        {
            var list = f.Value.Action switch
            {
                "like" => dto.Feedback.Liked,
                "dislike" => dto.Feedback.Disliked,
                "hide" => dto.Feedback.Hidden,
                _ => null,
            };
            if (list is null || list.Count >= 200)
            {
                continue;
            }

            LibEntry? entry = null;
            snap?.Lib.Items.TryGetValue(f.Key, out entry);
            var title = catalog.Get(lang, f.Key) ?? catalog.FromDetail(lang, f.Key);
            if (title is null && entry is null)
            {
                // Noch nichts über den Titel bekannt (Rückmeldung vor der ersten Berechnung): wenigstens die Nummer liefern.
                list.Add(new ItemDto { Key = f.Key.ToString(), MediaType = f.Key.Type, TmdbId = f.Key.Id });
                continue;
            }

            title ??= new Title { Key = f.Key, Name = entry!.Name, Date = entry.Year * 10000 };
            var item = Plain(title, catalog.Detail(f.Key), entry is { Visible: true } ? entry : null, lang);
            list.Add(item);
        }

        return dto;
    }

    /// <summary>Ein Titel ohne Bewertung durch das Profil (Geschmacks-Check, Rückmeldungen).</summary>
    public static ItemDto Plain(Title title, Detail? detail, LibEntry? lib, string lang)
    {
        var s = new Scored { Key = title.Key, Title = title, Detail = detail, Lib = lib };
        var item = Base(s, lang);
        item.Match = 0;
        item.Reason = string.Empty;
        item.ReasonKind = lib is not null ? "library" : "genre";
        return item;
    }

    public static ItemDto Item(Scored s, Snapshot snap, string context, int focusGenre = 0, int overview = RowOverview)
    {
        var item = Base(s, snap.Lang, focusGenre, overview);
        item.Match = s.Match;
        if (context == "requests")
        {
            item.State = "requested";
        }

        item.Reason = Reason(s, snap, context, out var kind);
        item.ReasonKind = kind;
        return item;
    }

    // ---- Auswahl mit Abwechslung -------------------------------------------------------------------------------

    /// <summary>
    /// Nimmt die Besten, aber nicht stur nach Punktzahl: höchstens zwei Titel derselben Filmreihe oder Regie dicht
    /// hintereinander, Genres und Filme/Serien gemischt.
    /// </summary>
    /// <param name="value">Eigene Wertung statt der Punktzahl (Genre-Reihen).</param>
    /// <param name="strict">Reine Rangfolge: nur ein dritter Titel derselben Filmreihe oder Regie wird übergangen, gemischt wird nicht.</param>
    private static List<Scored> Pick(IEnumerable<Scored> source, int count, HashSet<MediaKey> used, Func<Scored, float>? value = null, bool strict = false)
    {
        var pool = source as List<Scored> ?? source.ToList();
        if (value is null)
        {
            value = s => s.Score;
        }
        else
        {
            // Die Quelle kommt nach Punktzahl sortiert; mit eigener Wertung neu ordnen (stabil, damit Gleichstände fest bleiben).
            var weigh = value;
            pool = pool.Select((s, i) => (Item: s, Value: weigh(s), Index: i)).OrderByDescending(x => x.Value).ThenBy(x => x.Index).Select(x => x.Item).ToList();
        }

        var result = new List<Scored>(count);
        var taken = new HashSet<MediaKey>();
        var start = 0;
        while (result.Count < count)
        {
            while (start < pool.Count && (used.Contains(pool[start].Key) || taken.Contains(pool[start].Key)))
            {
                start++;
            }

            Scored? best = null;
            var bestValue = float.MinValue;
            var scanned = 0;
            for (var i = start; i < pool.Count && scanned < 30; i++)
            {
                var s = pool[i];
                if (used.Contains(s.Key) || taken.Contains(s.Key))
                {
                    continue;
                }

                // Die Liste ist absteigend sortiert und der Abzug höchstens 1: Was jetzt schon darunter liegt, holt nicht mehr auf.
                var own = value(s);
                if (best is not null && own <= bestValue)
                {
                    break;
                }

                scanned++;
                var adjusted = own * Variety(s, result, strict);
                if (adjusted > bestValue)
                {
                    bestValue = adjusted;
                    best = s;
                }
            }

            if (best is null)
            {
                break;
            }

            taken.Add(best.Key);
            result.Add(best);
        }

        return result;
    }

    private static float Variety(Scored s, List<Scored> picked, bool strict)
    {
        if (picked.Count == 0)
        {
            return 1f;
        }

        var franchise = Franchise(s);
        var director = Director(s);
        int sameFranchise = 0, sameDirector = 0, sameType = 0, sameGenre = 0;
        // Streng: auf die ganze Auswahl gesehen. Sonst zählen nur die letzten fünf Plätze („dicht hintereinander“).
        for (var i = picked.Count - 1; i >= 0 && (strict || i >= picked.Count - 5); i--)
        {
            var p = picked[i];
            if (Franchise(p) == franchise)
            {
                sameFranchise++;
            }

            if (director.Length > 0 && Director(p) == director)
            {
                sameDirector++;
            }

            if (i >= picked.Count - 3 && p.Key.Tv == s.Key.Tv)
            {
                sameType++;
            }

            if (i >= picked.Count - 2 && MainGenre(p) == MainGenre(s))
            {
                sameGenre++;
            }
        }

        if (strict)
        {
            return sameFranchise >= 2 || sameDirector >= 2 ? 0.3f : 1f;
        }

        // Reihe und Regie sind harte Grenzen (ein dritter Titel kommt praktisch nicht durch); Genre und Art mischen nur
        // unter fast gleich guten Titeln, damit die Übereinstimmung in einer Reihe weiter erkennbar abfällt.
        var factor = 1f;
        factor *= sameFranchise >= 2 ? 0.3f : sameFranchise == 1 ? 0.92f : 1f;
        factor *= sameDirector >= 2 ? 0.5f : sameDirector == 1 ? 0.97f : 1f;
        factor *= sameGenre >= 2 ? 0.97f : 1f;
        factor *= sameType >= 3 ? 0.985f : 1f;
        return factor;
    }

    /// <summary>Wie sehr ein Titel zu einem Genre gehört: an erster Stelle genannt voll, weiter hinten und über Sammel-Genres weniger.</summary>
    private static float GenreFit(Scored s, int genre)
    {
        var genres = s.Genres;
        for (var i = 0; i < genres.Length; i++)
        {
            if (genres[i] == genre)
            {
                return i switch { 0 => 1f, 1 => 0.96f, 2 => 0.9f, _ => 0.84f };
            }
        }

        // Nur über ein Sammel-Genre verwandt („Sci-Fi & Fantasy“ bei Serien): unklar, welcher Teil gemeint ist.
        for (var i = 0; i < genres.Length; i++)
        {
            if (GenreMap.Matches(new[] { genres[i] }, genre))
            {
                return (i switch { 0 => 1f, 1 => 0.96f, 2 => 0.9f, _ => 0.84f }) * 0.93f;
            }
        }

        return 0.8f;
    }

    private static int MainGenre(Scored s) => s.GenreHit > 0 ? s.GenreHit : s.Genres.Length > 0 ? s.Genres[0] : 0;

    private static string Franchise(Scored s)
    {
        if (s.FranchiseKey is null)
        {
            var collection = s.Detail?.Collection > 0 ? s.Detail.Collection : s.Candidate?.Collection ?? s.Lib?.Collection ?? 0;
            s.FranchiseKey = FranchiseOf(s.Title.Name, collection);
        }

        return s.FranchiseKey;
    }

    /// <summary>Filmreihe über die Nummer der Reihe; wo sie fehlt, über den Titel bis zum Doppelpunkt bzw. ohne Zahl am Ende.</summary>
    private static string FranchiseOf(string name, int collection)
    {
        if (collection > 0)
        {
            return "c" + collection.ToString(CultureInfo.InvariantCulture);
        }

        var text = name;
        foreach (var mark in new[] { ":", " – ", " - " })
        {
            var cut = text.IndexOf(mark, StringComparison.Ordinal);
            if (cut >= 3)
            {
                text = text[..cut];
            }
        }

        text = text.TrimEnd(' ', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'I', 'V', 'X').Trim();
        return "t" + (text.Length >= 3 ? text : name).ToLowerInvariant();
    }

    private static string Director(Scored s)
    {
        if (s.DirectorKey is null)
        {
            s.DirectorKey = s.Detail is { Directors.Length: > 0 }
                ? "d" + s.Detail.Directors[0].ToString(CultureInfo.InvariantCulture)
                : s.Candidate is { PersonDirects: true, Person: not null } ? "p" + s.Candidate.Person : string.Empty;
        }

        return s.DirectorKey;
    }

    private static float BestOfCollection(List<Scored> ranked, int collection)
    {
        foreach (var s in ranked)
        {
            if (s.Candidate?.Collection == collection)
            {
                return s.Score;
            }
        }

        return 0f;
    }

    private static bool IsClassic(Scored s, int year) =>
        s.Candidate is not null && (s.Candidate.Sources & Src.Classic) != 0 && s.Title.Year > 0 && s.Title.Year <= year - 8;

    /// <summary>Geheimtipp: sehr gut bewertet, aber wenig bekannt und nicht mehr neu (frische Titel sind oft nur anfangs hoch bewertet).</summary>
    private static bool IsGem(Scored s, int year) =>
        s.Candidate is not null && s.Candidate.TrendRank == 0 && s.Title.Year > 0 && s.Title.Year <= year - 2
        && s.Title.Rating >= 7.4f && s.Title.Votes >= (s.Key.Tv ? 200 : 400) && s.Title.Votes <= (s.Key.Tv ? 1500 : 3000);

    /// <summary>Genres in der Reihenfolge der Vorlieben; ohne Profil die breit gestreute Grundauswahl.</summary>
    private static IEnumerable<int> GenreOrder(Profile profile)
    {
        var seen = new HashSet<int>();
        foreach (var g in profile.TopGenres)
        {
            if (seen.Add(g))
            {
                yield return g;
            }
        }

        foreach (var g in GenreMap.Defaults)
        {
            if (profile.Genres.GetValueOrDefault(g) >= 0f && seen.Add(g))
            {
                yield return g;
            }
        }
    }

    private static ChipsDto Chips(Snapshot snap, string type)
    {
        var profile = snap.Profile;
        var chips = new ChipsDto();
        var candidates = new List<(int Id, float Weight)>();
        if (type == "tv")
        {
            // Serien haben eigene Genres; ihr Gewicht ergibt sich aus den Film-Genres, auf die sie abgebildet werden.
            var parts = new List<(int Id, float Part)>(4);
            var one = new int[1];
            foreach (var g in GenreMap.TvIds)
            {
                if (g == 10766)
                {
                    // „Soap“ erbt sein Gewicht nur von Drama und Liebesfilm; als eigene Auswahl führte das in die Irre.
                    continue;
                }

                one[0] = g;
                GenreMap.Canonical(one, parts);
                var weight = 0f;
                foreach (var p in parts)
                {
                    weight = Math.Max(weight, profile.Genres.GetValueOrDefault(p.Id));
                }

                candidates.Add((g, weight));
            }

            candidates.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        }
        else
        {
            var filler = 0.05f;
            foreach (var g in GenreOrder(profile))
            {
                var weight = profile.Genres.GetValueOrDefault(g);
                if (weight <= 0.05f)
                {
                    // Ohne Vorliebe: hinten anstellen, in fester Reihenfolge und mit kleinem Gewicht.
                    filler = Math.Max(0.01f, filler - 0.003f);
                    weight = filler;
                }

                candidates.Add((g, weight));
            }
        }

        foreach (var (id, weight) in candidates)
        {
            if (chips.Genres.Count >= 12)
            {
                break;
            }

            if (weight < 0f || Depth(snap, type, id) < MinRow)
            {
                continue;
            }

            var movie = type != "tv" && GenreMap.IsMovieGenre(id);
            chips.Genres.Add(new ChipDto
            {
                Id = id,
                Name = GenreMap.Name(id, snap.Lang, !movie),
                MediaType = movie ? "movie" : "tv",
                Weight = Math.Round(Math.Clamp(weight, 0f, 1f), 2),
            });
        }

        return chips;
    }

    // ---- Titel und Begründung ----------------------------------------------------------------------------------

    private static FeatureDto Feature(FeatureView f, string? role) =>
        new() { Feature = f.Feature, Name = f.Name, Role = role, Weight = Math.Round(Math.Clamp(f.Weight, 0f, 1f), 2), Muted = f.Muted };

    /// <param name="focusGenre">Auf einer Genre-Seite: dieses Genre soll unter den drei genannten sein.</param>
    /// <param name="overview">Höchstlänge der Beschreibung; gekürzt wird am Wortende.</param>
    private static ItemDto Base(Scored s, string lang, int focusGenre = 0, int overview = RowOverview)
    {
        var t = s.Title;
        var d = s.Detail;
        var date = t.Date > 0 ? t.Date : d?.Date ?? 0;
        var item = new ItemDto
        {
            Key = s.Key.ToString(),
            MediaType = s.Key.Type,
            TmdbId = s.Key.Id,
            Title = t.Name,
            Year = date > 0 ? date / 10000 : null,
            Overview = t.Overview is { Length: > 0 } text ? (text.Length > overview ? Texts.Shorten(text, overview) : text) : null,
            Poster = t.Poster ?? d?.Poster,
            Backdrop = t.Backdrop ?? d?.Backdrop,
            // Eine Handvoll Stimmen vor dem Start ist noch keine Bewertung.
            Rating = t.Votes >= 10 && t.Rating > 0f ? Math.Round(t.Rating, 1) : null,
            ReleaseDate = date % 100 > 0
                ? (date / 10000).ToString("0000", CultureInfo.InvariantCulture) + "-" + (date / 100 % 100).ToString("00", CultureInfo.InvariantCulture) + "-" + (date % 100).ToString("00", CultureInfo.InvariantCulture)
                : null,
        };

        var genres = s.Genres;
        var names = new List<string>(3);
        if (focusGenre > 0)
        {
            for (var i = 3; i < genres.Length; i++)
            {
                if (genres[i] == focusGenre || GenreMap.Matches(new[] { genres[i] }, focusGenre))
                {
                    // Steht das gewählte Genre erst an vierter Stelle oder später, rückt es nach vorn.
                    names.Add(GenreMap.Name(genres[i], lang, s.Key.Tv));
                    break;
                }
            }

            for (var i = 0; i < Math.Min(3, genres.Length) && names.Count > 0; i++)
            {
                if (genres[i] == focusGenre || GenreMap.Matches(new[] { genres[i] }, focusGenre))
                {
                    names.Clear();
                }
            }
        }

        foreach (var g in genres)
        {
            var name = GenreMap.Name(g, lang, s.Key.Tv);
            if (name.Length > 0 && !names.Contains(name) && names.Count < 3)
            {
                names.Add(name);
            }
        }

        item.Genres = names.ToArray();

        if (s.Lib is not null)
        {
            item.ItemId = s.Lib.ItemId.ToString("N", CultureInfo.InvariantCulture);
            // Teilweise vorhanden: Es gibt schon mehr Staffeln zu sehen, als in der Bibliothek liegen.
            item.State = s.Key.Tv && d is not null && s.Lib.Seasons > 0 && d.Seasons > s.Lib.Seasons ? "partial" : "available";
            item.Runtime = s.Key.Tv ? null : LibraryRuntime(s) is > 0 and var minutes ? minutes : null;
            item.Seasons = s.Key.Tv ? (s.Lib.Seasons > 0 ? s.Lib.Seasons : d?.Seasons > 0 ? d.Seasons : null) : null;
        }
        else
        {
            item.Runtime = !s.Key.Tv && d?.Runtime > 0 ? d.Runtime : null;
            item.Seasons = s.Key.Tv && d?.Seasons > 0 ? d.Seasons : null;
        }

        return item;
    }

    /// <summary>Laufzeit der Datei in der Bibliothek; kennt Jellyfin keine, die des Films laut Details.</summary>
    private static int LibraryRuntime(Scored s) =>
        s.Lib is { RuntimeMinutes: > 0 } lib ? lib.RuntimeMinutes : s.Detail?.Runtime ?? 0;

    /// <summary>Die Begründung nennt den stärksten Bestandteil der Punktzahl; Reihen mit eigenem Thema sagen ihres.</summary>
    private static string Reason(Scored s, Snapshot snap, string context, out string kind)
    {
        var lang = snap.Lang;
        var profile = snap.Profile;
        var c = s.Candidate;
        var t = s.Title;
        var now = DateTime.Now;

        if (s.Lib is not null)
        {
            kind = "library";
            var extra = s.Key.Tv
                ? (s.Lib.Seasons > 0 ? Texts.Seasons(s.Lib.Seasons, lang) : null)
                : (LibraryRuntime(s) is > 0 and var minutes ? Texts.Duration(minutes, lang) : null);
            return Texts.InLibrary(extra, lang);
        }

        if (context == "requests")
        {
            kind = "request";
            return Texts.Requested(lang);
        }

        if (s.Upcoming)
        {
            kind = "upcoming";
            return Texts.Upcoming(t.Date, lang, now.Year);
        }

        if (context == "explore")
        {
            kind = "explore";
            return Texts.Explore(lang);
        }

        // Der fehlende Teil einer angefangenen Reihe: der genaueste Grund, egal in welcher Reihe der Titel steht.
        if (c is { Collection: > 0 } && snap.Pool.Collections.TryGetValue(c.Collection, out var collection) && collection.Name.Length > 0)
        {
            kind = "collection";
            return Texts.Collection(collection.Name, lang);
        }

        // Persönliche Gründe zählen nur, wenn der Geschmack überhaupt mitredet.
        if (profile.Alpha >= 0.1f)
        {
            var because = context != "because" && c is not null && s.Support >= 0.12f ? profile.SignalFor(c.Because) : null;
            var becauseValue = because is not null && because.Name.Length > 0 ? Scorer.SupportShare * s.Support : 0f;
            var personValue = context != "person" && s.PersonHit is not null && s.People >= 0.2f ? Scorer.PeopleShare * s.People : 0f;
            // Verglichen wird, was jeder Bestandteil über einen beliebigen Titel hinaus beiträgt (Genres: über die neutrale Mitte).
            var genreValue = s.GenreHit > 0 ? Scorer.GenreShare * Math.Max(0f, s.Genre - 0.5f) : 0f;
            if (becauseValue > 0f && becauseValue >= personValue && becauseValue >= genreValue)
            {
                kind = "because";
                return Texts.Because(because!.Kind, because.Name, lang);
            }

            if (personValue > 0f && personValue >= genreValue)
            {
                kind = "person";
                return Texts.Person(s.PersonHit!, s.PersonDirects, s.Key.Tv, lang);
            }
        }

        switch (context)
        {
            case "trending":
                kind = "trending";
                return Texts.Trending(lang);
            case "classics":
                kind = "classic";
                return Texts.Classic(t.Rating, lang);
            case "gems":
                kind = "gem";
                return Texts.Gem(t.Rating, lang);
        }

        if (context.StartsWith("genre:", StringComparison.Ordinal) && int.TryParse(context.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var rowGenre))
        {
            // In einer Genre-Reihe steht das Genre der Reihe, nicht irgendeines des Titels.
            kind = "genre";
            return Texts.GenreRating(GenreName(s, rowGenre, lang), t.Rating, lang);
        }

        if (profile.Alpha >= 0.1f && s.GenreHit > 0 && s.Genre >= 0.6f)
        {
            kind = "genre";
            return Texts.GenreRating(GenreName(s, s.GenreHit, lang), t.Rating, lang);
        }

        if (c is { TrendRank: > 0 and <= 20 })
        {
            kind = "trending";
            return Texts.Trending(lang);
        }

        if (IsClassic(s, now.Year) && t.Rating >= 8f)
        {
            kind = "classic";
            return Texts.Classic(t.Rating, lang);
        }

        if (IsGem(s, now.Year) && t.Rating >= 7.8f)
        {
            kind = "gem";
            return Texts.Gem(t.Rating, lang);
        }

        kind = "genre";
        var first = s.Genres.Length > 0 ? GenreMap.Nice(s.Genres[0], lang, s.Key.Tv) : string.Empty;
        return first.Length > 0 ? Texts.GenreRating(first, t.Rating, lang) : Texts.Rating(t.Rating, lang);
    }

    /// <summary>Name des treffenden Genres, möglichst so, wie es beim Titel selbst heißt („Sci-Fi &amp; Fantasy“ bei Serien).</summary>
    private static string GenreName(Scored s, int canonical, string lang)
    {
        foreach (var g in s.Genres)
        {
            if (g == canonical)
            {
                return GenreMap.Nice(g, lang, s.Key.Tv);
            }
        }

        foreach (var g in s.Genres)
        {
            if (GenreMap.Matches(new[] { g }, canonical))
            {
                return GenreMap.Nice(g, lang, s.Key.Tv);
            }
        }

        return GenreMap.Nice(canonical, lang);
    }
}
