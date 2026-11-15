using System.Globalization;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>Auskunft für Administratoren: wie die Empfehlungen eines Kontos zustande kommen.</summary>
public sealed partial class DiscoverService
{
    /// <summary>
    /// Der Stand der Anfragen eines Kontos, wie er gerade oder zu einer gestellten Uhrzeit angezeigt würde (ohne Abruf bei
    /// Seerr und ohne etwas festzuhalten). Zum Nachvollziehen von „heute/morgen“ und der Ruhezeit.
    /// </summary>
    internal object DebugRequests(Guid user, string lang, DateTime? atLocal, bool refresh = false)
    {
        if (!Enabled)
        {
            return new { status = "empty", error = "off" };
        }

        var account = _users.GetUserById(user);
        if (account is null)
        {
            return new { status = "none" };
        }

        if (refresh)
        {
            _tracker.ForgetDates(account);
        }

        var now = atLocal ?? DateTime.Now;
        var view = atLocal.HasValue ? _tracker.At(account, lang, atLocal.Value) : _tracker.Peek(account, lang);
        var inv = CultureInfo.InvariantCulture;
        var quiet = QuietHours.Read();
        return new
        {
            status = view.Known ? "ready" : "empty",
            serverTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", inv),
            timeZone = TimeZoneInfo.Local.Id,
            at = now.ToString("yyyy-MM-ddTHH:mm:ss", inv),
            listAgeSeconds = _requests.Known.At > DateTime.MinValue ? Math.Round((DateTime.UtcNow - _requests.Known.At).TotalSeconds, 1) : (double?)null,
            listFetches = _requests.Fetches,
            seerrRequests = _seerr.Requests,
            quietHours = quiet is { } q ? new { from = q.From.ToString(@"hh\:mm", inv), to = q.To.ToString(@"hh\:mm", inv), marker = q.Marker } : null,
            active = view.Active,
            poll = view.Poll,
            items = view.Items.Select(e => new
            {
                key = e.Key.ToString(),
                title = e.Title?.Name,
                phase = e.Phase.ToString().ToLowerInvariant(),
                percent = e.Percent,
                eta = e.Eta?.ToString("yyyy-MM-ddTHH:mm:ssZ", inv),
                text = Texts.RequestText(e, lang, now),
                @short = Texts.RequestShort(e, lang, now),
                requestedAt = e.RequestedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", inv),
                availableAt = e.AvailableAt?.ToString("yyyy-MM-ddTHH:mm:ssZ", inv),
                availableFrom = e.AvailableFrom,
                wait = e.Wait.ToString(),
                longSearch = e.LongSearch,
                release = e.Detail?.Date,
                homeRelease = e.Detail?.HomeRelease,
                next = e.Detail is { NextDate: > 0 } d ? "S" + d.NextSeason.ToString(inv) + "E" + d.NextEpisode.ToString(inv) + " " + d.NextDate.ToString(inv) : null,
                aired = e.Detail?.Episodes,
                totals = e.Detail?.EpisodeCounts,
                detailAgeHours = e.Detail is { } known ? Math.Round((Catalog.Now - known.At) / 3600.0, 1) : (double?)null,
                isNew = e.IsNew,
                seasons = Texts.RequestSeasons(e.Seasons, e.Detail?.Seasons ?? 0, lang),
                files = e.Files,
                have = e.Have,
                expected = e.Expected,
                owner = e.Owner,
                inLibrary = e.Lib is not null,
            }),
        };
    }

    /// <summary>Alles zur Feinabstimmung: Profil, Signale, Quellen, Zeiten und die besten Kandidaten mit ihren Bestandteilen.</summary>
    internal object Debug(Guid user, string lang, bool build, int top, string? find)
    {
        if (!Enabled)
        {
            return new { status = "empty", error = "off" };
        }

        if (build)
        {
            GetPage(user, lang, "all", 0);
        }

        if (!_sessions.TryGetValue((user, lang), out var session))
        {
            return new { status = "none", hint = "Für dieses Konto und diese Sprache wurde noch nichts berechnet (build=1 stößt es an)." };
        }

        var snap = session.Snap;
        if (snap is null)
        {
            return new { status = session.Building ? "building" : "empty", progress = session.Progress, error = session.Error };
        }

