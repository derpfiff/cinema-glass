using System.Diagnostics;
using System.Globalization;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>Ergebnis einer Berechnung: Bestand und Bibliothek, oder der Grund, warum es keinen gibt.</summary>
internal sealed class BuildResult
{
    public string? Error { get; init; }

    /// <summary>Die Bibliothek sieht für das Konto aus wie bei der letzten Berechnung: nichts zu tun.</summary>
    public bool Unchanged { get; init; }

    public Pool Pool { get; init; } = new();

    public LibrarySnapshot Lib { get; init; } = LibrarySnapshot.Empty;

    public RequestsInfo Requests { get; init; } = RequestsInfo.Empty;

    public BuildInfo Info { get; init; } = new();
}

/// <summary>
/// Trägt die Kandidaten eines Kontos zusammen. Alles kommt über Seerr; Listen werden für alle Konten gemeinsam
/// zwischengespeichert, Details dauerhaft. Gerechnet wird in zwei Stufen: grob mit dem, was die Listen liefern, dann
/// werden für die Besten die Details geholt und Stichworte und Personen fließen ein.
/// </summary>
internal sealed class Builder
{
    private const int RecommendationSignals = 15;
    private const int SimilarSignals = 5;
    private const int MovieGenres = 6;
    private const int TvGenres = 4;
    private const int PeopleLists = 6;
    private const int KeywordLists = 6;
    private const int CollectionLists = 10;
    private const int DetailCandidates = 60;
    private const int DetailLibrary = 24;
    private const int SignalDetails = 150;

