using System.Collections.Concurrent;
using System.Globalization;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>Wie weit ein angefragter Titel ist. Die Reihenfolge ist zugleich die Reihenfolge in der Liste.</summary>
internal enum RequestPhase
{
    Downloading,
    Importing,
    Queued,
    Partial,
    Searching,
    Scheduled,
    Unreleased,
    Approval,
    Available,
    Declined,
    Failed,
}

/// <summary>Warum es einen freigegebenen Titel noch nicht zu laden gibt.</summary>
internal enum WaitKind
{
    None,

    /// <summary>Der Termin für zu Hause (digital oder Disc) liegt noch vor uns.</summary>
    HomeRelease,

    /// <summary>Kommt erst noch ins Kino.</summary>
    CinemaSoon,

    /// <summary>Läuft im Kino, ein Termin für zu Hause ist nicht bekannt.</summary>
    InCinema,

    /// <summary>Die angefragte Staffel beginnt an einem bekannten Tag.</summary>
    SeasonStart,

    /// <summary>Die angefragte Staffel hat noch nicht begonnen, der Tag steht nicht fest.</summary>
    SeasonUnknown,
}

/// <summary>Der Stand eines angefragten Titels für ein Konto.</summary>
internal sealed class RequestEntry
{
    public MediaKey Key { get; init; }

    public RequestPhase Phase { get; set; }

    public int? Percent { get; set; }

    public DateTime? Eta { get; set; }

    public DateTime RequestedAt { get; init; }

    public DateTime? AvailableAt { get; set; }

    public bool IsNew { get; set; }

    /// <summary>Angefragte Staffeln (ohne Specials, außer es sind nur Specials angefragt); leer bei Filmen.</summary>
    public int[] Seasons { get; init; } = Array.Empty<int>();

    /// <summary>Zahl der Dateien im Download.</summary>
    public int Files { get; set; }

    /// <summary>Geplant: Ende der Ruhezeit in der Zeit des Servers.</summary>
    public DateTime? StartsAt { get; set; }

    /// <summary>Noch nicht erschienen: warum, und der Tag, den der Text nennt (JJJJMMTT, 0 = keiner).</summary>
    public WaitKind Wait { get; set; }

    public int WaitDate { get; set; }

    /// <summary>Ab wann mit dem Titel zu rechnen ist (JJJJMMTT): Termin für zu Hause, Start der Staffel oder nächste Folge; 0 = unbekannt.</summary>
    public int AvailableFrom { get; set; }

    /// <summary>Es wird schon länger als zwei Tage gesucht.</summary>
    public bool LongSearch { get; set; }

    /// <summary>Serie teilweise da: vorhandene und erwartete Folgen der angefragten Staffeln; Staffel, wenn nur eine unvollständig ist.</summary>
    public int Have { get; set; }

    public int Expected { get; set; }

    public int PartialSeason { get; set; }

    public LibEntry? Lib { get; init; }

    public Title? Title { get; set; }

    public Detail? Detail { get; set; }

    /// <summary>Woran das Konto als Eigentümer erkannt wurde: <c>seerr</c> (gleiche Jellyfin-Kennung) oder <c>theme</c>.</summary>
    public string Owner { get; init; } = string.Empty;

    public bool Active => Phase is not (RequestPhase.Available or RequestPhase.Declined or RequestPhase.Failed);
}

/// <summary>Die Anfragen eines Kontos, fertig sortiert.</summary>
internal sealed class RequestView
{
    public static readonly RequestView None = new();

    public List<RequestEntry> Items { get; init; } = new();

    /// <summary>Ob Seerr die Liste schon einmal geliefert hat; sonst weiß man über Anfragen nichts.</summary>
    public bool Known { get; init; }

    public int Active => Items.Count(e => e.Active);

    /// <summary>Nach wie vielen Sekunden sich Nachfragen lohnt: 10 beim Laden, 30 bei Suche, Warteschlange und Einordnen, 300 bei Geplantem und noch nicht Erschienenem, 0 sonst.</summary>
    public int Poll
    {
        get
        {
            var poll = 0;
            foreach (var e in Items)
            {
                var own = e.Phase switch
                {
                    RequestPhase.Downloading => 10,
                    RequestPhase.Importing or RequestPhase.Queued or RequestPhase.Searching => 30,
                    RequestPhase.Scheduled or RequestPhase.Unreleased or RequestPhase.Approval or RequestPhase.Partial => 300,
                    _ => 0,
                };
                if (own > 0 && (poll == 0 || own < poll))
                {
                    poll = own;
                }
            }

            return poll;
        }
    }
}

/// <summary>Was ein Konto angefragt hat: die Anfragen bei Seerr dazu und der Vermerk des Themes.</summary>
internal sealed class OwnRequest
{
    public List<SeerrRequest> Requests { get; } = new();

    /// <summary>Zeitpunkt der Anfrage über das Theme (UTC), wenn sie darüber kam.</summary>
    public DateTime? ThemeAt { get; set; }

    public bool BySeerrUser { get; set; }

    public DateTime When => Requests.Count > 0 ? Requests.Max(r => r.Created) : ThemeAt ?? DateTime.MinValue;
}

