using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>Das Letterboxd-Konto, das ein Jellyfin-Konto verknüpft hat, und seine eigene Freundesliste.</summary>
internal sealed class LetterboxdLink
{
    /// <summary>Letterboxd-Name, klein geschrieben.</summary>
    [JsonPropertyName("u")]
    public string User { get; set; } = string.Empty;

    /// <summary>Zusätzlich eingetragene Freunde (Letterboxd-Namen).</summary>
    [JsonPropertyName("f")]
    public List<string> Friends { get; set; } = new();

    /// <summary>Nur die eingetragenen Freunde, nicht wem das Konto auf Letterboxd folgt.</summary>
    [JsonPropertyName("o")]
    public bool Only { get; set; }

    public LetterboxdLink Copy() => new() { User = User, Friends = Friends.ToList(), Only = Only };
}

/// <summary>Ein Titel im Tagebuch eines Letterboxd-Kontos: Bewertung (0,5–5, 0 = keine), Herz und wann gesehen.</summary>
internal sealed class LbEntry
{
    [JsonPropertyName("r")]
    public float Rating { get; set; }

    [JsonPropertyName("l")]
    public bool Like { get; set; }

    /// <summary>Gesehen am (Unix-Sekunden, Mitternacht UTC).</summary>
    [JsonPropertyName("d")]
    public long Watched { get; set; }

    [JsonIgnore]
    public DateTime? When => Watched > 0 ? DateTimeOffset.FromUnixTimeSeconds(Watched).UtcDateTime : null;
}

/// <summary>Was über ein Letterboxd-Konto zwischengespeichert ist: Tagebuch, Anzeigename, wem es folgt.</summary>
internal sealed class LbMember
{
    [JsonPropertyName("n")]
    public string? Name { get; set; }

    /// <summary>Letzter Abruf des Tagebuchs (Unix-Sekunden), auch wenn er fehlschlug.</summary>
    [JsonPropertyName("at")]
    public long At { get; set; }

    /// <summary>Letzter erfolgreicher Abruf.</summary>
    [JsonPropertyName("ok")]
    public long Ok { get; set; }

    /// <summary><c>notfound</c>, <c>cloudflare</c>, <c>network</c> oder <c>null</c>.</summary>
    [JsonPropertyName("e")]
    public string? Error { get; set; }

    /// <summary>Titel (<c>movie:603</c>) → Eintrag; wächst mit jedem Abruf, das Älteste fällt zuerst heraus.</summary>
    [JsonPropertyName("x")]
    public Dictionary<string, LbEntry> Entries { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Wem das Konto folgt (Name → Anzeigename); nur für verknüpfte Konten erhoben.</summary>
    [JsonPropertyName("fo")]
    public Dictionary<string, string>? Following { get; set; }

    [JsonPropertyName("foAt")]
    public long FollowingAt { get; set; }

    /// <summary>Die Liste ist unvollständig: Letterboxd zeigt ohne Browser nur die erste Seite.</summary>
    [JsonPropertyName("foPart")]
    public bool FollowingPartial { get; set; }

    [JsonPropertyName("foE")]
    public string? FollowingError { get; set; }
}

/// <summary>Stand eines Abgleichs für ein Jellyfin-Konto.</summary>
internal sealed class LbSyncState
{
    public bool Running { get; set; }

    public long Started { get; set; }

    public long Finished { get; set; }

    public string? Error { get; set; }

    public int Done { get; set; }

    public int Total { get; set; }
}

/// <summary>Eine Stimme eines Freundes zu einem Titel.</summary>
internal readonly record struct FriendVote(string User, string Name, LbEntry Entry);

/// <summary>
/// Letterboxd ohne Schnittstelle: Gelesen werden nur öffentliche Seiten, das Tagebuch jedes Kontos als RSS
/// (<c>/name/rss/</c>, mit TMDb-Nummern) und die Liste, wem ein Konto folgt (<c>/name/following/</c>, HTML). Alles
/// läuft nacheinander, höchstens ein Abruf je Sekunde, je Konto höchstens alle sechs Stunden, mit einer gewöhnlichen
/// Browser-Kennung ohne persönliche Angaben. Fällt Letterboxd aus oder sperrt Cloudflare, bleibt der letzte Stand.
/// </summary>
internal sealed partial class Letterboxd
{
    public const string BaseUrl = "https://letterboxd.com/";

