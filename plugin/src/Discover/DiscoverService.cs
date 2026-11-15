using System.Collections.Concurrent;
using System.Globalization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>
/// „Entdecken“: persönliche Empfehlungen je Konto, vor allem für Titel, die in der Bibliothek fehlen.
/// </summary>
/// <remarks>
/// Gerechnet wird nur, wenn ein Konto die Seite oder die Startseite öffnet: die erste Berechnung im Hintergrund,
/// danach kommen alle Antworten aus dem Arbeitsspeicher. Es gibt keine Zeitpläne. Ein Stand, der älter als zwölf
/// Stunden ist oder nach dem sich Gesehenes, Favoriten oder die Bibliothek geändert haben, wird beim nächsten Abruf
/// noch ausgeliefert und dabei im Hintergrund erneuert. Rückmeldungen ordnen den vorhandenen Bestand sofort neu.
/// </remarks>
public sealed partial class DiscoverService : IHostedService, IDisposable
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(12);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RebuildGap = TimeSpan.FromSeconds(20);

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _users;
    private readonly IUserDataManager _userData;
    private readonly ILogger<DiscoverService> _log;
    private readonly SeerrClient _seerr;
    private readonly Catalog _catalog;
    private readonly StateStore _store;
    private readonly LibraryReader _library;
    private readonly Builder _builder;
    private readonly RequestSource _requests;
    private readonly RequestTracker _tracker;
    private readonly ConcurrentDictionary<(Guid User, string Lang), Session> _sessions = new();
    private readonly ConcurrentDictionary<Guid, long> _userChanged = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Evaluator _evaluator;
    private readonly object _configLock = new();
    private long _libraryChanged;
    private long _builds;
    private string _configStamp = string.Empty;
    private HashSet<MediaKey>? _libraryKeys;
    private long _libraryKeysAt;
    private DateTime _lastWarning;
    private bool _disposed;

    public DiscoverService(IHttpClientFactory http, ILibraryManager libraryManager, IUserManager users, IUserDataManager userData, ILogger<DiscoverService> log)
    {
        _libraryManager = libraryManager;
        _users = users;
        _userData = userData;
        _log = log;
        _seerr = new SeerrClient(http);
        _catalog = new Catalog(Folder);
        _store = new StateStore(Folder);
        _library = new LibraryReader(libraryManager, userData);
        _requests = new RequestSource(_seerr, _stop.Token);
        _builder = new Builder(_seerr, _catalog, _library, _store, _requests);
        _tracker = new RequestTracker(_requests, _catalog, _store, _library, _builder, ChangeStamp, _stop.Token);
        _evaluator = new Evaluator(_builder, _library, _store, _catalog, _stop.Token);

        // Nur merken, dass sich etwas geändert hat. Gerechnet wird erst, wenn das Konto das nächste Mal nachfragt.
        _userData.UserDataSaved += OnUserDataSaved;
        _libraryManager.ItemAdded += OnLibraryChanged;
        _libraryManager.ItemRemoved += OnLibraryChanged;
        _libraryManager.ItemUpdated += OnLibraryChanged;
    }

    /// <summary>Ordner für den Zustand der Konten, neben den Einstellungen des Plugins.</summary>
    internal static string Folder() =>
        Path.Combine(Path.GetDirectoryName(Plugin.Instance?.ConfigurationFilePath ?? string.Empty) ?? ".", "CinemaGlass-discover");

    /// <summary>Schalter an und Seerr eingerichtet; sonst gibt es keine Abrufe.</summary>
    internal static bool Enabled
    {
        get
        {
            var c = Plugin.Instance?.Configuration;
            return c is not null && c.Discover && c.Seerr && c.SeerrUrl.Length > 0 && c.SeerrApiKey.Length > 0;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Shutdown();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // Das Abbruchsignal bleibt bestehen: Arbeiten, die gerade enden, dürfen es noch abfragen.
        Shutdown();
    }

    private void Shutdown()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _userData.UserDataSaved -= OnUserDataSaved;
        _libraryManager.ItemAdded -= OnLibraryChanged;
        _libraryManager.ItemRemoved -= OnLibraryChanged;
        _libraryManager.ItemUpdated -= OnLibraryChanged;
        try
        {
            // Laufende Berechnungen brechen ab; bereits geholte Details gehen nicht verloren.
            _stop.Cancel();
            _catalog.Save();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or AggregateException)
        {
            // Beim Herunterfahren ist nichts mehr zu retten.
        }
    }

    // ---- Abrufe des Themes -------------------------------------------------------------------------------------

    internal PageDto GetPage(Guid user, string lang, string type, int genre)
    {
        if (!Enabled)
        {
            return new PageDto { Status = "empty", Error = "off", Progress = 0 };
        }

        var session = Touch(user, lang);
        var snap = session.Snap;
        var underway = Underway(user, lang);
        if (snap is null)
        {
            var waiting = Waiting(session, new PageDto(), (p, status, error, progress) =>
            {
                p.Status = status;
                p.Error = error;
                p.Progress = progress;
            });
            if (waiting.Status == "building" && underway.Count > 0)
            {
                // Vorläufig schon einmal, was nicht an der Berechnung hängt: der Stand der eigenen Anfragen.
                var now = DateTime.Now;
                waiting.Rows.Add(new RowDto { Id = "requests", Kind = "poster", Title = Texts.RequestsTitle(lang), Items = underway.Select(e => Pages.RequestItem(e, lang, now)).ToList() });
            }

            return waiting;
        }

        snap = Refresh(session, snap);

        // Hat sich seit der letzten Rangfolge geändert, was angefragt ist, wird neu geordnet: Angefragtes soll nicht mehr
        // empfohlen werden, Abgelehntes und Zurückgezogenes darf wieder.
        Scorer.Requests(user, _requests.Known, _store.View(user), _store.ThemeRequests(), snap.Lib, DateTime.UtcNow, out _, out var open);
        if (_requests.Known.At > DateTime.MinValue && !open.SetEquals(snap.OpenRequests))
        {
            snap = Rerank(session) ?? snap;
        }

        var page = Pages.Page(snap, type, genre, underway);
        if (page.Status != "ready")
        {
            return page;
        }

        if (genre > 0 && !snap.Pool.Genres.Contains(genre))
        {
            // Ein selten geschautes Genre hat im Bestand wenig Tiefe: eigene Listen nachholen, das Theme fragt gleich wieder.
            var state = session.DeepState(genre);
            if (state == 0 && Pages.Depth(snap, type, genre) < 60)
            {
                Deepen(session, genre);
                state = 1;
            }

            if (state == 1)
            {
                page.Status = "building";
                page.Progress = 0.6;
            }
        }

        if (_catalog.LanguageDiffers(lang))
        {
            FixLanguage(session, snap, page);
        }

        return page;
    }

    internal async Task<HomeDto> GetHomeAsync(Guid user, string lang, CancellationToken ct)
    {
        HomeDto home;
        if (!Enabled)
        {
            home = new HomeDto { Status = "empty", Title = Texts.Home(lang) };
            home.Requests.Title = Texts.RequestsTitle(lang);
            home.Arrived.Title = Texts.ArrivedTitle(lang);
            return home;
        }

        var session = Touch(user, lang);
        var snap = session.Snap;
        home = snap is null
            ? Waiting(session, new HomeDto { Title = Texts.Home(lang) }, (h, status, _, _) => h.Status = status)
            : Pages.Home(Refresh(session, snap));

        // Der Stand der Anfragen hängt nicht an der Berechnung der Empfehlungen; er kommt auch, während sie noch läuft.
        var view = await RequestViewAsync(user, lang, TimeSpan.FromMilliseconds(1500), ct).ConfigureAwait(false);
        var now = DateTime.Now;
        home.Requests = new HomeRequestsDto
        {
            Title = Texts.RequestsTitle(lang),
            Active = view.Active,
            Poll = view.Poll,
            Items = view.Items.Where(e => e.Active).Take(12).Select(e => Pages.RequestItem(e, lang, now)).ToList(),
        };
        home.Arrived = new HomeArrivedDto
        {
            Title = Texts.ArrivedTitle(lang),
            Items = view.Items.Where(e => e.IsNew).Take(12).Select(e => Pages.RequestItem(e, lang, now)).ToList(),
        };
        return home;
    }

    /// <summary>„Unterwegs zu dir“: alles, was das Konto angefragt hat und was noch nicht erledigt ist, dazu neu Angekommenes.</summary>
    internal async Task<RequestsDto> GetRequestsAsync(Guid user, string lang, CancellationToken ct)
    {
        if (!Enabled)
        {
            return new RequestsDto { Status = "empty" };
        }

        CheckConfig();
        var view = await RequestViewAsync(user, lang, TimeSpan.FromMilliseconds(2500), ct).ConfigureAwait(false);
        var now = DateTime.Now;
        return new RequestsDto
        {
            // Ohne je eine Antwort von Seerr lässt sich über Anfragen nichts sagen.
            Status = view.Known || view.Items.Count > 0 ? "ready" : "empty",
            Active = view.Active,
            Poll = view.Poll,
            Items = view.Items.Select(e => Pages.RequestItem(e, lang, now)).ToList(),
        };
    }

    private async Task<RequestView> RequestViewAsync(Guid user, string lang, TimeSpan budget, CancellationToken ct)
    {
        var account = _users.GetUserById(user);
        if (account is null)
        {
            return RequestView.None;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        try
        {
            return await _tracker.ViewAsync(account, lang, budget, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return RequestView.None;
        }
    }

    /// <summary>Die noch laufenden Anfragen des Kontos aus dem, was gerade bekannt ist (ohne auf Seerr zu warten).</summary>
    private List<RequestEntry> Underway(Guid user, string lang)
    {
        var account = _users.GetUserById(user);
        if (account is null)
        {
            return new List<RequestEntry>();
        }

        if (DateTime.UtcNow - _requests.Known.At > RequestSource.Fresh)
        {
            // Im Hintergrund auffrischen; die Seite selbst wartet nicht darauf.
            _ = Task.Run(async () =>
            {
                try
                {
                    await _tracker.ViewAsync(account, lang, TimeSpan.FromSeconds(8), _stop.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Der nächste Abruf versucht es wieder.
                }
            });
        }

        return _tracker.Peek(account, lang).Items.Where(e => e.Active && e.Title is not null).ToList();
    }

    internal async Task<SimilarDto> SimilarAsync(Guid user, string lang, MediaKey key, CancellationToken ct)
    {
        var dto = new SimilarDto();
        if (!Enabled)
        {
            dto.Status = "empty";
            return dto;
        }

        CheckConfig();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        var titles = await _builder.RelatedAsync(key, lang, linked.Token).ConfigureAwait(false);
        _sessions.TryGetValue((user, lang), out var session);
        var snap = session?.Snap;
        var state = snap?.State ?? _store.View(user);
        var owned = snap is not null ? null : LibraryKeys();
        var open = snap?.OpenRequests;
        if (open is null)
        {
            Scorer.Requests(user, _requests.Known, state, _store.ThemeRequests(), LibrarySnapshot.Empty, DateTime.UtcNow, out _, out open);
        }

        var scored = new List<Scored>();
        var today = Scorer.Today(DateTime.Now);
        for (var i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            var inLibrary = snap is not null ? snap.Lib.Items.ContainsKey(t.Key) : owned!.Contains(t.Key);
            if (inLibrary || state.Feedback.ContainsKey(t.Key) || open.Contains(t.Key) || t.Adult || t.Poster is null || t.Date > today || !Scorer.PassesFloor(t, null, today))
            {
                continue;
            }

            // Was Seerr zu diesem Titel weiter vorn nennt, zählt wie eine Empfehlung eines gemochten Titels.
            var place = 1f - (0.5f * i / Math.Max(1, titles.Count));
            if (snap is not null)
            {
                scored.Add(Scorer.ScoreLoose(snap, t, _catalog, user, 1.5f * place));
            }
            else
            {
                var quality = Scorer.Quality(t.Rating, t.Votes, t.Key.Tv);
                scored.Add(new Scored { Key = t.Key, Title = t, Detail = _catalog.Detail(t.Key), Score = quality * (0.7f + (0.3f * place)), Quality = quality, Match = 60 + (int)(35 * quality) });
            }
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        foreach (var s in scored.Take(18))
        {
            if (snap is not null)
            {
                dto.Items.Add(Pages.Item(s, snap, "similar"));
            }
            else
            {
                var item = Pages.Plain(s.Title, s.Detail, null, lang);
                item.Match = s.Match;
                var genre = s.Genres.Length > 0 ? GenreMap.Nice(s.Genres[0], lang, s.Key.Tv) : string.Empty;
                item.Reason = genre.Length > 0 ? Texts.GenreRating(genre, s.Title.Rating, lang) : Texts.Rating(s.Title.Rating, lang);
                dto.Items.Add(item);
            }
        }

        if (dto.Items.Count == 0)
        {
            dto.Status = "empty";
        }

        return dto;
    }

    /// <summary>So lange wird ein übersprungener Titel im Geschmacks-Check nicht wieder gefragt.</summary>
    private const int SkipDays = 180;

    /// <param name="groups">Welten (<c>genre:878,genre:53</c>), aus denen rund zwei Drittel der Titel stammen sollen; leer = breit gestreut.</param>
    internal async Task<TasteDto> TasteAsync(Guid user, string lang, string? groups, CancellationToken ct)
    {
        var state = _store.View(user);
        var dto = new TasteDto { Done = state.TasteDone };
        if (!Enabled)
        {
            return dto;
        }

        CheckConfig();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        var snap = AnySnapshot(user, lang);
        var lib = snap?.Lib;
        if (lib is null && _users.GetUserById(user) is { } account)
        {
            // Noch keine Berechnung für dieses Konto: Was es schon gesehen hat, soll trotzdem nicht gefragt werden.
            try
            {
                lib = _library.Read(account);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                lib = null;
            }
        }

        LibEntry? Entry(Title t) => lib is not null && lib.Items.TryGetValue(t.Key, out var entry) ? entry : null;

        // Was das Konto schon bewertet oder in der Bibliothek gesehen bzw. angefangen hat, ist bereits ein Signal.
        var skipLimit = Catalog.Now - (SkipDays * 86400L);
        bool Usable(Title t) => !state.Feedback.ContainsKey(t.Key) && !(state.Skipped.TryGetValue(t.Key, out var skipped) && skipped > skipLimit) && !t.Adult && t.Poster is not null
            && (Entry(t) is not { } entry || (entry.Visible && entry.Untouched && !entry.Favorite));

        // Auch nach dem ersten Durchgang soll es etwas zu bewerten geben („verfeinern“): Sind die bekanntesten Titel eines
        // Lagers verbraucht, reichen die Listen eine Seite tiefer.
        // Ist der Check erledigt, schärfen zwei Arten von Titeln das Profil am meisten: solche nah am Geschmack und solche
        // aus Bereichen, über die noch wenig bekannt ist. Ausdrücklich genannte Welten gehen dem vor.
        var focus = Worlds.Parse(groups?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var plan = focus.Length == 0 && state.TasteDone && snap is { Profile.TopGenres.Count: > 0 }
            ? TastePicker.Refine(snap.Profile)
            : TastePicker.Plan(focus);
        var camps = plan.Select(p => p.Camp).Concat(TastePicker.Guards).DistinctBy(camp => camp.Id).ToList();
        var chosen = new List<Title>();
        for (var depth = 1; depth <= Builder.TasteDepth; depth++)
        {
            var lists = await _builder.TasteListsAsync(camps, lang, depth, linked.Token).ConfigureAwait(false);
            chosen = TastePicker.Pick(plan, lists, Usable, t => _catalog.Detail(t.Key)?.Collection ?? 0, depth == Builder.TasteDepth);
            if (chosen.Count >= TastePicker.Count)
            {
                break;
            }
        }

        if (_catalog.LanguageDiffers(lang))
        {
            // Diese Listen kommen in der Sprache des Seerr-Kontos; die Namen der Auswahl einmalig über die Details nachholen.
            var unsure = chosen.Where(t => !t.Sure).Select(t => t.Key).ToList();
            if (unsure.Count > 0)
            {
                await _builder.FixLanguageAsync(unsure, lang, linked.Token).ConfigureAwait(false);
                _catalog.Save();
            }
        }

        foreach (var title in chosen)
        {
            dto.Items.Add(Pages.Plain(_catalog.Get(lang, title.Key) ?? title, _catalog.Detail(title.Key), Entry(title), lang));
        }

        // Die Welten: feste Reihenfolge, je drei Plakate der bekanntesten Filme des Genres (für alle Konten dieselben).
        var worldLists = await _builder.TasteListsAsync(Worlds.Genres.Select(TastePicker.Movies).ToList(), lang, 1, linked.Token).ConfigureAwait(false);
        var posters = TastePicker.Posters(worldLists);
        foreach (var genre in Worlds.Genres)
        {
            dto.Groups.Add(new TasteGroupDto
            {
                Feature = Worlds.Feature(genre),
                Name = GenreMap.Nice(genre, lang),
                Posters = posters.GetValueOrDefault(genre) ?? new List<string>(),
                Selected = Array.IndexOf(state.Worlds, genre) >= 0,
            });
        }

        return dto;
    }

    /// <summary>Setzt die im Geschmacks-Check gewählten Welten des Kontos (ersetzt die frühere Wahl) und ordnet sofort neu.</summary>
    internal FeedbackResultDto SetWorlds(Guid user, IEnumerable<string>? features)
    {
        var chosen = Worlds.Parse(features);
        var changed = _store.Update(user, s =>
        {
            if (s.Worlds.SequenceEqual(chosen))
            {
                return false;
            }

            s.Worlds = chosen.ToList();
            return true;
        });

        Snapshot? newest = null;
        foreach (var session in SessionsOf(user))
        {
            var snap = Rerank(session);
            if (snap is not null && (newest is null || snap.BuiltAt > newest.BuiltAt))
            {
                newest = snap;
            }

            // Für neu gewählte Genres fehlen dem Bestand noch die eigenen Listen; sie kommen nach, ohne dass jemand wartet.
            if (changed && Enabled)
            {
                session.Pending = true;
                EnsureBuild(session, "worlds");
            }
        }

        if (newest is not null)
        {
            return new FeedbackResultDto { Ok = true, Signals = newest.Profile.StrongCount, Confidence = Math.Round(newest.Profile.Alpha, 2) };
        }

        var view = _store.View(user);
        var signals = Profile.BuildSignals(LibrarySnapshot.Empty, view, new Dictionary<MediaKey, DateTime>(), DateTime.UtcNow);
        var profile = Profile.Build(signals, _catalog, "de", view.Muted, view.Worlds);
        return new FeedbackResultDto { Ok = true, Signals = profile.StrongCount, Confidence = Math.Round(profile.Alpha, 2) };
    }

    internal ProfileDto GetProfile(Guid user, string lang)
    {
        var state = _store.View(user);
        var snap = Enabled ? AnySnapshot(user, lang) : null;
        return Pages.Profile(snap, state, _catalog, lang);
    }

    internal FeedbackResultDto Feedback(Guid user, FeedbackInput input)
    {
        var now = Catalog.Now;
        var deepen = false;
        var source = input.Source is { Length: > 0 and <= 16 } ? input.Source : null;
        _store.Update(user, s =>
        {
            var any = false;
            foreach (var item in input.Items ?? new List<FeedbackItemInput>())
            {
                if (!MediaKey.TryParse(item.Key, out var key))
                {
                    continue;
                }

                var name = key.ToString();
                switch (item.Action)
                {
                    case "clear":
                        any |= s.Feedback.Remove(name) | s.Skipped.Remove(name);
                        break;
                    case "skip":
                        // „Kenne ich nicht“: sagt nichts über den Geschmack und bleibt empfehlbar; nur gefragt wird vorerst nicht mehr.
                        s.Skipped[name] = now;
                        any = true;
                        break;
                    case "like" or "dislike" or "hide":
                        s.Feedback[name] = new FeedbackEntry { Action = item.Action, At = now, Source = source };
                        any = true;
                        deepen |= item.Action != "hide";
                        break;
                }
            }

            if (source == "taste" && !s.TasteDone)
            {
                s.TasteDone = true;
                any = true;
            }

            return any;
        });

        Snapshot? newest = null;
        foreach (var session in SessionsOf(user))
        {
            var snap = Rerank(session);
            if (snap is not null && (newest is null || snap.BuiltAt > newest.BuiltAt))
            {
                newest = snap;
            }

            // Vertiefung: Details und Empfehlungen zu den neu bewerteten Titeln holen, ohne dass jemand darauf wartet.
            if (deepen && Enabled)
            {
                session.Pending = true;
                EnsureBuild(session, "feedback");
            }
        }

        if (newest is not null)
        {
            return new FeedbackResultDto { Ok = true, Signals = newest.Profile.StrongCount, Confidence = Math.Round(newest.Profile.Alpha, 2) };
        }

        // Noch keine Berechnung: wenigstens aus den Rückmeldungen selbst ableiten.
        var view = _store.View(user);
        var signals = Profile.BuildSignals(LibrarySnapshot.Empty, view, new Dictionary<MediaKey, DateTime>(), DateTime.UtcNow);
        var profile = Profile.Build(signals, _catalog, "de", view.Muted, view.Worlds);
        return new FeedbackResultDto { Ok = true, Signals = profile.StrongCount, Confidence = Math.Round(profile.Alpha, 2) };
    }

    internal bool Mute(Guid user, string feature, bool muted)
    {
        _store.Update(user, s =>
        {
            var has = s.Muted.Contains(feature);
            if (muted == has)
            {
                return false;
            }

            if (muted)
            {
                s.Muted.Add(feature);
            }
            else
            {
                s.Muted.Remove(feature);
            }

            return true;
        });

        foreach (var session in SessionsOf(user))
        {
            Rerank(session);
        }

        return true;
    }

    internal void Impressions(Guid user, List<string> keys)
    {
        var now = Catalog.Now;
        _store.Update(user, s =>
        {
            var any = false;
            foreach (var text in keys)
            {
                if (!MediaKey.TryParse(text, out var key))
                {
                    continue;
                }

                var name = key.ToString();
                s.Shown.TryGetValue(name, out var old);
                // Höchstens einmal je Besuch: Wiederholungen innerhalb einer halben Stunde zählen nicht.
                if (old is not null && now - old.At < 1800)
                {
                    continue;
                }

                s.Shown[name] = new ShownEntry { Count = Math.Min(99, (old?.Count ?? 0) + 1), At = now };
                any = true;
            }

            return any;
        });
    }

    /// <summary>Eine Anfrage ging über die Weiterleitung des Themes erfolgreich an Seerr: Konto und Titel festhalten.</summary>
    internal void NoteRequest(Guid user, MediaKey key)
    {
        var now = Catalog.Now;
        _store.Update(user, s =>
        {
            s.Requests[key.ToString()] = now;
            return true;
        });
        // Der gemerkte Stand der Anfragen ist damit überholt: Der nächste Abruf holt ihn neu und enthält den Titel.
        _requests.Forget();
        foreach (var session in SessionsOf(user))
        {
            Rerank(session);
            if (Enabled)
            {
                // Die Anfrage ist ein neues Signal; dafür braucht es die Merkmale des Titels.
                session.Pending = true;
                EnsureBuild(session, "request");
            }
        }
    }

    // ---- Hinweise von Jellyfin ---------------------------------------------------------------------------------

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        // Laufende Wiedergabe meldet sich alle paar Sekunden; für die Signale zählt erst das Ergebnis.
        if (e.SaveReason is UserDataSaveReason.PlaybackProgress or UserDataSaveReason.PlaybackStart)
        {
            return;
        }

        if (e.Item is Movie or Episode or Series or Season)
        {
            _userChanged[e.UserId] = DateTime.UtcNow.Ticks;
        }
    }

    private void OnLibraryChanged(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is Movie or Series || (e.Item is Episode && e.UpdateReason == 0))
        {
            Interlocked.Exchange(ref _libraryChanged, DateTime.UtcNow.Ticks);
        }
    }
}
