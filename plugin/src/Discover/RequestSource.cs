using System.Globalization;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>
/// Die Liste der Anfragen bei Seerr, für alle Konten gemeinsam im Arbeitsspeicher. Wer den Stand angefragter Titel
/// zeigt, fragt oft; Seerr wird trotzdem höchstens alle zehn Sekunden gefragt, und nie von zwei Abrufen zugleich.
/// </summary>
internal sealed class RequestSource
{
    /// <summary>So lange gilt eine Antwort als frisch genug für den Stand angefragter Titel.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ProfilesFor = TimeSpan.FromMinutes(10);

    private readonly SeerrClient _seerr;
    private readonly CancellationToken _stop;
    private readonly object _lock = new();
    private readonly Dictionary<string, (DateTime At, Dictionary<int, string> Names, int Default)> _profiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DateTime At, int Id)> _defaults = new(StringComparer.Ordinal);
    private RequestsInfo _known = RequestsInfo.Empty;
    private Task<RequestsInfo>? _running;
    private DateTime _lastAttempt = DateTime.MinValue;
    private long _version;
    private long _fetches;

    public RequestSource(SeerrClient seerr, CancellationToken stop)
    {
        _seerr = seerr;
        _stop = stop;
    }

    /// <summary>Der letzte bekannte Stand, wie alt er auch ist.</summary>
    public RequestsInfo Known
    {
        get
        {
            lock (_lock)
            {
                return _known;
            }
        }
    }

    /// <summary>Wie oft die Liste seit dem Start bei Seerr geholt wurde.</summary>
    public long Fetches => Interlocked.Read(ref _fetches);

    /// <summary>
    /// Liefert die Liste, höchstens <paramref name="maxAge"/> alt, sofern Seerr mitspielt. Läuft schon ein Abruf, wird
    /// auf ihn gewartet; nach einem Abruf (auch einem gescheiterten) vergehen mindestens zehn Sekunden bis zum nächsten.
    /// </summary>
    /// <param name="budget">So lange wird höchstens gewartet; dauert es länger, kommt der alte Stand und der Abruf läuft im Hintergrund zu Ende.</param>
    public async Task<RequestsInfo> GetAsync(TimeSpan maxAge, TimeSpan budget, CancellationToken ct)
    {
        Task<RequestsInfo> task;
        RequestsInfo known;
        lock (_lock)
        {
            known = _known;
            var now = DateTime.UtcNow;
            if (now - known.At < maxAge)
            {
                return known;
            }

            if (_running is { IsCompleted: false })
            {
                task = _running;
            }
            else if (now - _lastAttempt < Fresh)
            {
                return known;
            }
            else
            {
                _lastAttempt = now;
                task = _running = FetchAsync(_version);
            }
        }

        if (budget > TimeSpan.Zero)
        {
            try
            {
                var done = await Task.WhenAny(task, Task.Delay(budget, ct)).ConfigureAwait(false);
                if (done == task)
                {
                    return await task.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Der Aufrufer wartet nicht mehr; der Abruf selbst läuft für die anderen weiter.
            }
        }

        return Known;
    }

    /// <summary>Verwirft den Stand: Nach einer neuen Anfrage soll der nächste Abruf sie enthalten.</summary>
    public void Forget()
    {
        lock (_lock)
        {
            _version++;
            _known = RequestsInfo.Empty;
            _lastAttempt = DateTime.MinValue;
            _running = null;
        }
    }

    /// <summary>
    /// Name des Qualitätsprofils einer Anfrage (für das Kennzeichen „lädt erst nach der Ruhezeit“). Nennt die Anfrage
    /// keinen Dienst oder kein Profil, gilt die Voreinstellung des Dienstes. <c>null</c>, wenn es sich nicht klären lässt.
    /// </summary>
    public async Task<string?> ProfileNameAsync(SeerrRequest request, CancellationToken ct)
    {
        var kind = request.Key.Tv ? "sonarr" : "radarr";
        var server = request.ServerId;
        if (server < 0)
        {
            var defaultKey = kind + (request.Is4k ? "#4k" : "#");
            lock (_lock)
            {
                if (_defaults.TryGetValue(defaultKey, out var known) && DateTime.UtcNow - known.At < ProfilesFor)
                {
                    server = known.Id;
                }
            }

            if (server < 0)
            {
                using var doc = await _seerr.GetAsync("service/" + kind, SeerrClient.QuickTimeout, ct).ConfigureAwait(false);
                if (doc is null)
                {
                    return null;
                }

                var match = SeerrParse.Servers(doc.RootElement).Where(s => s.Is4k == request.Is4k).OrderByDescending(s => s.Default).ToList();
                if (match.Count == 0)
                {
                    return null;
                }

                server = match[0].Id;
                lock (_lock)
                {
                    _defaults[defaultKey] = (DateTime.UtcNow, server);
                }
            }
        }

        var cacheKey = kind + "/" + server.ToString(CultureInfo.InvariantCulture);
        (DateTime At, Dictionary<int, string> Names, int Default) entry;
        bool hit;
        lock (_lock)
        {
            hit = _profiles.TryGetValue(cacheKey, out entry) && DateTime.UtcNow - entry.At < ProfilesFor;
        }

        if (!hit)
        {
            using var doc = await _seerr.GetAsync("service/" + cacheKey, SeerrClient.QuickTimeout, ct).ConfigureAwait(false);
            if (doc is null)
            {
                return null;
            }

            var active = doc.RootElement.TryGetProperty("server", out var s) ? SeerrParse.Int(s, "activeProfileId") : 0;
            entry = (DateTime.UtcNow, SeerrParse.Profiles(doc.RootElement), active);
            lock (_lock)
            {
                _profiles[cacheKey] = entry;
            }
        }

        return entry.Names.TryGetValue(request.ProfileId >= 0 ? request.ProfileId : entry.Default, out var name) ? name : null;
    }

    /// <summary>Verwirft die Profilnamen, etwa wenn ein anderes Seerr eingerichtet wurde.</summary>
    public void ForgetProfiles()
    {
        lock (_lock)
        {
            _profiles.Clear();
            _defaults.Clear();
        }
    }

    private async Task<RequestsInfo> FetchAsync(long version)
    {
        // Die hundert zuletzt geänderten Anfragen in einem einzigen Abruf: Was noch unterwegs oder gerade angekommen ist,
        // steht darin vorn.
        Interlocked.Increment(ref _fetches);
        RequestsInfo result;
        try
        {
            using var doc = await _seerr.GetAsync("request?take=100&skip=0&sort=modified&filter=all", SeerrClient.QuickTimeout, _stop).ConfigureAwait(false);
            if (doc is null)
            {
                // Lieber die alte Liste als gar keine.
                return Known;
            }

            var now = DateTime.UtcNow;
            result = new RequestsInfo { At = now, Items = SeerrParse.Requests(doc.RootElement, now) };
        }
        catch (OperationCanceledException)
        {
            return Known;
        }

        lock (_lock)
        {
            if (version == _version)
            {
                _known = result;
            }
        }

        return result;
    }
}