    private static readonly TimeSpan Days3 = TimeSpan.FromDays(3);
    private static readonly TimeSpan Days2 = TimeSpan.FromDays(2);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);
    private static readonly TimeSpan HalfDay = TimeSpan.FromHours(12);

    private readonly SeerrClient _seerr;
    private readonly Catalog _catalog;
    private readonly LibraryReader _library;
    private readonly StateStore _store;
    private readonly RequestSource _requests;
    private long _cacheHits;

    public Builder(SeerrClient seerr, Catalog catalog, LibraryReader library, StateStore store, RequestSource requests)
    {
        _seerr = seerr;
        _catalog = catalog;
        _library = library;
        _store = store;
        _requests = requests;
    }

    private enum Shape
    {
        Results,
        Parts,
        Credits,
    }

    /// <param name="unchangedIf">Prüfsumme der Bibliothek vom letzten Mal; ist sie gleich geblieben, endet die Berechnung sofort.</param>
    /// <param name="replay">Für einen Messlauf: Bibliothek, Zustand und Gewichte vorgegeben, nichts wird gespeichert.</param>
    public async Task<BuildResult> RunAsync(User user, string lang, Pool? previous, string reason, ulong? unchangedIf, Action<float> progress, CancellationToken ct, Replay? replay = null)
    {
        var info = new BuildInfo { Started = DateTime.UtcNow, Reason = reason };
        var watch = Stopwatch.StartNew();
        long lap = 0;
        void Lap(string name)
        {
            info.Timings[name] = watch.ElapsedMilliseconds - lap;
            lap = watch.ElapsedMilliseconds;
        }

        var requests0 = _seerr.Requests;
        var failed0 = _seerr.Failed;
        var hits0 = Interlocked.Read(ref _cacheHits);
        void Count()
        {
            info.SeerrRequests = _seerr.Requests - requests0;
            info.SeerrFailed = _seerr.Failed - failed0;
            info.CacheHits = Interlocked.Read(ref _cacheHits) - hits0;
            info.Timings["total"] = watch.ElapsedMilliseconds;
        }

        var lib = replay?.Lib ?? await Task.Run(() => _library.Read(user), ct).ConfigureAwait(false);
        Lap("library");
        if (unchangedIf.HasValue && lib.Fingerprint == unchangedIf.Value)
        {
            return new BuildResult { Unchanged = true, Lib = lib, Info = info };
        }

        progress(0.05f);

        // Ein kleiner Abruf vorweg zeigt, ob Seerr erreichbar ist und den Schlüssel annimmt; sonst liefe jede Liste ins Zeitlimit.
        using (var me = await _seerr.GetAsync("auth/me", 6000, ct, true).ConfigureAwait(false))
        {
            if (me is null)
            {
                Count();
                return new BuildResult { Error = "seerr", Lib = lib, Info = info };
            }
        }

        await LearnGenresAsync(lang, ct).ConfigureAwait(false);
        // Für die Rangfolge genügt ein Stand der Anfragen, der bis zu zwei Minuten alt ist.
        var requests = await _requests.GetAsync(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(8), ct).ConfigureAwait(false);
        if (replay is not null)
        {
            requests = replay.Requests(requests);
        }

        Lap("requests");
        progress(0.08f);

        var now = DateTime.UtcNow;
        var local = DateTime.Now;
        var state = replay?.State ?? _store.View(user.Id);
        var theme = replay?.Theme ?? _store.ThemeRequests();
        var tuning = replay?.Tuning;
        Scorer.Requests(user.Id, requests, state, theme, lib, now, out var own, out _);
        var signals = Profile.BuildSignals(lib, state, own, now, tuning);

        // Merkmale der starken Signale: aus den Details. Titel außerhalb der Bibliothek brauchen dabei auch ihren Namen.
        var strong = signals.Where(s => s.Kind != SignalKind.Unseen).Take(SignalDetails)
            .Select(s => (s.Key, Text: s.Lib is null)).ToList();
        info.DetailsFetched += await DetailsAsync(strong, lang, f => progress(0.08f + (0.14f * f)), ct).ConfigureAwait(false);
        Lap("signalDetails");

        var profile = Profile.Build(signals, _catalog, lang, state.Muted, state.Worlds, tuning);
        var pool = new Pool();
        var jobs = Plan(pool, profile, lib, lang, local, ct);
        if (previous is not null)
        {
            // Genres, die das Konto eigens aufgerufen hat, bleiben vertieft.
            foreach (var genre in previous.Deep.Take(8))
            {
                jobs.AddRange(GenreJobs(pool, genre, lang, local, Src.Deep, true, ct));
                pool.Deep.Add(genre);
                pool.Genres.Add(genre);
            }
        }

        var done = 0;
        await Task.WhenAll(jobs.Select(async job =>
        {
            await job().ConfigureAwait(false);
            progress(0.22f + (0.5f * Interlocked.Increment(ref done) / jobs.Count));
        })).ConfigureAwait(false);
        Lap("lists");

        // Stufe 1: grob bewerten, den Bestand begrenzen, für die Besten die Details holen.
        var first = Scorer.Rank(new RankInput { User = user.Id, Lang = lang, Pool = pool, Lib = lib, Requests = requests, Theme = theme, State = state, Catalog = _catalog, BuiltAt = now, Info = info, Tuning = tuning });
        Limit(pool, first, state);
        Lap("stage1");
        progress(0.75f);

        var fix = _catalog.LanguageDiffers(lang);
        var wanted = new List<(MediaKey Key, bool Text)>();
        // Je Berechnung die besten sechzig, die noch keine (frischen) Details haben: So reicht die Verfeinerung mit jeder
        // Berechnung weiter nach hinten, ohne dass die erste lange dauert.
        var today = Scorer.Today(local);
        wanted.AddRange(first.Ranked.Take(DetailCandidates * 4).Where(s => _catalog.NeedsDetail(s.Key, fix && !s.Title.Sure ? lang : null, today))
            .Take(DetailCandidates).Select(s => (s.Key, fix && !s.Title.Sure)));
        wanted.AddRange(first.Upcoming.Take(12).Select(s => (s.Key, fix && !s.Title.Sure)));
        wanted.AddRange(first.Library.Take(DetailLibrary).Select(s => (s.Key, s.Title.Backdrop is null)));
        wanted.AddRange(first.Library.Where(s => s.Key.Tv).Take(40).Select(s => (s.Key, false)));
        info.DetailsFetched += await DetailsAsync(wanted, lang, f => progress(0.75f + (0.22f * f)), ct).ConfigureAwait(false);
        Lap("details");

        if (replay is null)
        {
            _catalog.Save();
        }

        info.PoolSize = pool.Items.Count;
        Count();
        return new BuildResult { Pool = pool, Lib = lib, Requests = requests, Info = info };
    }

    /// <summary>Holt für ein gewähltes Genre eigene Listen dazu, damit dessen Seite genug Tiefe hat.</summary>
    public async Task<Pool> DeepenAsync(Pool current, int genre, string lang, CancellationToken ct)
    {
        var pool = current.Copy();
        var jobs = GenreJobs(pool, genre, lang, DateTime.Now, Src.Deep, true, ct);
        await Task.WhenAll(jobs.Select(j => j())).ConfigureAwait(false);
        pool.Genres.Add(genre);
        pool.Deep.Add(genre);
        return pool;
    }

    /// <summary>Namen und Texte in der gewünschten Sprache nachholen (siehe <see cref="Catalog.LanguageDiffers"/>).</summary>
    public Task<int> FixLanguageAsync(IEnumerable<MediaKey> keys, string lang, CancellationToken ct) =>
        DetailsAsync(keys.Take(160).Select(k => (k, true)).ToList(), lang, null, ct);

    /// <summary>Die Empfehlungen von Seerr zu genau einem Titel.</summary>
    public async Task<List<Title>> RelatedAsync(MediaKey key, string lang, CancellationToken ct)
    {
        var result = new List<Title>();
        var seen = new HashSet<MediaKey> { key };
        foreach (var kind in new[] { "recommendations", "similar" })
        {
            var list = await ListAsync(lang, key.Type + "/" + Num(key.Id) + "/" + kind + "?page=1", Days3, Shape.Results, true, key.Tv, ct, SeerrClient.QuickTimeout).ConfigureAwait(false);
            if (list is not null)
            {
                foreach (var k in list.Keys)
                {
                    var title = _catalog.Get(lang, k);
                    if (title is not null && seen.Add(k))
                    {
                        result.Add(title);
                    }
                }
            }

            if (result.Count >= 24)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>So viele Seiten tief reichen die Listen des Geschmacks-Checks höchstens (je Seite 20 Titel je Lager).</summary>
    public const int TasteDepth = 8;

    /// <summary>
    /// Die Listen für den Geschmacks-Check: je Lager die Titel mit den meisten Stimmen. Mit <paramref name="pages"/> reichen
    /// sie tiefer, wenn das Konto die bekanntesten schon bewertet hat.
    /// </summary>
    public async Task<Dictionary<string, List<Title>>> TasteListsAsync(IReadOnlyCollection<TasteCamp> camps, string lang, int pages, CancellationToken ct)
    {
        var result = new System.Collections.Concurrent.ConcurrentDictionary<string, List<Title>>(StringComparer.Ordinal);
        await Task.WhenAll(camps.Select(async camp =>
        {
            var titles = new List<Title>();
            var seen = new HashSet<MediaKey>();
            // Lager, die auch zum Erkennen dienen (Stichwort statt Genre), reichen von Anfang an eine Seite tiefer.
            var depth = Math.Clamp(pages + (camp.Id.StartsWith("kw:", StringComparison.Ordinal) && !camp.Tv ? 1 : 0), 1, TasteDepth);
            for (var page = 1; page <= depth; page++)
            {
                var list = await ListAsync(lang, camp.Path + "&page=" + Num(page), Week, Shape.Results, false, camp.Tv, ct, SeerrClient.QuickTimeout).ConfigureAwait(false);
                foreach (var key in list?.Keys ?? Array.Empty<MediaKey>())
                {
                    if (seen.Add(key) && _catalog.Get(lang, key) is { } title)
                    {
                        titles.Add(title);
                    }
                }
            }

            result[camp.Id] = titles;
        })).ConfigureAwait(false);
        return new Dictionary<string, List<Title>>(result, StringComparer.Ordinal);
    }

    /// <param name="schedule">Auch dann neu holen, wenn die Termine fehlen (Folgen je Staffel, Termin für zu Hause).</param>
    /// <param name="maxAge">Höchstalter in Sekunden, wenn es frischer sein soll als üblich (0 = üblich).</param>
    public async Task<int> DetailsAsync(IReadOnlyCollection<(MediaKey Key, bool Text)> keys, string lang, Action<float>? progress, CancellationToken ct, bool schedule = false, long maxAge = 0)
    {
        var today = Scorer.Today(DateTime.Now);
        var todo = new Dictionary<MediaKey, bool>();
        foreach (var k in keys)
        {
            if (_catalog.NeedsDetail(k.Key, k.Text ? lang : null, today, schedule, maxAge))
            {
                todo[k.Key] = true;
            }
            else if (k.Text)
            {
                _catalog.FromDetail(lang, k.Key);
            }
        }

        if (todo.Count == 0)
        {
            return 0;
        }

        int done = 0, fetched = 0;
        await Task.WhenAll(todo.Keys.Select(async key =>
        {
            using var doc = await _seerr.GetAsync(key.Type + "/" + Num(key.Id) + "?language=" + lang, SeerrClient.DetailTimeout, ct).ConfigureAwait(false);
            if (doc is not null)
            {
                var detail = SeerrParse.Detail(doc.RootElement, key, lang, _catalog.NotePerson, _catalog.NoteKeyword, today);
                if (detail is not null)
                {
                    _catalog.PutDetail(detail);
                    _catalog.FromDetail(lang, key);
                    Interlocked.Increment(ref fetched);
                }
            }

            progress?.Invoke(Interlocked.Increment(ref done) / (float)todo.Count);
        })).ConfigureAwait(false);
        return fetched;
    }

    // ---- Plan der Listenabrufe ---------------------------------------------------------------------------------

    private List<Func<Task>> Plan(Pool pool, Profile profile, LibrarySnapshot lib, string lang, DateTime local, CancellationToken ct)
    {
        var jobs = new List<Func<Task>>();
        var positive = profile.Signals.Where(s => s.Strong && s.Weight > 0f).ToList();

        // Was Seerr zu den stärksten Signalen empfiehlt; „ähnlich“ ist gröber (nur Genres und Stichworte) und zählt weniger.
        // Serien sind unter den Signalen meist in der Minderheit und schwächer gewichtet (halb gesehen); die stärksten vier
        // kommen deshalb in jedem Fall dazu, sonst gäbe es für Serien kaum persönliche Kandidaten.
        // Angefragtes ist ein schwächerer Hinweis als Gesehenes (man kennt den Titel noch nicht) und kommt oft gehäuft:
        // höchstens vier solcher Titel dürfen Empfehlungen anstoßen, sonst verdrängen frische Anfragen alles Gesehene.
        var seeds = new List<Signal>(RecommendationSignals + 4);
        var requestedSeeds = 0;
        foreach (var s in positive)
        {
            if (seeds.Count >= RecommendationSignals)
            {
                break;
            }

            if (s.Kind == SignalKind.Requested && ++requestedSeeds > 4)
            {
                continue;
            }

            seeds.Add(s);
        }

        seeds.AddRange(positive.Where(s => s.Key.Tv && s.Kind != SignalKind.Requested && !seeds.Contains(s)).Take(Math.Max(0, 4 - seeds.Count(s => s.Key.Tv))));
        foreach (var s in seeds)
        {
            jobs.Add(() => RecommendAsync(pool, s, "recommendations", 1f, Src.Recommendation, lang, ct));
        }

        foreach (var s in seeds.Take(SimilarSignals))
        {
            jobs.Add(() => RecommendAsync(pool, s, "similar", 0.45f, Src.Similar, lang, ct));
        }

        // Je Lieblings-Genre: bestbewertet und neu/beliebt; ohne Profil breit gestreute Genres.
        var genres = profile.TopGenres.Where(GenreMap.IsMovieGenre).Take(MovieGenres).ToList();
        foreach (var g in GenreMap.Defaults)
        {
            if (genres.Count >= MovieGenres)
            {
                break;
            }

            if (!genres.Contains(g) && profile.Genres.GetValueOrDefault(g) >= 0f)
            {
                genres.Add(g);
            }
        }

        var tvDone = new HashSet<int>();
        foreach (var g in genres)
        {
            jobs.AddRange(GenreJobs(pool, g, lang, local, Src.GenreTop, false, ct));
            lock (pool)
            {
                pool.Genres.Add(g);
            }

            var tv = GenreMap.TvListGenre(g);
            if (tv > 0 && tvDone.Count < TvGenres && tvDone.Add(tv))
            {
                jobs.AddRange(TvGenreJobs(pool, tv, lang, local, Src.GenreTop, ct));
            }
        }

        foreach (var g in genres.Take(3))
        {
            jobs.Add(() => FlagAsync(pool, "discover/movies?genre=" + Num(g) + "&sortBy=vote_average.desc&voteCountGte=400&voteCountLte=3000&page=1", Days2, false, false, Src.Gem, "gems", lang, ct));
        }

        // Regie und Darsteller, die in mindestens zwei starken positiven Signalen vorkommen.
        foreach (var person in profile.People.Where(p => p.Value.StrongHits >= 2).OrderByDescending(p => p.Value.Weight).Take(PeopleLists))
        {
            var id = _catalog.PersonId(person.Key);
            if (id > 0)
            {
                var feature = person.Key;
                var name = person.Value.Name;
                jobs.Add(() => PersonAsync(pool, feature, name, id, lang, ct));
            }
        }

        foreach (var keyword in profile.Keywords.OrderByDescending(k => k.Value).Take(KeywordLists))
        {
            var id = keyword.Key;
            jobs.Add(() => KeywordAsync(pool, id, lang, ct));
        }

        jobs.Add(() => FlagAsync(pool, "discover/trending?page=1", TimeSpan.FromHours(3), true, null, Src.Trending, "trending", lang, ct));
        jobs.Add(() => FlagAsync(pool, "discover/trending?page=2", TimeSpan.FromHours(3), true, null, Src.Trending, "trending", lang, ct));
        jobs.Add(() => FlagAsync(pool, "discover/movies/upcoming?page=1", HalfDay, true, false, Src.Upcoming, "upcoming", lang, ct));
        jobs.Add(() => FlagAsync(pool, "discover/movies/upcoming?page=2", HalfDay, true, false, Src.Upcoming, "upcoming", lang, ct));
        jobs.Add(() => FlagAsync(pool, "discover/tv/upcoming?page=1", HalfDay, true, true, Src.Upcoming, "upcoming", lang, ct));

        // Zeitlose Bestenliste: sehr viele Stimmen, sehr hohe Bewertung.
        for (var page = 1; page <= 3; page++)
        {
            var p = page;
            jobs.Add(() => FlagAsync(pool, "discover/movies?sortBy=vote_average.desc&voteCountGte=8000&page=" + Num(p), Week, false, false, Src.Classic, "classics", lang, ct));
        }

        for (var page = 1; page <= 2; page++)
        {
            var p = page;
            jobs.Add(() => FlagAsync(pool, "discover/tv?sortBy=vote_average.desc&voteCountGte=3000&page=" + Num(p), Week, false, true, Src.Classic, "classics", lang, ct));
        }

        // Filmreihen, aus denen das Konto mindestens einen Teil gesehen hat.
        var collections = new List<int>();
        foreach (var s in positive)
        {
            if (s.Key.Tv || s.Kind is SignalKind.Requested)
            {
                continue;
            }

            var id = _catalog.Detail(s.Key)?.Collection ?? 0;
            if (id <= 0)
            {
                id = s.Lib?.Collection ?? 0;
            }

            if (id > 0 && !collections.Contains(id) && collections.Count < CollectionLists)
            {
                collections.Add(id);
                jobs.Add(() => CollectionAsync(pool, id, lib, lang, ct));
            }
        }

        return jobs;
    }

    private List<Func<Task>> GenreJobs(Pool pool, int genre, string lang, DateTime local, Src flag, bool withTv, CancellationToken ct)
    {
        var jobs = new List<Func<Task>>();
        var since = Since(local);
        if (GenreMap.IsMovieGenre(genre))
        {
            for (var page = 1; page <= 2; page++)
            {
                var p = page;
                jobs.Add(() => FlagAsync(pool, "discover/movies?genre=" + Num(genre) + "&sortBy=vote_average.desc&voteCountGte=1500&page=" + Num(p), Days2, false, false, flag, "genreTop", lang, ct));
            }

            jobs.Add(() => FlagAsync(pool, "discover/movies?genre=" + Num(genre) + "&sortBy=popularity.desc&primaryReleaseDateGte=" + since + "&voteCountGte=100&page=1", HalfDay, false, false, flag == Src.Deep ? Src.Deep : Src.GenreNew, "genreNew", lang, ct));
        }

        if (withTv)
        {
            var tv = GenreMap.IsTvGenre(genre) && !GenreMap.IsMovieGenre(genre) ? genre : GenreMap.TvListGenre(genre);
            if (tv > 0)
            {
                jobs.AddRange(TvGenreJobs(pool, tv, lang, local, flag, ct));
            }
        }

        return jobs;
    }

    private List<Func<Task>> TvGenreJobs(Pool pool, int genre, string lang, DateTime local, Src flag, CancellationToken ct)
    {
        var jobs = new List<Func<Task>>();
        var since = Since(local);
        for (var page = 1; page <= 2; page++)
        {
            var p = page;
            jobs.Add(() => FlagAsync(pool, "discover/tv?genre=" + Num(genre) + "&sortBy=vote_average.desc&voteCountGte=400&page=" + Num(p), Days2, false, true, flag, "genreTop", lang, ct));
        }

        jobs.Add(() => FlagAsync(pool, "discover/tv?genre=" + Num(genre) + "&sortBy=popularity.desc&firstAirDateGte=" + since + "&voteCountGte=40&page=1", HalfDay, false, true, flag == Src.Deep ? Src.Deep : Src.GenreNew, "genreNew", lang, ct));
        return jobs;
    }

    /// <summary>„Neu“ heißt: in den letzten anderthalb Jahren erschienen. Auf den Monatsersten gerundet, damit der Zwischenspeicher greift.</summary>
    private static string Since(DateTime local)
    {
        var d = new DateTime(local.Year, local.Month, 1).AddMonths(-18);
        return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    // ---- Einzelne Quellen --------------------------------------------------------------------------------------

    private async Task RecommendAsync(Pool pool, Signal signal, string kind, float factor, Src flag, string lang, CancellationToken ct)
    {
        var list = await ListAsync(lang, signal.Key.Type + "/" + Num(signal.Key.Id) + "/" + kind + "?page=1", Days3, Shape.Results, true, signal.Key.Tv, ct).ConfigureAwait(false);
        if (list is null)
        {
            return;
        }

        lock (pool)
        {
            var n = Math.Max(1, list.Keys.Length);
            for (var i = 0; i < list.Keys.Length; i++)
            {
                var c = pool.Get(list.Keys[i]);
                c.Sources |= flag;
                // Vordere Plätze der Liste zählen voll, hintere noch halb.
                var share = signal.Weight * factor * (1f - (0.5f * i / n));
                c.Support += share;
                if (share > c.BecauseShare)
                {
                    c.BecauseShare = share;
                    c.Because = signal.Key;
                }
            }

            if (flag == Src.Recommendation)
            {
                pool.Recs[signal.Key] = list.Keys;
            }

            Bump(pool, kind, list.Keys.Length);
        }
    }

    private async Task FlagAsync(Pool pool, string path, TimeSpan ttl, bool sure, bool? tv, Src flag, string source, string lang, CancellationToken ct)
    {
        var list = await ListAsync(lang, path, ttl, Shape.Results, sure, tv, ct).ConfigureAwait(false);
        if (list is null)
        {
            return;
        }

        // Die zweite Seite von „angesagt“ zählt hinter der ersten weiter.
        var offset = flag == Src.Trending && path.EndsWith("page=2", StringComparison.Ordinal) ? 20 : 0;
        lock (pool)
        {
            for (var i = 0; i < list.Keys.Length; i++)
            {
                var c = pool.Get(list.Keys[i]);
                c.Sources |= flag;
                if (flag == Src.Trending && c.TrendRank == 0)
                {
                    c.TrendRank = offset + i + 1;
                }
            }

            Bump(pool, source, list.Keys.Length);
        }
    }

    private async Task PersonAsync(Pool pool, string feature, string name, int id, string lang, CancellationToken ct)
    {
        var list = await ListAsync(lang, "person/" + Num(id) + "/combined_credits", Week, Shape.Credits, true, null, ct).ConfigureAwait(false);
        if (list is null || list.Directs is null)
        {
            return;
        }

        lock (pool)
        {
            var directs = false;
            for (var i = 0; i < list.Keys.Length; i++)
            {
                var c = pool.Get(list.Keys[i]);
                c.Sources |= Src.Person;
                if (c.Person is null || (list.Directs[i] && !c.PersonDirects))
                {
                    c.Person = feature;
                    c.PersonDirects = list.Directs[i];
                }

                directs |= list.Directs[i];
            }

            pool.People[feature] = new PersonList(feature, name, directs, list.Keys);
            Bump(pool, "person", list.Keys.Length);
        }
    }

    private async Task KeywordAsync(Pool pool, int id, string lang, CancellationToken ct)
    {
        var list = await ListAsync(lang, "discover/keyword/" + Num(id) + "/movies?page=1", Days3, Shape.Results, true, false, ct).ConfigureAwait(false);
        if (list is null)
        {
            return;
        }

        lock (pool)
        {
            foreach (var key in list.Keys)
            {
                var c = pool.Get(key);
                c.Sources |= Src.Keyword;
                if (c.Keyword == 0)
                {
                    c.Keyword = id;
                }
            }

            Bump(pool, "keyword", list.Keys.Length);
        }
    }

    private async Task CollectionAsync(Pool pool, int id, LibrarySnapshot lib, string lang, CancellationToken ct)
    {
        var list = await ListAsync(lang, "collection/" + Num(id), Week, Shape.Parts, true, false, ct).ConfigureAwait(false);
        if (list is null || list.Keys.Length < 2)
        {
            return;
        }

        lock (pool)
        {
            var missing = 0;
            foreach (var key in list.Keys)
            {
                if (lib.Items.ContainsKey(key))
                {
                    continue;
                }

                var c = pool.Get(key);
                c.Sources |= Src.Collection;
                c.Collection = id;
                missing++;
            }

            pool.Collections[id] = new CollectionList(id, list.Name ?? string.Empty, list.Keys);
            Bump(pool, "collection", missing);
        }
    }

    private static void Bump(Pool pool, string source, int count) =>
        pool.SourceCounts[source] = pool.SourceCounts.GetValueOrDefault(source) + count;

    /// <summary>Begrenzt den Bestand: Was nach der ersten Bewertung weit hinten liegt, wird nie gezeigt und fällt heraus.</summary>
    private static void Limit(Pool pool, Snapshot first, StateView state)
    {
        if (pool.Items.Count <= Scorer.PoolLimit)
        {
            return;
        }

        var keep = new HashSet<MediaKey>();
        foreach (var s in first.Ranked.Take(Scorer.PoolLimit))
        {
            keep.Add(s.Key);
        }

        foreach (var s in first.Upcoming.Take(80))
        {
            keep.Add(s.Key);
        }

        // Bewertete und angefragte Titel bleiben, damit sie nach „Rückmeldung löschen“ sofort wieder da sind.
        foreach (var key in pool.Items.Keys.ToList())
        {
            if (!keep.Contains(key) && !state.Feedback.ContainsKey(key) && !first.OpenRequests.Contains(key) && pool.Items[key].Collection == 0)
            {
                pool.Items.Remove(key);
            }
        }
    }

    // ---- Abruf mit Zwischenspeicher ----------------------------------------------------------------------------

    private async Task LearnGenresAsync(string lang, CancellationToken ct)
    {
        foreach (var tv in new[] { false, true })
        {
            var path = "genres/" + (tv ? "tv" : "movie") + "?language=" + lang;
            var cacheKey = lang + "|" + path;
            if (_catalog.List(cacheKey, Week) is not null)
            {
                continue;
            }

            using var doc = await _seerr.GetAsync(path, SeerrClient.ListTimeout, ct).ConfigureAwait(false);
            if (doc is null || doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                continue;
            }

            foreach (var g in doc.RootElement.EnumerateArray())
            {
                GenreMap.Learn(tv, SeerrParse.Int(g, "id"), SeerrParse.Str(g, "name"));
            }

            _catalog.PutList(cacheKey, new CachedList { At = DateTime.UtcNow });
        }
    }

    /// <param name="sure">Der Abruf beachtet <c>language</c> als Anzeigesprache (bei den gefilterten Listen tut er das nicht).</param>
    private async Task<CachedList?> ListAsync(string lang, string path, TimeSpan ttl, Shape shape, bool sure, bool? tv, CancellationToken ct, int timeout = SeerrClient.ListTimeout)
    {
        var cacheKey = lang + "|" + path;
        var hit = _catalog.List(cacheKey, ttl);
        if (hit is not null)
        {
            Interlocked.Increment(ref _cacheHits);
            return hit;
        }

        // Bei den gefilterten Listen würde „language“ nach Originalsprache filtern; dort bleibt der Parameter weg.
        var url = sure ? path + (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "language=" + lang : path;
        using var doc = await _seerr.GetAsync(url, timeout, ct).ConfigureAwait(false);
        if (doc is null)
        {
            // Fällt ein Abruf aus, tut es auch der veraltete Stand.
            return _catalog.List(cacheKey, TimeSpan.FromDays(60));
        }

        CachedList list;
        if (shape == Shape.Credits)
        {
            var credits = SeerrParse.Credits(doc.RootElement);
            var keys = new List<MediaKey>(credits.Count);
            var directs = new List<bool>(credits.Count);
            var index = new Dictionary<MediaKey, int>();
            foreach (var (title, isDirector) in credits)
            {
                _catalog.Put(lang, title);
                if (index.TryGetValue(title.Key, out var at))
                {
                    directs[at] |= isDirector;
                }
                else
                {
                    index[title.Key] = keys.Count;
                    keys.Add(title.Key);
                    directs.Add(isDirector);
                }
            }

            list = new CachedList { At = DateTime.UtcNow, Keys = keys.ToArray(), Directs = directs.ToArray() };
        }
        else
        {
            var titles = SeerrParse.Items(doc.RootElement, shape == Shape.Parts ? "parts" : "results", tv, sure);
            var keys = new List<MediaKey>(titles.Count);
            foreach (var title in titles)
            {
                _catalog.Put(lang, title);
                if (!keys.Contains(title.Key))
                {
                    keys.Add(title.Key);
                }
            }

            list = new CachedList
            {
                At = DateTime.UtcNow,
                Keys = keys.ToArray(),
                Name = shape == Shape.Parts ? SeerrParse.Str(doc.RootElement, "name") : null,
            };
        }

        _catalog.PutList(cacheKey, list);
        return list;
    }
}
