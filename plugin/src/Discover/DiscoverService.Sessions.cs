using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>Die Zwischenstände der Konten und die Arbeit im Hintergrund: berechnen, vertiefen, neu ordnen.</summary>
public sealed partial class DiscoverService
{
    // ---- Sitzungen ---------------------------------------------------------------------------------------------

    private Session Touch(Guid user, string lang)
    {
        CheckConfig();
        var session = _sessions.GetOrAdd((user, lang), key => new Session(key.User, key.Lang));
        session.LastUsed = DateTime.UtcNow;
        if (_sessions.Count > 12)
        {
            // Lange ungenutzte Stände freigeben; sie entstehen beim nächsten Abruf neu.
            foreach (var old in _sessions.Where(s => !s.Value.Building && DateTime.UtcNow - s.Value.LastUsed > TimeSpan.FromHours(36)).Select(s => s.Key).ToList())
            {
                _sessions.TryRemove(old, out _);
            }
        }

        return session;
    }

    private IEnumerable<Session> SessionsOf(Guid user) => _sessions.Where(s => s.Key.User == user).Select(s => s.Value).ToList();

    private Snapshot? AnySnapshot(Guid user, string lang)
    {
        if (_sessions.TryGetValue((user, lang), out var session) && session.Snap is { } snap)
        {
            return snap;
        }

        return SessionsOf(user).Select(s => s.Snap).FirstOrDefault(s => s is not null);
    }

    /// <summary>Wechselt die Seerr-Einrichtung, gilt nichts Bisheriges mehr.</summary>
    private void CheckConfig()
    {
        var c = Plugin.Instance?.Configuration;
        if (c is null)
        {
            return;
        }

        var stamp = c.SeerrUrl + "|" + c.SeerrUserId + "|" + StringComparer.Ordinal.GetHashCode(c.SeerrApiKey).ToString(CultureInfo.InvariantCulture);
        lock (_configLock)
        {
            if (stamp == _configStamp)
            {
                return;
            }

            var first = _configStamp.Length == 0;
            _configStamp = stamp;
            if (first)
            {
                return;
            }
        }

        _sessions.Clear();
        _catalog.Clear();
        _requests.Forget();
        _requests.ForgetProfiles();
        _tracker.Reset();
    }

    private T Waiting<T>(Session session, T dto, Action<T, string, string?, double> set)
    {
        var error = session.Error;
        if (error is not null)
        {
            // Nach einem Fehlschlag nicht bei jedem Abruf neu anrennen, aber auch nicht aufgeben.
            if (!session.Building && DateTime.UtcNow - session.LastAttempt >= RetryAfter)
            {
                EnsureBuild(session, "retry");
            }

            set(dto, "empty", error, 0);
            return dto;
        }

        EnsureBuild(session, "first");
        set(dto, "building", null, Math.Round(Math.Clamp(session.Progress, 0f, 0.99f), 2));
        return dto;
    }

    /// <summary>Alten Stand ausliefern, bei Bedarf im Hintergrund erneuern; am neuen Tag die Reihenfolge leicht verschieben.</summary>
    private Snapshot Refresh(Session session, Snapshot snap)
    {
        var now = DateTime.UtcNow;
        if (!session.Building && now - session.LastAttempt > RebuildGap)
        {
            if (now - session.BuiltAt > MaxAge)
            {
                EnsureBuild(session, "stale");
            }
            else if (ChangeStamp(session.User) > session.ChangeSeen)
            {
                EnsureBuild(session, "changed");
            }
        }

        if (snap.Day != Scorer.DayNumber(DateTime.Now))
        {
            return Rerank(session) ?? snap;
        }

        return snap;
    }

    private long ChangeStamp(Guid user) =>
        Math.Max(_userChanged.TryGetValue(user, out var ticks) ? ticks : 0, Interlocked.Read(ref _libraryChanged));

    /// <summary>Ordnet den vorhandenen Bestand mit dem aktuellen Zustand neu. Ohne Abrufe, wenige Millisekunden.</summary>
    private Snapshot? Rerank(Session session, bool publish = true)
    {
        lock (session.Gate)
        {
            if (session.Pool is null)
            {
                return null;
            }

            var known = _requests.Known;
            var snap = Scorer.Rank(new RankInput
            {
                User = session.User,
                Lang = session.Lang,
                Pool = session.Pool,
                Lib = session.Lib,
                Requests = known.At > session.Requests.At ? known : session.Requests,
                Theme = _store.ThemeRequests(),
                State = _store.View(session.User),
                Catalog = _catalog,
                BuiltAt = session.BuiltAt,
                Info = session.Info,
            });
            if (publish)
            {
                session.Snap = snap;
            }

            return snap;
        }
    }