    /// <summary>So lange gilt ein Abruf je Konto.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    /// <summary>„Jetzt abgleichen“ holt das eigene Tagebuch und die Liste der Gefolgten frühestens nach dieser Zeit neu.</summary>
    public static readonly TimeSpan ForceGap = TimeSpan.FromMinutes(10);

    /// <summary>Nach einer Sperre durch Cloudflare so lange gar nicht mehr fragen.</summary>
    public static readonly TimeSpan BlockedPause = TimeSpan.FromHours(1);

    public const int MaxFriends = 80;
    public const int MaxManual = 50;
    private const int MaxEntries = 1500;
    private const int MaxFollowingPages = 4;
    private const int MaxBody = 3 * 1024 * 1024;
    private static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(1100);

    // Eine gewöhnliche Browser-Kennung, ohne Hinweis auf Server oder Person.
    private const string Agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    private static readonly XNamespace NsLb = "https://letterboxd.com";
    private static readonly XNamespace NsTmdb = "https://themoviedb.org";

    private static readonly JsonSerializerOptions FileJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IHttpClientFactory _http;
    private readonly Func<string> _folder;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<Guid, LbSyncState> _sync = new();
    private Dictionary<string, LbMember>? _members;
    private DateTime _lastRequest;
    private DateTime _blockedUntil;

    public Letterboxd(IHttpClientFactory http, Func<string> folder, ILogger log)
    {
        _http = http;
        _folder = folder;
        _log = log;
    }

    /// <summary>Schalter in den Einstellungen; „Entdecken“ muss ebenfalls an sein.</summary>
    public static bool On
    {
        get
        {
            var c = Plugin.Instance?.Configuration;
            return c is not null && c.Letterboxd && c.Discover;
        }
    }