/// <summary>
/// Wem eine Anfrage gehört. Ein Titel gehört zum Konto, wenn der Seerr-Nutzer der Anfrage dieselbe Jellyfin-Kennung hat
/// oder wenn das Konto ihn über das Theme angefragt hat. Anfragen über das Theme laufen bei Seerr alle unter einem festen
/// Konto; hat das Plugin festgehalten, wer es war, gilt das und nicht der Seerr-Nutzer.
/// </summary>
internal static class RequestOwnership
{
    /// <summary>So nah müssen Vermerk des Themes und Anfrage bei Seerr zeitlich beieinander liegen, um dieselbe zu sein.</summary>
    private static readonly TimeSpan Match = TimeSpan.FromMinutes(5);

    /// <summary>So lange gilt ein Vermerk des Themes, zu dem Seerr (noch) keine Anfrage zeigt, als unterwegs.</summary>
    public static readonly TimeSpan ThemeGrace = TimeSpan.FromMinutes(10);

    /// <summary>So lange darf Jellyfin brauchen, bis ein laut Seerr vorhandener Titel in der Bibliothek auftaucht.</summary>
    public static readonly TimeSpan ImportGrace = TimeSpan.FromHours(2);

    public static Dictionary<MediaKey, OwnRequest> For(Guid user, RequestsInfo info, StateView state, IReadOnlyDictionary<MediaKey, List<(Guid User, long At)>> theme)
    {
        var own = new Dictionary<MediaKey, OwnRequest>();
        var hex = user.ToString("N", CultureInfo.InvariantCulture);
        var bound = new HashSet<MediaKey>();
        foreach (var r in info.Items)
        {
            var claimedBy = Guid.Empty;
            if (theme.TryGetValue(r.Key, out var notes))
            {
                var best = TimeSpan.MaxValue;
                foreach (var note in notes)
                {
                    var gap = (DateTimeOffset.FromUnixTimeSeconds(note.At).UtcDateTime - r.Created).Duration();
                    if (gap <= Match && gap < best)
                    {
                        best = gap;
                        claimedBy = note.User;
                    }
                }
            }

            var mine = claimedBy != Guid.Empty ? claimedBy == user : r.JellyfinUser == hex;
            if (!mine)
            {
                continue;
            }

            if (!own.TryGetValue(r.Key, out var entry))
            {
                own[r.Key] = entry = new OwnRequest();
            }

            entry.Requests.Add(r);
            if (claimedBy == user)
            {
                bound.Add(r.Key);
                entry.ThemeAt = DateTimeOffset.FromUnixTimeSeconds(state.Requests.GetValueOrDefault(r.Key)).UtcDateTime;
            }
            else
            {
                entry.BySeerrUser = true;
            }
        }

        // Vermerke, zu denen Seerr keine Anfrage zeigt: gerade erst gestellt, inzwischen gelöscht oder schon lange her.
        foreach (var note in state.Requests)
        {
            if (!bound.Contains(note.Key) && !own.ContainsKey(note.Key))
            {
                own[note.Key] = new OwnRequest { ThemeAt = DateTimeOffset.FromUnixTimeSeconds(note.Value).UtcDateTime };
            }
        }

        return own;
    }

    /// <summary>
    /// Titel, die nicht (noch einmal) empfohlen werden: offene Anfragen aller Nutzer, gerade Erledigtes (bis es in der
    /// Bibliothek auftaucht), vor Kurzem Abgelehntes und frische eigene Vermerke.
    /// </summary>
    public static HashSet<MediaKey> Open(RequestsInfo info, Dictionary<MediaKey, OwnRequest> own, DateTime now)
    {
        var open = new HashSet<MediaKey>();
        foreach (var r in info.Items)
        {
            if (r.Pending || (r.Status == 5 && now - r.Updated < ImportGrace) || (r.Status == 3 && now - r.Updated < TimeSpan.FromDays(30)))
            {
                open.Add(r.Key);
            }
        }

        foreach (var o in own)
        {
            if (o.Value.Requests.Count == 0 && o.Value.ThemeAt is { } at && now - at < ThemeGrace)
            {
                open.Add(o.Key);
            }
        }

        return open;
    }
}