    private void EnsureBuild(Session session, string reason)
    {
        lock (session.Gate)
        {
            if (session.Building || _disposed)
            {
                return;
            }

            session.Building = true;
            session.Progress = 0f;
            session.LastAttempt = DateTime.UtcNow;
        }

        Interlocked.Increment(ref _builds);
        _ = Task.Run(() => BuildAsync(session, reason));
    }

    private async Task BuildAsync(Session session, string reason)
    {
        var again = false;
        try
        {
            await session.Work.WaitAsync(_stop.Token).ConfigureAwait(false);
            try
            {
                session.Pending = false;
                var user = _users.GetUserById(session.User);
                if (user is null)
                {
                    session.Error = "none";
                    return;
                }

                var stamp = ChangeStamp(session.User);
                var requestsBefore = _seerr.Requests;
                // Hat sich seit dem letzten Mal nur scheinbar etwas geändert, bleibt der Stand, wie er ist.
                ulong? same = reason == "changed" && session.Snap is not null ? session.Lib.Fingerprint : null;
                var result = await _builder.RunAsync(user, session.Lang, session.Pool, reason, same, p => session.Progress = p, _stop.Token).ConfigureAwait(false);
                if (result.Unchanged)
                {
                    session.ChangeSeen = stamp;
                    return;
                }

                if (result.Error is not null)
                {
                    if (!Enabled)
                    {
                        // Während der Berechnung abgeschaltet: kein Fehler von Seerr, es wurde nur nicht mehr gefragt.
                        return;
                    }

                    Warn();
                    lock (session.Gate)
                    {
                        // Ein kurzer Ausfall von Seerr soll eine vorhandene Seite nicht leeren; ein alter Stand aber auch nicht ewig gelten.
                        session.Error = result.Error;
                        if (session.Snap is not null && DateTime.UtcNow - session.BuiltAt > TimeSpan.FromHours(48))
                        {
                            session.Snap = null;
                            session.Pool = null;
                        }
                    }

                    return;
                }

                lock (session.Gate)
                {
                    session.Pool = result.Pool;
                    session.Lib = result.Lib;
                    session.Requests = result.Requests;
                    session.Info = result.Info;
                    session.BuiltAt = DateTime.UtcNow;
                    session.ChangeSeen = stamp;
                    session.Error = null;
                }

                if (_catalog.LanguageDiffers(session.Lang) && Rerank(session, false) is { } preview)
                {
                    // Seerr liefert die gefilterten Listen in einer anderen Sprache: Namen und Texte der Startansicht nachholen,
                    // bevor der Stand sichtbar wird; sonst stünden auf der ersten Seite zwei Sprachen durcheinander.
                    var keys = Unsure(Pages.Page(preview, "all", 0), preview);
                    if (keys.Count > 0)
                    {
                        var lap = System.Diagnostics.Stopwatch.StartNew();
                        lock (session.Gate)
                        {
                            session.FixTried.UnionWith(keys);
                        }

                        result.Info.DetailsFetched += await _builder.FixLanguageAsync(keys, session.Lang, _stop.Token).ConfigureAwait(false);
                        result.Info.Timings["language"] = lap.ElapsedMilliseconds;
                        result.Info.Timings["total"] = result.Info.Timings.GetValueOrDefault("total") + lap.ElapsedMilliseconds;
                        _catalog.Save();
                    }
                }

                var watch = System.Diagnostics.Stopwatch.StartNew();
                Rerank(session);
                result.Info.Timings["rank"] = watch.ElapsedMilliseconds;
                result.Info.SeerrRequests = _seerr.Requests - requestsBefore;

                _catalog.Trim(session.Lang, () => _sessions.Values.Where(s => s.Lang == session.Lang).Select(s => s.Pool).Where(p => p is not null).SelectMany(p => p!.Items.Keys.ToList()));
                _log.LogDebug(
                    "Cinema Glass Entdecken: Stand berechnet ({Reason}), {Candidates} Kandidaten, {Requests} Abrufe bei Seerr, {Ms} ms",
                    reason,
                    result.Info.PoolSize,
                    result.Info.SeerrRequests,
                    result.Info.Timings.GetValueOrDefault("total"));
            }
            finally
            {
                session.Work.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren: die Berechnung endet hier.
        }
        catch (Exception ex)
        {
            session.Error ??= "none";
            _log.LogError("Cinema Glass Entdecken: Berechnung fehlgeschlagen ({Type}: {Message})", ex.GetType().Name, ex.Message);
        }
        finally
        {
            lock (session.Gate)
            {
                session.Building = false;
                again = session.Pending && !_disposed;
            }
        }

        if (again)
        {
            EnsureBuild(session, "feedback");
        }
    }

    private void Deepen(Session session, int genre)
    {
        session.SetDeep(genre, 1);
        _ = Task.Run(async () =>
        {
            var done = false;
            try
            {
                await session.Work.WaitAsync(_stop.Token).ConfigureAwait(false);
                try
                {
                    var pool = session.Pool;
                    if (pool is null)
                    {
                        return;
                    }

                    var deeper = await _builder.DeepenAsync(pool, genre, session.Lang, _stop.Token).ConfigureAwait(false);
                    lock (session.Gate)
                    {
                        // Kam inzwischen eine volle Berechnung dazwischen, gilt deren Bestand; der nächste Abruf holt das Genre dann nach.
                        if (ReferenceEquals(session.Pool, pool))
                        {
                            session.Pool = deeper;
                            done = true;
                        }
                    }

                    if (done)
                    {
                        Rerank(session);
                    }
                }
                finally
                {
                    session.Work.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Herunterfahren.
            }
            catch (Exception ex)
            {
                _log.LogError("Cinema Glass Entdecken: Genre-Listen fehlgeschlagen ({Type}: {Message})", ex.GetType().Name, ex.Message);
            }
            finally
            {
                session.SetDeep(genre, done ? 2 : 0);
            }
        });
    }

    private void FixLanguage(Session session, Snapshot snap, PageDto page)
    {
        var keys = Unsure(page, snap);
        lock (session.Gate)
        {
            keys.RemoveAll(session.FixTried.Contains);
            if (keys.Count == 0 || session.Fixing)
            {
                return;
            }

            session.Fixing = true;
            session.FixTried.UnionWith(keys);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await session.Work.WaitAsync(_stop.Token).ConfigureAwait(false);
                try
                {
                    await _builder.FixLanguageAsync(keys, session.Lang, _stop.Token).ConfigureAwait(false);
                    _catalog.Save();
                    Rerank(session);
                }
                finally
                {
                    session.Work.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Herunterfahren.
            }
            catch (Exception ex)
            {
                _log.LogError("Cinema Glass Entdecken: Texte nachholen fehlgeschlagen ({Type}: {Message})", ex.GetType().Name, ex.Message);
            }
            finally
            {
                session.Fixing = false;
            }
        });
    }

    /// <summary>Titel einer Seite, deren Name nicht sicher in der gewünschten Sprache vorliegt.</summary>
    private static List<MediaKey> Unsure(PageDto page, Snapshot snap)
    {
        var keys = new List<MediaKey>();
        var seen = new HashSet<MediaKey>();
        foreach (var item in page.Hero.Concat(page.Rows.SelectMany(r => r.Items)))
        {
            if (MediaKey.TryParse(item.Key, out var key) && seen.Add(key) && snap.ByKey.TryGetValue(key, out var s) && !s.Title.Sure)
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private HashSet<MediaKey> LibraryKeys()
    {
        var stamp = Interlocked.Read(ref _libraryChanged);
        var keys = _libraryKeys;
        if (keys is null || _libraryKeysAt != stamp)
        {
            keys = _library.Keys();
            _libraryKeys = keys;
            _libraryKeysAt = stamp;
        }

        return keys;
    }

    private void Warn()
    {
        // Höchstens alle zehn Minuten eine Zeile, und nie die Adresse oder den Schlüssel.
        if (DateTime.UtcNow - _lastWarning > TimeSpan.FromMinutes(10))
        {
            _lastWarning = DateTime.UtcNow;
            _log.LogWarning("Cinema Glass Entdecken: Seerr ist nicht erreichbar oder lehnt den Schlüssel ab ({Reason}); vorerst gibt es keine Empfehlungen", _seerr.LastError ?? "unbekannt");
        }
    }

    private sealed class Session
    {
        private readonly Dictionary<int, int> _deep = new();

        public Session(Guid user, string lang)
        {
            User = user;
            Lang = lang;
        }

        public Guid User { get; }

        public string Lang { get; }

        public object Gate { get; } = new();

        /// <summary>Hintergrundarbeiten eines Kontos laufen nacheinander.</summary>
        public SemaphoreSlim Work { get; } = new(1, 1);

        public volatile Snapshot? Snap;

        public Pool? Pool { get; set; }

        public LibrarySnapshot Lib { get; set; } = LibrarySnapshot.Empty;

        public RequestsInfo Requests { get; set; } = RequestsInfo.Empty;

        public BuildInfo Info { get; set; } = new();

        public DateTime BuiltAt { get; set; }

        public DateTime LastAttempt { get; set; }

        public DateTime LastUsed { get; set; }

        public long ChangeSeen { get; set; }

        public volatile bool Building;

        public volatile bool Pending;

        public volatile bool Fixing;

        public volatile float Progress;

        public volatile string? Error;

        public HashSet<MediaKey> FixTried { get; } = new();

        /// <summary>0 = nie versucht, 1 = läuft, 2 = erledigt.</summary>
        public int DeepState(int genre)
        {
            lock (_deep)
            {
                return _deep.GetValueOrDefault(genre);
            }
        }

        public void SetDeep(int genre, int state)
        {
            lock (_deep)
            {
                _deep[genre] = state;
            }
        }
    }
}