    [GeneratedRegex(@"^[a-z0-9_]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    // Ein Eintrag der Liste „Gefolgt“: <a href="/name/" class="name"> Anzeigename </a>
    [GeneratedRegex(@"<a\s+href=""/([A-Za-z0-9_]{1,40})/""\s+class=""name""\s*>\s*([^<]{0,80}?)\s*</a>", RegexOptions.CultureInvariant)]
    private static partial Regex FollowingPattern();

    /// <summary>Macht aus einer Eingabe (Name, @Name oder Profil-Adresse) einen Letterboxd-Namen; <c>null</c> = ungültig.</summary>
    public static string? CleanName(string? text)
    {
        var s = text?.Trim() ?? string.Empty;
        var at = s.IndexOf("letterboxd.com/", StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
        {
            s = s[(at + "letterboxd.com/".Length)..];
            var slash = s.IndexOf('/', StringComparison.Ordinal);
            if (slash >= 0)
            {
                s = s[..slash];
            }
        }

        s = s.TrimStart('@').Trim('/').ToLowerInvariant();
        return NamePattern().IsMatch(s) ? s : null;
    }

    /// <summary>Welches Signal ein eigener Eintrag ergibt: ab 4 Sternen oder mit Herz fast wie „Gefällt mir“, bis 2 Sterne fast wie „Nicht meins“, sonst gesehen.</summary>
    public static (SignalKind Kind, float Weight) SignalOf(LbEntry e, Tuning t)
    {
        if (e.Like || e.Rating >= 4f)
        {
            return (SignalKind.Like, t.Like * 0.7f);
        }

        if (e.Rating > 0f && e.Rating <= 2f)
        {
            return (SignalKind.Dislike, t.Dislike * 0.6f);
        }

        return (SignalKind.Seen, t.Seen);
    }

    // ---- Lesen aus dem Zwischenspeicher -----------------------------------------------------------------------

    /// <summary>Die eigenen Einträge eines verknüpften Kontos; leer ohne Schalter oder Verknüpfung.</summary>
    public IReadOnlyDictionary<MediaKey, LbEntry> Own(LetterboxdLink? link)
    {
        var result = new Dictionary<MediaKey, LbEntry>();
        if (!On || link is null || link.User.Length == 0)
        {
            return result;
        }

        lock (_lock)
        {
            if (Members().TryGetValue(link.User, out var m))
            {
                foreach (var e in m.Entries)
                {
                    if (MediaKey.TryParse(e.Key, out var key))
                    {
                        result[key] = e.Value;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Die Freunde eines verknüpften Kontos: eingetragene zuerst, dann wem es folgt (Name → Anzeigename).</summary>
    public List<(string User, string Name, bool Manual)> Friends(LetterboxdLink link)
    {
        var list = new List<(string, string, bool)>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { link.User };
        lock (_lock)
        {
            var members = Members();
            foreach (var f in link.Friends)
            {
                if (seen.Add(f))
                {
                    list.Add((f, members.TryGetValue(f, out var m) && !string.IsNullOrEmpty(m.Name) ? m.Name! : f, true));
                }
            }

            if (!link.Only && members.TryGetValue(link.User, out var me) && me.Following is not null)
            {
                foreach (var f in me.Following)
                {
                    if (list.Count >= MaxFriends)
                    {
                        break;
                    }

                    if (seen.Add(f.Key))
                    {
                        list.Add((f.Key, f.Value.Length > 0 ? f.Value : f.Key, false));
                    }
                }
            }
        }

        return list;
    }

    /// <summary>Je Titel die Stimmen der Freunde (alle Einträge, auch schwache Bewertungen).</summary>
    public Dictionary<MediaKey, List<FriendVote>> Votes(LetterboxdLink? link)
    {
        var map = new Dictionary<MediaKey, List<FriendVote>>();
        if (!On || link is null || link.User.Length == 0)
        {
            return map;
        }

        var friends = Friends(link);
        lock (_lock)
        {
            var members = Members();
            foreach (var (user, name, _) in friends)
            {
                if (!members.TryGetValue(user, out var m))
                {
                    continue;
                }

                foreach (var e in m.Entries)
                {
                    if (!MediaKey.TryParse(e.Key, out var key))
                    {
                        continue;
                    }

                    if (!map.TryGetValue(key, out var votes))
                    {
                        map[key] = votes = new List<FriendVote>(2);
                    }

                    votes.Add(new FriendVote(user, name, e.Value));
                }
            }
        }

        return map;
    }

    /// <summary>Titel, die mindestens ein Freund mit 3,5 Sternen oder mehr bewertet oder mit Herz markiert hat.</summary>
    public IReadOnlyCollection<MediaKey> GoodKeys(LetterboxdLink? link)
    {
        var keys = new List<(MediaKey Key, long At)>();
        foreach (var v in Votes(link))
        {
            var good = v.Value.Where(x => Good(x.Entry)).ToList();
            if (good.Count > 0)
            {
                keys.Add((v.Key, good.Max(x => x.Entry.Watched)));
            }
        }

        // Die jüngsten zuerst: Werden es sehr viele, sollen die frischen Tipps Details bekommen.
        return keys.OrderByDescending(k => k.At).Select(k => k.Key).Take(600).ToList();
    }

    public static bool Good(LbEntry e) => e.Like || e.Rating >= 3.5f;

    /// <summary>Stand für „Dein Geschmack“.</summary>
    public LetterboxdDto Status(Guid user, LetterboxdLink? link)
    {
        var dto = new LetterboxdDto { Enabled = On };
        if (link is null || link.User.Length == 0)
        {
            return dto;
        }

        dto.User = link.User;
        dto.Friends = link.Friends.ToList();
        dto.Only = link.Only;
        var friends = Friends(link);
        lock (_lock)
        {
            var members = Members();
            if (members.TryGetValue(link.User, out var me))
            {
                dto.Name = me.Name;
                dto.Ratings = me.Entries.Count;
                dto.UserError = me.Error;
                dto.LastSync = me.Ok > 0 ? Iso(me.Ok) : null;
                dto.Following = me.Following?.Count ?? 0;
                dto.FollowingPartial = me.FollowingPartial;
                dto.FollowingError = me.FollowingError;
            }

            foreach (var (name, display, manual) in friends)
            {
                members.TryGetValue(name, out var m);
                dto.FriendList.Add(new LetterboxdFriendDto
                {
                    User = name,
                    Name = display,
                    Manual = manual,
                    Ratings = m?.Entries.Count ?? 0,
                    Good = m?.Entries.Values.Count(Good) ?? 0,
                    Error = m?.Error,
                    Synced = m is { Ok: > 0 },
                });
            }
        }

        dto.FriendRatings = dto.FriendList.Sum(f => f.Ratings);
        if (_sync.TryGetValue(user, out var s))
        {
            dto.Running = s.Running;
            dto.Done = s.Done;
            dto.Total = s.Total;
            dto.Error = s.Error;
            if (s.Finished > 0 && dto.LastSync is null && s.Error is null)
            {
                dto.LastSync = Iso(s.Finished);
            }
        }

        if (DateTime.UtcNow < _blockedUntil)
        {
            dto.Error ??= "cloudflare";
        }

        return dto;
    }

    /// <summary>Ist ein Abgleich fällig (etwas nie oder vor über sechs Stunden geholt)?</summary>
    public bool Due(Guid user, LetterboxdLink? link)
    {
        if (!On || link is null || link.User.Length == 0 || DateTime.UtcNow < _blockedUntil)
        {
            return false;
        }

        if (_sync.TryGetValue(user, out var s) && (s.Running || Catalog.Now - s.Started < 900))
        {
            // Läuft schon oder lief gerade erst (auch fehlgeschlagen): nicht bei jedem Seitenaufruf neu anstoßen.
            return false;
        }

        var limit = Catalog.Now - (long)MaxAge.TotalSeconds;
        lock (_lock)
        {
            var members = Members();
            if (!members.TryGetValue(link.User, out var me) || me.At < limit || (!link.Only && me.FollowingAt < limit))
            {
                return true;
            }
        }

        return Friends(link).Any(f => Stale(f.User, limit));
    }

    // ---- Abgleich ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Holt, was veraltet ist: eigenes Tagebuch, wem das Konto folgt, die Tagebücher der Freunde. Läuft im Hintergrund
    /// und nacheinander mit allen anderen Abgleichen; <paramref name="changed"/> wird gerufen, wenn neue Einträge kamen.
    /// </summary>
    public bool Start(Guid user, LetterboxdLink link, bool force, Action<Guid> changed, CancellationToken stop)
    {
        var state = _sync.GetOrAdd(user, _ => new LbSyncState());
        lock (state)
        {
            if (state.Running)
            {
                return false;
            }

            state.Running = true;
            state.Started = Catalog.Now;
            state.Error = null;
            state.Done = 0;
            state.Total = 0;
        }

        _ = Task.Run(async () =>
        {
            var any = false;
            try
            {
                any = await SyncAsync(link.Copy(), force, state, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Herunterfahren.
            }
            catch (Exception ex)
            {
                state.Error = "none";
                _log.LogWarning("Cinema Glass Letterboxd: Abgleich fehlgeschlagen ({Type}: {Message})", ex.GetType().Name, ex.Message);
            }
            finally
            {
                lock (state)
                {
                    state.Running = false;
                    state.Finished = Catalog.Now;
                }
            }

            if (any)
            {
                try
                {
                    changed(user);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _log.LogWarning("Cinema Glass Letterboxd: Neu ordnen fehlgeschlagen ({Type}: {Message})", ex.GetType().Name, ex.Message);
                }
            }
        });
        return true;
    }

    private async Task<bool> SyncAsync(LetterboxdLink link, bool force, LbSyncState state, CancellationToken stop)
    {
        await _gate.WaitAsync(stop).ConfigureAwait(false);
        var any = false;
        try
        {
            var now = Catalog.Now;
            var limit = now - (long)MaxAge.TotalSeconds;
            var ownLimit = force ? now - (long)ForceGap.TotalSeconds : limit;

            // Erst das eigene Konto: Gibt es den Namen nicht, ist der Rest sinnlos.
            if (Stale(link.User, ownLimit))
            {
                var r = await FeedAsync(link.User, stop).ConfigureAwait(false);
                any |= r.Changed;
                if (r.Error is "cloudflare" or "notfound")
                {
                    state.Error = r.Error;
                    return any;
                }
            }

            if (!link.Only && FollowingStale(link.User, ownLimit))
            {
                var error = await FollowingAsync(link.User, stop).ConfigureAwait(false);
                any |= error is null;
                if (error == "cloudflare")
                {
                    state.Error = error;
                    return any;
                }
            }

            var todo = Friends(link).Where(f => Stale(f.User, limit)).Select(f => f.User).ToList();
            state.Total = todo.Count;
            foreach (var friend in todo)
            {
                var r = await FeedAsync(friend, stop).ConfigureAwait(false);
                any |= r.Changed;
                state.Done++;
                if (r.Error == "cloudflare")
                {
                    state.Error = r.Error;
                    break;
                }
            }

            return any;
        }
        finally
        {
            if (any || state.Done > 0)
            {
                Save();
            }

            _gate.Release();
        }
    }

    private bool Stale(string user, long limit)
    {
        lock (_lock)
        {
            return !Members().TryGetValue(user, out var m) || m.At < limit;
        }
    }

    private bool FollowingStale(string user, long limit)
    {
        lock (_lock)
        {
            return !Members().TryGetValue(user, out var m) || m.FollowingAt < limit;
        }
    }

    /// <summary>Liest das Tagebuch eines Kontos (RSS) und führt es mit dem Bekannten zusammen.</summary>
    private async Task<(bool Changed, string? Error)> FeedAsync(string user, CancellationToken stop)
    {
        var (_, body, error) = await GetAsync(user + "/rss/", stop).ConfigureAwait(false);
        Dictionary<string, LbEntry>? fresh = null;
        if (error is null && body is not null)
        {
            fresh = ParseFeed(body);
            if (fresh is null)
            {
                error = "format";
            }
        }

        lock (_lock)
        {
            var members = Members();
            if (!members.TryGetValue(user, out var m))
            {
                members[user] = m = new LbMember();
            }

            m.At = Catalog.Now;
            m.Error = error;
            if (fresh is null)
            {
                return (false, error);
            }

            m.Ok = m.At;
            var changed = false;
            foreach (var f in fresh)
            {
                if (!m.Entries.TryGetValue(f.Key, out var old) || old.Rating != f.Value.Rating || old.Like != f.Value.Like || old.Watched != f.Value.Watched)
                {
                    // Im Tagebuch kann ein Titel öfter stehen: die jüngste Bewertung gilt, ein Herz bleibt.
                    if (old is not null && old.Watched > f.Value.Watched)
                    {
                        continue;
                    }

                    m.Entries[f.Key] = new LbEntry
                    {
                        Rating = f.Value.Rating > 0f ? f.Value.Rating : old?.Rating ?? 0f,
                        Like = f.Value.Like || (old?.Like ?? false),
                        Watched = f.Value.Watched,
                    };
                    changed = true;
                }
            }

            if (m.Entries.Count > MaxEntries)
            {
                foreach (var key in m.Entries.OrderBy(e => e.Value.Watched).Take(m.Entries.Count - MaxEntries).Select(e => e.Key).ToList())
                {
                    m.Entries.Remove(key);
                }
            }

            return (changed, null);
        }
    }

    /// <summary>Liest, wem ein Konto folgt. Ohne Browser zeigt Letterboxd meist nur die erste Seite (25 Konten).</summary>
    private async Task<string?> FollowingAsync(string user, CancellationToken stop)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        var partial = false;
        string? error = null;
        for (var page = 1; page <= MaxFollowingPages && found.Count < MaxFriends; page++)
        {
            var path = user + "/following/" + (page > 1 ? "page/" + page.ToString(CultureInfo.InvariantCulture) + "/" : string.Empty);
            var (_, body, err) = await GetAsync(path, stop, page > 1).ConfigureAwait(false);
            if (err is not null)
            {
                if (page == 1)
                {
                    error = err;
                }
                else
                {
                    // Weitere Seiten sperrt Cloudflare gern (ohne Browser): dann bleibt es bei der ersten.
                    partial = true;
                }

                break;
            }

            var before = found.Count;
            foreach (Match match in FollowingPattern().Matches(body!))
            {
                var name = match.Groups[1].Value.ToLowerInvariant();
                if (name != user && found.Count < MaxFriends)
                {
                    found.TryAdd(name, WebUtility.HtmlDecode(match.Groups[2].Value).Trim());
                }
            }

            // Ohne Verweis auf die nächste Seite (oder ohne neue Namen) war das die letzte.
            if (found.Count == before || !body!.Contains("/following/page/" + (page + 1).ToString(CultureInfo.InvariantCulture) + "/", StringComparison.Ordinal))
            {
                break;
            }

            if (page == MaxFollowingPages || found.Count >= MaxFriends)
            {
                partial = true;
            }
        }

        lock (_lock)
        {
            var members = Members();
            if (!members.TryGetValue(user, out var m))
            {
                members[user] = m = new LbMember();
            }

            m.FollowingAt = Catalog.Now;
            m.FollowingError = error;
            if (error is null)
            {
                m.Following = found;
                m.FollowingPartial = partial;
                foreach (var f in found)
                {
                    // Anzeigenamen der Freunde merken (für „Lisa ★★★★½“).
                    if (!members.TryGetValue(f.Key, out var friend))
                    {
                        members[f.Key] = friend = new LbMember();
                    }

                    if (f.Value.Length > 0)
                    {
                        friend.Name = f.Value;
                    }
                }
            }
        }

        return error;
    }

    /// <summary>Ein Abruf bei Letterboxd: höchstens einer je Sekunde, mit Zeitlimit; Sperren durch Cloudflare werden erkannt.</summary>
    /// <param name="soft">Eine Sperre gilt nur für diesen Abruf (weitere Seiten der Liste „Gefolgt“), nicht für eine Stunde.</param>
    private async Task<(int Status, string? Body, string? Error)> GetAsync(string path, CancellationToken stop, bool soft = false)
    {
        if (DateTime.UtcNow < _blockedUntil)
        {
            return (0, null, "cloudflare");
        }

        var wait = _lastRequest + Gap - DateTime.UtcNow;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, stop).ConfigureAwait(false);
        }

        _lastRequest = DateTime.UtcNow;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
            msg.Headers.TryAddWithoutValidation("User-Agent", Agent);
            msg.Headers.TryAddWithoutValidation("Accept", path.EndsWith("/rss/", StringComparison.Ordinal) ? "application/rss+xml, application/xml;q=0.9, */*;q=0.8" : "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
            msg.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.8");
            using var res = await _http.CreateClient("CinemaGlass").SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            var status = (int)res.StatusCode;
            var challenged = res.Headers.TryGetValues("cf-mitigated", out var mitigated) && mitigated.Any(v => v.Contains("challenge", StringComparison.OrdinalIgnoreCase));
            if (challenged || (status is 403 or 429 or 503 && res.Headers.Server.ToString().Contains("cloudflare", StringComparison.OrdinalIgnoreCase)))
            {
                if (soft)
                {
                    return (status, null, "cloudflare");
                }

                _blockedUntil = DateTime.UtcNow + BlockedPause;
                _log.LogInformation("Cinema Glass Letterboxd: Cloudflare sperrt den Abruf ({Status}); nächster Versuch frühestens in einer Stunde", status);
                return (status, null, "cloudflare");
            }

            if (status == 404)
            {
                return (status, null, "notfound");
            }

            if (!res.IsSuccessStatusCode)
            {
                return (status, null, "network");
            }

            await using var stream = await res.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var buffer = new char[MaxBody];
            var length = 0;
            int read;
            while (length < buffer.Length && (read = await reader.ReadAsync(buffer.AsMemory(length, buffer.Length - length), limit.Token).ConfigureAwait(false)) > 0)
            {
                length += read;
            }

            var body = new string(buffer, 0, length);
            if (body.Contains("<title>Just a moment", StringComparison.OrdinalIgnoreCase))
            {
                if (!soft)
                {
                    _blockedUntil = DateTime.UtcNow + BlockedPause;
                }

                return (status, null, "cloudflare");
            }

            return (status, body, null);
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            return (0, null, "network");
        }
        catch (HttpRequestException)
        {
            return (0, null, "network");
        }
    }

    /// <summary>Die Einträge eines Tagebuch-Feeds mit TMDb-Nummer; Listen und Einträge ohne Nummer fallen weg. <c>null</c> = kein RSS.</summary>
    internal static Dictionary<string, LbEntry>? ParseFeed(string xml)
    {
        XDocument doc;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            doc = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        if (doc.Root?.Name.LocalName != "rss")
        {
            return null;
        }

        var result = new Dictionary<string, LbEntry>(StringComparer.Ordinal);
        foreach (var item in doc.Descendants("item"))
        {
            var movie = (string?)item.Element(NsTmdb + "movieId");
            var tv = (string?)item.Element(NsTmdb + "tvId");
            var key = !string.IsNullOrWhiteSpace(movie) ? "movie:" + movie.Trim() : !string.IsNullOrWhiteSpace(tv) ? "tv:" + tv.Trim() : null;
            if (key is null || !MediaKey.TryParse(key, out var parsed))
            {
                continue;
            }

            var rating = float.TryParse((string?)item.Element(NsLb + "memberRating"), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? Math.Clamp(r, 0f, 5f) : 0f;
            var like = string.Equals(((string?)item.Element(NsLb + "memberLike"))?.Trim(), "Yes", StringComparison.OrdinalIgnoreCase);
            long watched = 0;
            if (DateTime.TryParseExact(((string?)item.Element(NsLb + "watchedDate"))?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var w))
            {
                watched = new DateTimeOffset(w, TimeSpan.Zero).ToUnixTimeSeconds();
            }
            else if (DateTimeOffset.TryParse((string?)item.Element("pubDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var pub))
            {
                watched = pub.ToUnixTimeSeconds();
            }

            var name = parsed.ToString();
            if (result.TryGetValue(name, out var old))
            {
                // Der Feed steht mit dem Neuesten oben: ältere Einträge ergänzen nur, was fehlt.
                old.Rating = old.Rating > 0f ? old.Rating : rating;
                old.Like |= like;
                continue;
            }

            result[name] = new LbEntry { Rating = rating, Like = like, Watched = watched };
        }

        return result;
    }

    // ---- Datei ------------------------------------------------------------------------------------------------

    private string PathOf() => Path.Combine(_folder(), "letterboxd.json");

    /// <summary>Unter <see cref="_lock"/> aufrufen.</summary>
    private Dictionary<string, LbMember> Members()
    {
        if (_members is not null)
        {
            return _members;
        }

        try
        {
            var path = PathOf();
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, LbMember>>(stream, FileJson);
                if (loaded is not null)
                {
                    foreach (var m in loaded.Values)
                    {
                        m.Entries ??= new(StringComparer.Ordinal);
                    }

                    return _members = new Dictionary<string, LbMember>(loaded, StringComparer.Ordinal);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // Unlesbar: neu anfangen, die Datei wird beim nächsten Abgleich ersetzt.
        }

        return _members = new Dictionary<string, LbMember>(StringComparer.Ordinal);
    }

    private void Save()
    {
        try
        {
            byte[] bytes;
            lock (_lock)
            {
                bytes = JsonSerializer.SerializeToUtf8Bytes(Members(), FileJson);
            }

            var folder = _folder();
            Directory.CreateDirectory(folder);
            var path = PathOf();
            File.WriteAllBytes(path + ".tmp", bytes);
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Gilt im Arbeitsspeicher weiter; der nächste Abgleich schreibt erneut.
        }
    }

    private static string Iso(long unix) => DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}

// ---- Antworten und Eingaben ------------------------------------------------------------------------------------

internal sealed class LetterboxdDto
{
    public bool Enabled { get; set; }

    public string? User { get; set; }

    public string? Name { get; set; }

    public List<string> Friends { get; set; } = new();

    public bool Only { get; set; }

    /// <summary>Eigene Einträge im Zwischenspeicher.</summary>
    public int Ratings { get; set; }

    public int Following { get; set; }

    public bool FollowingPartial { get; set; }

    public string? FollowingError { get; set; }

    public string? UserError { get; set; }

    public int FriendRatings { get; set; }

    public string? LastSync { get; set; }

    public bool Running { get; set; }

    public int Done { get; set; }

    public int Total { get; set; }

    /// <summary><c>cloudflare</c>, <c>notfound</c>, <c>network</c>, <c>none</c> oder <c>null</c>.</summary>
    public string? Error { get; set; }

    public List<LetterboxdFriendDto> FriendList { get; set; } = new();

    /// <summary>Eingaben, die kein gültiger Letterboxd-Name sind.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Invalid { get; set; }
}

internal sealed class LetterboxdFriendDto
{
    public string User { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Manual { get; set; }

    public int Ratings { get; set; }

    public int Good { get; set; }

    public bool Synced { get; set; }

    public string? Error { get; set; }
}

internal sealed class LetterboxdInput
{
    public string? User { get; set; }

    /// <summary>Weitere Freunde, getrennt durch Komma, Leerzeichen oder Zeilen.</summary>
    public string? Friends { get; set; }

    public bool Only { get; set; }
}