/// <summary>Ruhezeit der Download-Dienste aus den Einstellungen, in der Zeit des Servers.</summary>
internal readonly record struct QuietHours(TimeSpan From, TimeSpan To, string Marker)
{
    public static QuietHours? Read()
    {
        var c = Plugin.Instance?.Configuration;
        if (c is null
            || !TimeOnly.TryParseExact(c.DownloadsQuietFrom?.Trim(), new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            || !TimeOnly.TryParseExact(c.DownloadsQuietTo?.Trim(), new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to)
            || from == to)
        {
            return null;
        }

        return new QuietHours(from.ToTimeSpan(), to.ToTimeSpan(), (c.DownloadsDeferMarker ?? string.Empty).Trim());
    }

    /// <summary>Liegt der Zeitpunkt in der Ruhezeit, deren Ende; sonst <c>null</c>.</summary>
    public DateTime? EndIfWithin(DateTime local)
    {
        var t = local.TimeOfDay;
        if (From < To)
        {
            return t >= From && t < To ? local.Date + To : null;
        }

        // Über Mitternacht: abends beginnt sie, morgens endet sie.
        return t >= From ? local.Date.AddDays(1) + To : t < To ? local.Date + To : null;
    }

    /// <summary>Ende der nächsten Ruhezeit ab diesem Zeitpunkt (der laufenden, wenn er in einer liegt).</summary>
    public DateTime NextEnd(DateTime local)
    {
        if (EndIfWithin(local) is { } end)
        {
            return end;
        }

        var start = local.Date + From;
        if (start <= local)
        {
            start = start.AddDays(1);
        }

        return From < To ? start.Date + To : start.Date.AddDays(1) + To;
    }
}

/// <summary>
/// „Unterwegs zu dir“: bestimmt für ein Konto, wie weit seine angefragten Titel sind. Seerr sagt, was angefragt ist und
/// was gerade lädt; ob ein Titel da ist und ob das Konto ihn gesehen hat, entscheidet Jellyfin.
/// </summary>
internal sealed class RequestTracker
{
    /// <summary>So viele Tage gilt Angekommenes als neu, solange es nicht gesehen ist; auch Abgelehntes bleibt so lange stehen.</summary>
    public const int NewDays = 14;

    private const int MaxItems = 30;

    /// <summary>So viele Tage nach dem Kinostart gilt ein Film ohne Termin für zu Hause noch als „im Kino“.</summary>
    private const int CinemaDays = 90;

    /// <summary>Ab dann heißt es statt „Wird gesucht“: „Noch nichts gefunden“.</summary>
    private static readonly TimeSpan LongSearchAfter = TimeSpan.FromHours(48);

    /// <summary>Gelingt das Nachholen der Termine nicht, wird es frühestens nach dieser Zeit wieder versucht.</summary>
    private static readonly TimeSpan RetryDates = TimeSpan.FromMinutes(10);

    private readonly RequestSource _source;
    private readonly Catalog _catalog;
    private readonly StateStore _store;
    private readonly LibraryReader _library;
    private readonly Builder _builder;
    private readonly Func<Guid, long> _stamp;
    private readonly CancellationToken _stop;
    private readonly ConcurrentDictionary<MediaKey, DateTime> _datesAsked = new();
    private readonly ConcurrentDictionary<Guid, (long Stamp, Dictionary<MediaKey, LibEntry?> Found)> _lookups = new();
    private readonly ConcurrentDictionary<int, string> _profileNames = new();
    private readonly ConcurrentDictionary<(Guid User, MediaKey Key), byte> _seenActive = new();

    /// <param name="stamp">Liefert je Konto einen Wert, der sich ändert, sobald sich an Bibliothek oder Gesehenem etwas getan hat.</param>
    public RequestTracker(RequestSource source, Catalog catalog, StateStore store, LibraryReader library, Builder builder, Func<Guid, long> stamp, CancellationToken stop)
    {
        _source = source;
        _catalog = catalog;
        _store = store;
        _library = library;
        _builder = builder;
        _stamp = stamp;
        _stop = stop;
    }

    /// <summary>Der Stand für ein Konto, mit frischer Liste von Seerr (höchstens zehn Sekunden alt), soweit das im Zeitrahmen gelingt.</summary>
    public async Task<RequestView> ViewAsync(User user, string lang, TimeSpan budget, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var info = await _source.GetAsync(RequestSource.Fresh, budget, ct).ConfigureAwait(false);
        var state = _store.View(user.Id);
        var own = RequestOwnership.For(user.Id, info, state, _store.ThemeRequests());
        if (own.Count == 0)
        {
            return new RequestView { Known = info.At > DateTime.MinValue };
        }

        // Gewartet wird nur auf Titel, über die noch gar nichts bekannt ist (ohne Namen und Plakat ließen sie sich nicht zeigen).
        var left = budget - (DateTime.UtcNow - started);
        var unknown = own.Keys.Where(k => _catalog.Get(lang, k) is null && _catalog.FromDetail(lang, k) is null).Select(k => (Key: k, Text: true)).ToList();
        if (unknown.Count > 0 && left > TimeSpan.FromMilliseconds(100))
        {
            await Within(_builder.DetailsAsync(unknown, lang, null, _stop, true), left).ConfigureAwait(false);
        }

        if (QuietHours.Read() is { Marker.Length: > 0 })
        {
            foreach (var request in own.Values.SelectMany(o => o.Requests).Where(r => r.Status == 2 && r.Downloads.Length == 0 && !_profileNames.ContainsKey(r.Id)).Take(20))
            {
                left = budget - (DateTime.UtcNow - started);
                if (left < TimeSpan.FromMilliseconds(100))
                {
                    break;
                }

                var name = await Within(_source.ProfileNameAsync(request, ct), left).ConfigureAwait(false);
                if (name is not null)
                {
                    _profileNames[request.Id] = name;
                }
            }
        }

        var view = Compute(user, lang, info, state, own, DateTime.UtcNow, DateTime.Now, true);
        RefreshDates(view, lang);
        return view;
    }

    /// <summary>
    /// Holt im Hintergrund die Termine nach (Heimkino, Staffelstart, nächste Folge), von denen der Text eines wartenden
    /// Titels abhängt: höchstens einen Tag alt. Gewartet wird darauf nie; bis sie da sind, steht der bisherige Text.
    /// </summary>
    private void RefreshDates(RequestView view, string lang)
    {
        var today = Scorer.Today(DateTime.Now);
        var now = DateTime.UtcNow;
        List<(MediaKey Key, bool Text)>? stale = null;
        foreach (var e in view.Items)
        {
            if (!e.Active)
            {
                continue;
            }

            // Täglich frisch nur dort, wo ein Termin den Text bestimmt; was schon lädt, braucht ihn nicht mehr.
            var waiting = e.Phase is RequestPhase.Searching or RequestPhase.Scheduled or RequestPhase.Unreleased or RequestPhase.Partial or RequestPhase.Approval;
            if (_catalog.NeedsDetail(e.Key, lang, today, true, waiting ? 86400 : 0)
                && (!_datesAsked.TryGetValue(e.Key, out var asked) || now - asked > RetryDates))
            {
                _datesAsked[e.Key] = now;
                (stale ??= new()).Add((e.Key, true));
            }
        }

        if (stale is not null)
        {
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        if (await _builder.DetailsAsync(stale, lang, null, _stop, true, 86400).ConfigureAwait(false) > 0)
                        {
                            _catalog.Save();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Der Server fährt herunter.
                    }
                },
                CancellationToken.None);
        }
    }

    /// <summary>Für die Diagnose: Die Termine der angefragten Titel eines Kontos gelten als veraltet und werden beim nächsten Abruf neu geholt.</summary>
    public int ForgetDates(User user)
    {
        var own = RequestOwnership.For(user.Id, _source.Known, _store.View(user.Id), _store.ThemeRequests());
        foreach (var key in own.Keys)
        {
            _catalog.Expire(key);
            _datesAsked.TryRemove(key, out _);
        }

        return own.Count;
    }

    /// <summary>Der Stand aus dem, was gerade bekannt ist: ohne Abruf bei Seerr, für Antworten, die nicht warten dürfen.</summary>
    public RequestView Peek(User user, string lang)
    {
        var info = _source.Known;
        var state = _store.View(user.Id);
        var own = RequestOwnership.For(user.Id, info, state, _store.ThemeRequests());
        return own.Count == 0
            ? new RequestView { Known = info.At > DateTime.MinValue }
            : Compute(user, lang, info, state, own, DateTime.UtcNow, DateTime.Now, false);
    }

    /// <summary>Wie <see cref="Peek"/>, aber zu einer gestellten Uhrzeit: zum Nachvollziehen, was wann angezeigt würde.</summary>
    public RequestView At(User user, string lang, DateTime nowLocal)
    {
        var info = _source.Known;
        var state = _store.View(user.Id);
        var own = RequestOwnership.For(user.Id, info, state, _store.ThemeRequests());
        return Compute(user, lang, info, state, own, TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nowLocal, DateTimeKind.Unspecified), TimeZoneInfo.Local), nowLocal, false);
    }

    public string? ProfileName(int requestId) => _profileNames.TryGetValue(requestId, out var name) ? name : null;

    public void Reset()
    {
        _lookups.Clear();
        _profileNames.Clear();
        _datesAsked.Clear();
    }

    private static async Task<T?> Within<T>(Task<T> task, TimeSpan limit)
    {
        // Wer wartet, wartet nicht ewig; die Arbeit selbst läuft zu Ende und nützt dem nächsten Abruf.
        var done = await Task.WhenAny(task, Task.Delay(limit)).ConfigureAwait(false);
        if (done == task)
        {
            try
            {
                return await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return default;
            }
        }

        _ = task.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        return default;
    }

    private RequestView Compute(User user, string lang, RequestsInfo info, StateView state, Dictionary<MediaKey, OwnRequest> own, DateTime now, DateTime nowLocal, bool persist)
    {
        var lib = Lookup(user, own.Keys);
        var quiet = QuietHours.Read();
        var arrivals = new List<(MediaKey Key, DateTime At, DateTime Requested)>();
        var items = new List<RequestEntry>(own.Count);
        foreach (var o in own)
        {
            lib.TryGetValue(o.Key, out var entry);
            var detail = _catalog.Detail(o.Key);
            var e = o.Key.Tv
                ? Series(user.Id, o.Key, o.Value, entry, detail, state, quiet, now, nowLocal, arrivals)
                : Movie(user.Id, o.Key, o.Value, entry, detail, state, quiet, now, nowLocal, arrivals);
            if (e is null)
            {
                continue;
            }

            e.Detail = detail;
            e.Title = _catalog.Get(lang, o.Key) ?? _catalog.FromDetail(lang, o.Key);
            if (e.Title is null && entry is not null)
            {
                // Zur Not so, wie die Bibliothek den Titel kennt.
                e.Title = new Title { Key = o.Key, Name = entry.Name, Date = entry.Year * 10000, Sure = true };
            }

            if (e.Title is null)
            {
                // Noch nichts über den Titel bekannt; beim nächsten Abruf liegen die Details vor.
                continue;
            }

            if (persist && e.Active)
            {
                _seenActive[(user.Id, o.Key)] = 1;
            }

            items.Add(e);
        }

        if (persist && arrivals.Count > 0)
        {
            _store.Update(user.Id, s =>
            {
                foreach (var a in arrivals)
                {
                    s.Arrived[a.Key.ToString()] = new ArrivedEntry { At = new DateTimeOffset(a.At).ToUnixTimeSeconds(), Requested = new DateTimeOffset(a.Requested).ToUnixTimeSeconds() };
                }

                return true;
            });
        }

        // Ladendes zuerst (das Weiteste vorn), dann Einordnen, Warteschlange, Teilweises, Suche, Geplantes, Freigabe;
        // danach Angekommenes (das Jüngste zuerst), zuletzt Abgelehntes.
        items.Sort((a, b) =>
        {
            var c = a.Phase.CompareTo(b.Phase);
            if (c != 0)
            {
                return c;
            }

            if (a.Phase == RequestPhase.Downloading && a.Percent != b.Percent)
            {
                return (b.Percent ?? -1).CompareTo(a.Percent ?? -1);
            }

            c = (b.AvailableAt ?? b.RequestedAt).CompareTo(a.AvailableAt ?? a.RequestedAt);
            return c != 0 ? c : a.Key.Id.CompareTo(b.Key.Id);
        });
        if (items.Count > MaxItems)
        {
            items.RemoveRange(MaxItems, items.Count - MaxItems);
        }

        return new RequestView { Items = items, Known = info.At > DateTime.MinValue };
    }

    /// <summary>Bibliothek und Gesehenes zu den angefragten Titeln; neu gelesen nur, wenn sich dort etwas getan hat.</summary>
    private Dictionary<MediaKey, LibEntry?> Lookup(User user, IReadOnlyCollection<MediaKey> keys)
    {
        var stamp = _stamp(user.Id);
        if (_lookups.TryGetValue(user.Id, out var cached) && cached.Stamp == stamp && keys.All(cached.Found.ContainsKey))
        {
            return cached.Found;
        }

        var found = new Dictionary<MediaKey, LibEntry?>();
        try
        {
            var hits = _library.Find(user, keys);
            foreach (var key in keys)
            {
                found[key] = hits.GetValueOrDefault(key);
            }

            _lookups[user.Id] = (stamp, found);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ohne Blick in die Bibliothek gilt vorerst, was Seerr sagt.
            foreach (var key in keys)
            {
                found.TryAdd(key, null);
            }
        }

        return found;
    }

    private RequestEntry? Movie(Guid user, MediaKey key, OwnRequest own, LibEntry? lib, Detail? detail, StateView state, QuietHours? quiet, DateTime now, DateTime nowLocal, List<(MediaKey, DateTime, DateTime)> arrivals)
    {
        var current = Current(own);
        var requested = current?.Created ?? own.ThemeAt ?? now;
        var owner = own.BySeerrUser ? "seerr" : "theme";
        if (lib is not null)
        {
            return Arrived(user, key, own, current, lib, lib.Added, lib.Untouched, requested, Array.Empty<int>(), state, now, arrivals, owner);
        }

        // Gibt es den Film für zu Hause überhaupt schon? Solange nicht, wäre „Wird gesucht“ über Wochen eine falsche Auskunft.
        var today = Scorer.Today(nowLocal);
        (WaitKind Kind, int Date) wait = default;
        DateTime? searchable = null;
        if (detail?.HomeRelease is { } home)
        {
            if (home > today)
            {
                wait = (WaitKind.HomeRelease, home);
            }
            else if (home == 0 && detail.Date > today)
            {
                wait = (WaitKind.CinemaSoon, detail.Date);
            }
            else if (home == 0 && detail.Date > 0 && DaysBetween(detail.Date, today) < CinemaDays)
            {
                wait = (WaitKind.InCinema, detail.Date);
            }
            else
            {
                // Zu finden ist der Film frühestens, seit es ihn für zu Hause gibt; erst ab da zählt, wie lange schon gesucht wird.
                var since = home > 0 ? StartOfDay(home, 0) : StartOfDay(detail.Date, CinemaDays);
                searchable = since > requested ? since : requested;
            }
        }

        return Underway(key, own, current, current?.Downloads ?? Array.Empty<DownloadEntry>(), null, requested, Array.Empty<int>(), quiet, now, nowLocal, owner, wait, searchable);
    }

    private RequestEntry? Series(Guid user, MediaKey key, OwnRequest own, LibEntry? lib, Detail? detail, StateView state, QuietHours? quiet, DateTime now, DateTime nowLocal, List<(MediaKey, DateTime, DateTime)> arrivals)
    {
        var current = Current(own);
        var requested = current?.Created ?? own.ThemeAt ?? now;
        var owner = own.BySeerrUser ? "seerr" : "theme";
        var today = Scorer.Today(nowLocal);

        // Welche Staffeln gemeint sind: die der offenen Anfragen, sonst die der jüngsten; ohne Angabe alle bekannten.
        var open = own.Requests.Where(r => r.Pending).ToList();
        var source = open.Count > 0 ? open : current is not null ? new List<SeerrRequest> { current } : new List<SeerrRequest>();
        var seasonStatus = new Dictionary<int, int>();
        foreach (var r in source)
        {
            foreach (var s in r.Seasons)
            {
                seasonStatus[s.Number] = Math.Max(seasonStatus.GetValueOrDefault(s.Number), s.Status);
            }
        }

        var numbers = seasonStatus.Keys.Where(n => n > 0).OrderBy(n => n).ToList();
        if (numbers.Count == 0 && seasonStatus.Count == 0)
        {
            var known = Math.Max(detail?.Seasons ?? 0, lib?.SeasonsHave?.Keys.DefaultIfEmpty(0).Max() ?? 0);
            numbers = Enumerable.Range(1, Math.Max(1, known)).ToList();
        }
        else if (numbers.Count == 0)
        {
            numbers.Add(0);
        }

        // Die zuletzt angelaufene Staffel kann noch unvollständig sein: Dort sind alle Folgen das Ziel, auch die kommenden.
        // Ältere Staffeln sind ganz gelaufen, spätere haben noch gar nicht begonnen.
        var lastAired = 0;
        if (detail?.Episodes is { } airedAll)
        {
            for (var n = airedAll.Length - 1; n >= 1; n--)
            {
                if (airedAll[n] > 0)
                {
                    lastAired = n;
                    break;
                }
            }
        }

        int have = 0, expected = 0, played = 0, airedSum = 0, missingSeasons = 0, missingSeason = 0, missingHave = 0, missingExpected = 0;
        var counted = true;
        var newest = DateTime.MinValue;
        foreach (var n in numbers)
        {
            var season = lib?.SeasonsHave is { } all && all.TryGetValue(n, out var h) ? h : default;
            var aired = detail?.Episodes is { } eps && n < eps.Length ? eps[n] : -1;
            var total = detail?.EpisodeCounts is { } counts && n < counts.Length ? counts[n] : -1;
            have += season.Count;
            played += season.Played;
            if (season.Newest > newest)
            {
                newest = season.Newest;
            }

            // Weichen die Zählweisen von TMDb und Bibliothek ab, gilt eine Staffel auch als vollständig, wenn Seerr sie so führt.
            var done = seasonStatus.GetValueOrDefault(n) == 5 && season.Count > 0;
            if (aired < 0)
            {
                // Ohne Folgenzahlen von TMDb bleibt nur, was Seerr über die Staffel sagt.
                counted = false;
                expected += season.Count;
                if (!done)
                {
                    missingSeasons++;
                    missingSeason = n;
                }

                continue;
            }

            airedSum += aired;
            var target = n == lastAired && total > aired ? total : aired;
            expected += Math.Max(target, season.Count);
            if (season.Count < target && !done)
            {
                missingSeasons++;
                missingSeason = n;
                missingHave = season.Count;
                missingExpected = target;
            }
        }

        var complete = have > 0 && missingSeasons == 0;
        var shown = numbers.ToArray();
        if (current is not null && current.Status is 1 or 3 or 4)
        {
            // Noch nicht freigegeben, abgelehnt oder gescheitert: Das gilt, auch wenn von früher schon Staffeln da sind.
            return Underway(key, own, current, Array.Empty<DownloadEntry>(), lib, requested, shown, quiet, now, nowLocal, owner, default, null);
        }

        var downloads = (current?.Downloads ?? Array.Empty<DownloadEntry>()).Where(d => d.Season < 0 || numbers.Contains(d.Season)).ToArray();
        if (downloads.Length == 0 && complete && lib is not null)
        {
            return Arrived(user, key, own, current, lib, newest, played == 0, requested, shown, state, now, arrivals, owner);
        }

        // Die nächste Folge, sofern sie zu den angefragten Staffeln gehört und ihr Tag feststeht.
        var next = detail is { NextDate: > 0 } && numbers.Contains(detail.NextSeason) && detail.NextDate >= today ? detail.NextDate : 0;
        if (downloads.Length == 0 && have > 0 && lib is not null)
        {
            // Fehlt nur an einer Staffel etwas, nennt der Text diese Staffel und ihre Folgen; sonst die Summe.
            var single = counted && missingSeasons == 1 && missingExpected > 0;
            return new RequestEntry
            {
                Key = key,
                Phase = RequestPhase.Partial,
                Percent = counted && expected > 0 ? Math.Clamp((int)Math.Round(100.0 * have / expected), 1, 99) : null,
                RequestedAt = requested,
                Seasons = shown,
                Have = single ? missingHave : have,
                Expected = single ? missingExpected : counted ? expected : 0,
                PartialSeason = single ? missingSeason : 0,
                AvailableFrom = next,
                Lib = lib,
                Owner = owner,
            };
        }

        // Noch keine Folge der angefragten Staffeln ist gelaufen: Die Staffel startet erst. Fehlt bei einer längst
        // gestarteten Serie jede Angabe zu gelaufenen Folgen, ist die Quelle lückenhaft; dann lieber nichts behaupten.
        (WaitKind Kind, int Date) wait = default;
        var plausible = lastAired > 0 || detail is { Date: 0 } || detail?.Date > today || next > 0;
        if (downloads.Length == 0 && counted && detail is not null && airedSum == 0 && have == 0 && plausible)
        {
            var start = next > 0 ? next : numbers.Contains(1) && detail.Date > today ? detail.Date : 0;
            wait = start > 0 ? (WaitKind.SeasonStart, start) : (WaitKind.SeasonUnknown, 0);
        }

        return Underway(key, own, current, downloads, lib, requested, shown, quiet, now, nowLocal, owner, wait, counted && detail is not null ? requested : null);
    }

    /// <summary>Beginn eines Tages (JJJJMMTT, Zeit des Servers) als UTC, um einige Tage verschoben; bei unlesbaren Angaben der früheste Zeitpunkt.</summary>
    private static DateTime StartOfDay(int date, int addDays)
    {
        try
        {
            var day = new DateOnly(date / 10000, date / 100 % 100, date % 100).AddDays(addDays);
            return TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), TimeZoneInfo.Local);
        }
        catch (ArgumentException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>Tage zwischen zwei Daten in der Schreibweise JJJJMMTT; bei unlesbaren Angaben sehr viele.</summary>
    internal static int DaysBetween(int from, int to)
    {
        try
        {
            return new DateOnly(to / 10000, to / 100 % 100, to % 100).DayNumber - new DateOnly(from / 10000, from / 100 % 100, from % 100).DayNumber;
        }
        catch (ArgumentOutOfRangeException)
        {
            return int.MaxValue;
        }
    }

    /// <summary>Die Anfrage, um die es gerade geht: die jüngste offene, sonst die jüngste überhaupt.</summary>
    private static SeerrRequest? Current(OwnRequest own)
    {
        SeerrRequest? best = null;
        foreach (var r in own.Requests)
        {
            if (best is null || (r.Pending && !best.Pending) || (r.Pending == best.Pending && r.Created > best.Created))
            {
                best = r;
            }
        }

        return best;
    }

    private RequestEntry? Arrived(Guid user, MediaKey key, OwnRequest own, SeerrRequest? current, LibEntry lib, DateTime added, bool unseen, DateTime requested, int[] seasons, StateView state, DateTime now, List<(MediaKey, DateTime, DateTime)> arrivals, string owner)
    {
        // Seit wann der Titel da ist, wird beim ersten Mal festgehalten; sonst begänne „neu“ mit jedem Neustart von vorn.
        DateTime at;
        if (state.Arrived.TryGetValue(key, out var noted) && Math.Abs(noted.Requested - new DateTimeOffset(requested).ToUnixTimeSeconds()) <= 600)
        {
            at = DateTimeOffset.FromUnixTimeSeconds(noted.At).UtcDateTime;
        }
        else
        {
            // Am liebsten das Datum aus Jellyfin; richtet sich das nach dem Alter der Datei, liegt es vor der Anfrage und
            // taugt nicht. Dann der Zeitpunkt, zu dem Seerr die Anfrage als erledigt führt.
            var addedUtc = added.Kind == DateTimeKind.Local ? added.ToUniversalTime() : added;
            if (addedUtc >= requested.AddHours(-1) && addedUtc <= now)
            {
                at = addedUtc;
            }
            else if (current is { Status: 5 } && current.Updated >= current.Created && current.Updated <= now)
            {
                at = current.Updated;
            }
            else if (current?.MediaAdded is { } seerr && seerr >= requested && seerr <= now)
            {
                at = seerr;
            }
            else if (_seenActive.ContainsKey((user, key)))
            {
                at = now;
            }
            else
            {
                at = requested;
            }

            arrivals.Add((key, at, requested));
        }

        // Gesehen oder länger als zwei Wochen da: nichts mehr, woran erinnert werden müsste.
        if (!unseen || now - at >= TimeSpan.FromDays(NewDays))
        {
            return null;
        }

        return new RequestEntry
        {
            Key = key,
            Phase = RequestPhase.Available,
            Percent = 100,
            RequestedAt = requested,
            AvailableAt = at,
            IsNew = true,
            Seasons = seasons,
            Lib = lib,
            Owner = owner,
        };
    }

    /// <param name="wait">Warum es den Titel noch nicht zu laden gibt, sofern das so ist.</param>
    /// <param name="searchable">Seit wann der Titel zu finden sein kann; <c>null</c>, solange die Termine dazu fehlen.</param>
    private RequestEntry? Underway(MediaKey key, OwnRequest own, SeerrRequest? current, DownloadEntry[] downloads, LibEntry? lib, DateTime requested, int[] seasons, QuietHours? quiet, DateTime now, DateTime nowLocal, string owner, (WaitKind Kind, int Date) wait, DateTime? searchable)
    {
        var e = new RequestEntry { Key = key, RequestedAt = requested, Seasons = seasons, Lib = lib, Owner = owner };
        if (current is null)
        {
            // Nur der Vermerk des Themes: gerade erst gestellt. Zeigt Seerr die Anfrage auch nach einer Weile nicht, gibt es sie dort nicht (mehr).
            if (own.ThemeAt is not { } noted || now - noted >= RequestOwnership.ThemeGrace)
            {
                return null;
            }

            if (wait.Kind != WaitKind.None)
            {
                return Unreleased(e, wait);
            }

            e.StartsAt = quiet?.EndIfWithin(nowLocal);
            e.Phase = e.StartsAt.HasValue ? RequestPhase.Scheduled : RequestPhase.Searching;
            return e;
        }

        switch (current.Status)
        {
            case 3:
                e.Phase = RequestPhase.Declined;
                return now - current.Updated < TimeSpan.FromDays(NewDays) ? e : null;
            case 1:
                e.Phase = RequestPhase.Approval;
                return e;
            case 4:
                // Seerr meldet „fehlgeschlagen“, wenn Radarr/Sonarr in der Ruhezeit schlafen. Wurde in dieser Ruhezeit
                // angefragt oder mit einem Profil, das ohnehin erst danach lädt, holt der Weckdienst das am Morgen nach.
                if (quiet is { } fq)
                {
                    var askedLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(requested, DateTimeKind.Utc), TimeZoneInfo.Local);
                    var deferred = fq.Marker.Length > 0 && _profileNames.TryGetValue(current.Id, out var fp) && fp.Contains(fq.Marker, StringComparison.OrdinalIgnoreCase);
                    var end = deferred ? fq.NextEnd(askedLocal) : fq.EndIfWithin(askedLocal);
                    if (end is { } startAt && nowLocal < startAt.AddMinutes(30))
                    {
                        e.Phase = RequestPhase.Scheduled;
                        e.StartsAt = startAt;
                        return e;
                    }
                }

                e.Phase = RequestPhase.Failed;
                return now - current.Updated < TimeSpan.FromDays(NewDays) ? e : null;
        }

        if (downloads.Length > 0)
        {
            double size = 0, left = 0;
            DateTime? eta = null;
            bool loading = false, allDone = true, allFailed = true;
            foreach (var d in downloads)
            {
                size += Math.Max(0, d.Size);
                left += Math.Clamp(d.SizeLeft, 0, Math.Max(0, d.Size));
                loading |= d.Loading;
                allDone &= d.Done;
                allFailed &= d.Failed;
                if (!d.Done && d.Eta is { } end && (eta is null || end > eta))
                {
                    eta = end;
                }
            }

            e.Files = downloads.Length;
            int? percent = size > 0 ? Math.Clamp((int)Math.Round(100.0 * (1 - (left / size))), 0, 100) : null;
            if (loading)
            {
                e.Phase = RequestPhase.Downloading;
                e.Percent = percent is { } p ? Math.Min(p, 99) : null;
                e.Eta = eta > now ? eta : null;
            }
            else if (allDone)
            {
                e.Phase = RequestPhase.Importing;
                e.Percent = 100;
            }
            else if (allFailed)
            {
                e.Phase = RequestPhase.Failed;
            }
            else
            {
                e.Phase = RequestPhase.Queued;
                e.Percent = percent;
            }

            return e;
        }

        if (current.Status == 5 || current.MediaStatus == 5)
        {
            // Laut Seerr da, in Jellyfin (für dieses Konto) noch nicht: wird gerade eingelesen. Bleibt das so, ist nichts mehr zu erwarten.
            if (now - current.Updated >= RequestOwnership.ImportGrace)
            {
                return null;
            }

            e.Phase = RequestPhase.Importing;
            e.Percent = 100;
            return e;
        }

        // Freigegeben, aber noch kein Download. Gibt es den Titel noch gar nicht (für zu Hause), sagt der Stand das und
        // nennt den Termin, statt wochenlang „Wird gesucht“ zu zeigen.
        if (wait.Kind != WaitKind.None)
        {
            return Unreleased(e, wait);
        }

        // Sonst wird gesucht, außer die Download-Dienste ruhen gerade oder das gewählte Profil lädt erst nach der
        // nächsten Ruhezeit.
        if (quiet is { } q)
        {
            if (q.Marker.Length > 0 && _profileNames.TryGetValue(current.Id, out var profile) && profile.Contains(q.Marker, StringComparison.OrdinalIgnoreCase))
            {
                var end = q.NextEnd(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(requested, DateTimeKind.Utc), TimeZoneInfo.Local));
                if (nowLocal < end)
                {
                    e.Phase = RequestPhase.Scheduled;
                    e.StartsAt = end;
                    return e;
                }
            }

            // Nur was in dieser Ruhezeit angefragt wurde, startet an ihrem Ende. Was schon vorher gesucht und nicht
            // gefunden wurde, sucht danach einfach weiter – „Startet morgen gegen …“ wäre da ein falsches Versprechen.
            if (q.EndIfWithin(nowLocal) is { } quietEnd
                && q.EndIfWithin(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(requested, DateTimeKind.Utc), TimeZoneInfo.Local)) == quietEnd)
            {
                e.Phase = RequestPhase.Scheduled;
                e.StartsAt = quietEnd;
                return e;
            }
        }

        // Nach zwei Tagen ohne Fund sagt der Text das auch. Solange die Termine fehlen, bleibt es beim schlichten
        // „Wird gesucht“: Vielleicht gibt es den Titel ja noch gar nicht.
        e.Phase = RequestPhase.Searching;
        e.LongSearch = searchable is { } since && now - since > LongSearchAfter;
        return e;
    }

    private static RequestEntry Unreleased(RequestEntry e, (WaitKind Kind, int Date) wait)
    {
        e.Phase = RequestPhase.Unreleased;
        e.Wait = wait.Kind;
        e.WaitDate = wait.Date;

        // Ein Datum nur, wenn es eines für zu Hause gibt; aus dem Kinostart wird keines geschätzt.
        e.AvailableFrom = wait.Kind is WaitKind.HomeRelease or WaitKind.SeasonStart ? wait.Date : 0;
        return e;
    }
}