        var p = snap.Profile;
        var inv = CultureInfo.InvariantCulture;
        return new
        {
            status = "ready",
            generatedAt = snap.BuiltAt.ToString("yyyy-MM-ddTHH:mm:ssZ", inv),
            building = session.Building,
            reason = snap.Info.Reason,
            alpha = Math.Round(p.Alpha, 3),
            n = Math.Round(p.N, 2),
            strongSignals = p.StrongCount,
            weights = new
            {
                genre = Scorer.GenreShare,
                support = Scorer.SupportShare,
                people = Scorer.PeopleShare,
                decade = Scorer.DecadeShare,
                foreignLanguage = Scorer.ForeignFactor,
                formatPenalty = Scorer.FormatPenalty,
                supportScale = Scorer.SupportScale,
            },
            signals = p.Signals.Where(s => s.Kind != SignalKind.Unseen).Take(200).Select(s => new
            {
                key = s.Key.ToString(),
                name = s.Name,
                kind = s.Kind.ToString(),
                @base = s.Base,
                weight = Math.Round(s.Weight, 3),
                strong = s.Strong,
                when = s.When?.ToString("yyyy-MM-dd", inv),
                details = _catalog.Detail(s.Key) is not null,
            }),
            unseen = new { count = p.Signals.Count(s => s.Kind == SignalKind.Unseen), weightEach = Math.Round(p.Signals.FirstOrDefault(s => s.Kind == SignalKind.Unseen)?.Weight ?? 0f, 4) },
            profile = new
            {
                genres = p.Genres.OrderByDescending(g => g.Value).Select(g => new { feature = "genre:" + g.Key.ToString(inv), name = GenreMap.Name(g.Key, lang), weight = Math.Round(g.Value, 3) }),
                people = p.People.OrderByDescending(x => x.Value.Weight).Take(30).Select(x => new { feature = "person:" + x.Key, name = x.Value.Name, weight = Math.Round(x.Value.Weight, 3), directs = x.Value.Directs, strongHits = x.Value.StrongHits }),
                keywords = p.Keywords.OrderByDescending(k => k.Value).Take(30).Select(k => new { feature = "kw:" + k.Key.ToString(inv), name = _catalog.Keyword(k.Key), weight = Math.Round(k.Value, 3) }),
                decades = p.Decades.OrderByDescending(d => d.Value).Select(d => new { feature = "decade:" + d.Key.ToString(inv), weight = Math.Round(d.Value, 3) }),
                languages = p.Langs.OrderByDescending(l => l.Value).Select(l => new { lang = l.Key, weight = Math.Round(l.Value, 2) }),
                muted = snap.State.Muted,
            },
            library = new { movies = snap.Lib.Movies, series = snap.Lib.Series, untouched = snap.Library.Length },
            sources = snap.Pool.SourceCounts.OrderBy(s => s.Key, StringComparer.Ordinal).ToDictionary(s => s.Key, s => s.Value),
            pool = new
            {
                candidates = snap.Pool.Items.Count,
                ranked = snap.Ranked.Length,
                upcoming = snap.Upcoming.Length,
                openRequests = snap.OpenRequests.Count,
                recommendationSeeds = snap.Pool.Recs.Count,
                people = snap.Pool.People.Values.Select(l => l.Name),
                collections = snap.Pool.Collections.Values.Select(c => c.Name),
                genres = snap.Pool.Genres.Select(g => GenreMap.Name(g, lang)),
            },
            timingsMs = snap.Info.Timings,
            seerr = new
            {
                requests = snap.Info.SeerrRequests,
                failed = snap.Info.SeerrFailed,
                listCacheHits = snap.Info.CacheHits,
                detailsFetched = snap.Info.DetailsFetched,
                totalRequests = _seerr.Requests,
                totalFailed = _seerr.Failed,
                totalBytes = _seerr.Bytes,
            },
            cache = new { lists = _catalog.ListCount, titles = _catalog.TitleCount, details = _catalog.DetailCount, languageDiffers = _catalog.LanguageDiffers(lang) },
            service = new { buildsStarted = Interlocked.Read(ref _builds), sessions = _sessions.Count },
            top = (string.IsNullOrEmpty(find)
                ? snap.Ranked.Take(Math.Clamp(top, 1, 500))
                : snap.Ranked.Concat(snap.Upcoming).Concat(snap.Library).Where(s => s.Title.Name.Contains(find, StringComparison.OrdinalIgnoreCase)).Take(60)).Select(s => new
            {
                rank = Array.IndexOf(snap.Ranked, s) + 1,
                key = s.Key.ToString(),
                title = s.Title.Name,
                year = s.Title.Year,
                lang = s.Title.Lang,
                score = Math.Round(s.Score, 4),
                match = s.Match,
                taste = Math.Round(s.Taste, 3),
                quality = Math.Round(s.Quality, 3),
                genre = Math.Round(s.Genre, 3),
                support = Math.Round(s.Support, 3),
                people = Math.Round(s.People, 3),
                decade = Math.Round(s.Decade, 3),
                langFactor = Math.Round(s.LangFactor, 2),
                formatFactor = Math.Round(s.FormatFactor, 2),
                shownFactor = Math.Round(s.ShownFactor, 2),
                jitter = Math.Round(s.Jitter, 3),
                rating = Math.Round(s.Title.Rating, 1),
                votes = s.Title.Votes,
                sources = s.Candidate?.Sources.ToString(),
                because = s.Candidate is { Support: > 0f } c ? p.SignalFor(c.Because)?.Name : null,
                person = s.PersonHit,
                details = s.Detail is not null,
                reason = Pages.Item(s, snap, "top10").Reason,
            }),
        };
    }
}
