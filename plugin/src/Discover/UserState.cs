using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>Eine Rückmeldung des Kontos zu einem Titel.</summary>
internal sealed class FeedbackEntry
{
    /// <summary><c>like</c>, <c>dislike</c> oder <c>hide</c>.</summary>
    [JsonPropertyName("a")]
    public string Action { get; set; } = string.Empty;

    /// <summary>Zeitpunkt in Unix-Sekunden.</summary>
    [JsonPropertyName("t")]
    public long At { get; set; }

    /// <summary>Wo die Rückmeldung entstand (Karte, großes Bild, Detailseite, Geschmacks-Check).</summary>
    [JsonPropertyName("s")]
    public string? Source { get; set; }
}

/// <summary>Wie oft ein Titel dem Konto gezeigt wurde und wann zuletzt.</summary>
internal sealed class ShownEntry
{
    [JsonPropertyName("n")]
    public int Count { get; set; }

    [JsonPropertyName("t")]
    public long At { get; set; }
}

/// <summary>Wann ein angefragter Titel angekommen ist und zu welcher Anfrage das gehört.</summary>
internal sealed class ArrivedEntry
{
    /// <summary>Ankunft in Unix-Sekunden.</summary>
    [JsonPropertyName("t")]
    public long At { get; set; }

    /// <summary>Zeitpunkt der Anfrage; eine spätere Anfrage zum selben Titel (weitere Staffel) zählt neu.</summary>
    [JsonPropertyName("r")]
    public long Requested { get; set; }
}

/// <summary>
/// Alles, was sich die Empfehlungen je Konto merken: Rückmeldungen, stummgeschaltete Merkmale, gewählte Welten, gezeigte
/// Titel, über das Theme gestellte Anfragen und ob der Geschmacks-Check gemacht ist. Eine kleine JSON-Datei je Konto.
/// </summary>
internal sealed class UserState
{
    [JsonPropertyName("v")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("tasteDone")]
    public bool TasteDone { get; set; }

    [JsonPropertyName("feedback")]
    public Dictionary<string, FeedbackEntry> Feedback { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("muted")]
    public List<string> Muted { get; set; } = new();

    /// <summary>Im Geschmacks-Check gewählte „Welten“ (Genre-Nummern).</summary>
    [JsonPropertyName("worlds")]
    public List<int> Worlds { get; set; } = new();

    [JsonPropertyName("shown")]
    public Dictionary<string, ShownEntry> Shown { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("requests")]
    public Dictionary<string, long> Requests { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Im Geschmacks-Check übersprungene Titel („Kenne ich nicht“) mit Zeitpunkt: kein Signal, nur eine Weile nicht mehr fragen.</summary>
    [JsonPropertyName("skipped")]
    public Dictionary<string, long> Skipped { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Angekommene Anfragen: hält fest, seit wann ein Titel da ist, damit „neu“ nicht bei jedem Abruf neu beginnt.</summary>
    [JsonPropertyName("arrived")]
    public Dictionary<string, ArrivedEntry> Arrived { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Eine unveränderliche Kopie des Zustands für eine Berechnung.</summary>
internal sealed class StateView
{
    public static readonly StateView Empty = new();

    public long Revision { get; init; }

    public bool TasteDone { get; init; }

    public Dictionary<MediaKey, FeedbackEntry> Feedback { get; init; } = new();

    public HashSet<string> Muted { get; init; } = new(StringComparer.Ordinal);

    public int[] Worlds { get; init; } = Array.Empty<int>();

    public Dictionary<MediaKey, ShownEntry> Shown { get; init; } = new();

    public Dictionary<MediaKey, long> Requests { get; init; } = new();

    public Dictionary<MediaKey, ArrivedEntry> Arrived { get; init; } = new();

    public Dictionary<MediaKey, long> Skipped { get; init; } = new();
}

/// <summary>
/// Liest und schreibt den Zustand der Konten. Jede Änderung läuft unter der Sperre des Kontos und landet sofort in der
/// Datei: erst daneben geschrieben, dann umbenannt, damit nie eine halbe Datei liegen bleibt.
/// </summary>
internal sealed class StateStore
{
    private const int MaxFeedback = 3000;
    private const int MaxShown = 1500;
    private const int MaxRequests = 500;
    private const int MaxMuted = 200;
    private const int MaxArrived = 200;
    private const int MaxSkipped = 2000;

    private static readonly JsonSerializerOptions FileJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ConcurrentDictionary<Guid, Holder> _users = new();
    private readonly Func<string> _folder;
    private volatile Dictionary<MediaKey, List<(Guid User, long At)>>? _themeRequests;
    private long _themeRequestsAt = -1;
    private long _changes;
    private int _allLoaded;

    public StateStore(Func<string> folder) => _folder = folder;

    public StateView View(Guid user)
    {
        var holder = Get(user);
        lock (holder)
        {
            var s = holder.State;
            var view = new StateView
            {
                Revision = holder.Revision,
                TasteDone = s.TasteDone,
                Muted = new HashSet<string>(s.Muted, StringComparer.Ordinal),
                Worlds = s.Worlds.ToArray(),
            };
            foreach (var f in s.Feedback)
            {
                if (MediaKey.TryParse(f.Key, out var key))
                {
                    view.Feedback[key] = f.Value;
                }
            }

            foreach (var f in s.Shown)
            {
                if (MediaKey.TryParse(f.Key, out var key))
                {
                    view.Shown[key] = f.Value;
                }
            }

            foreach (var f in s.Requests)
            {
                if (MediaKey.TryParse(f.Key, out var key))
                {
                    view.Requests[key] = f.Value;
                }
            }

            foreach (var f in s.Arrived)
            {
                if (MediaKey.TryParse(f.Key, out var key))
                {
                    view.Arrived[key] = f.Value;
                }
            }

            foreach (var f in s.Skipped)
            {
                if (MediaKey.TryParse(f.Key, out var key))
                {
                    view.Skipped[key] = f.Value;
                }
            }

            return view;
        }
    }

    public long Revision(Guid user)
    {
        var holder = Get(user);
        lock (holder)
        {
            return holder.Revision;
        }
    }

    /// <summary>Ändert den Zustand eines Kontos und schreibt ihn; liefert, ob sich etwas geändert hat.</summary>
    public bool Update(Guid user, Func<UserState, bool> change)
    {
        var holder = Get(user);
        lock (holder)
        {
            if (!change(holder.State))
            {
                return false;
            }

            Limit(holder.State);
            holder.Revision++;
            Interlocked.Increment(ref _changes);
            Write(user, holder.State);
            return true;
        }
    }

    /// <summary>
    /// Alle über das Theme gestellten Anfragen aller Konten: Titel → wer und wann. Bei Seerr laufen sie unter einem festen
    /// Konto; nur so lässt sich sagen, wem eine Anfrage gehört. Liest dafür einmal die Dateien aller Konten.
    /// </summary>
    public IReadOnlyDictionary<MediaKey, List<(Guid User, long At)>> ThemeRequests()
    {
        var changes = Interlocked.Read(ref _changes);
        var known = _themeRequests;
        if (known is not null && _themeRequestsAt == changes)
        {
            return known;
        }

        LoadAll();
        var map = new Dictionary<MediaKey, List<(Guid, long)>>();
        foreach (var user in _users)
        {
            lock (user.Value)
            {
                foreach (var r in user.Value.State.Requests)
                {
                    if (!MediaKey.TryParse(r.Key, out var key))
                    {
                        continue;
                    }

                    if (!map.TryGetValue(key, out var list))
                    {
                        map[key] = list = new List<(Guid, long)>(1);
                    }

                    list.Add((user.Key, r.Value));
                }
            }
        }

        _themeRequests = map;
        _themeRequestsAt = changes;
        return map;
    }

    private void LoadAll()
    {
        if (Interlocked.Exchange(ref _allLoaded, 1) == 1)
        {
            return;
        }

        try
        {
            var folder = _folder();
            if (!Directory.Exists(folder))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var user))
                {
                    Get(user);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dann zählen nur die Konten, die sich seit dem Start gemeldet haben.
        }
    }

    private static void Limit(UserState s)
    {
        // Obergrenzen, damit die Datei klein bleibt: das Älteste fällt zuerst heraus.
        if (s.Feedback.Count > MaxFeedback)
        {
            foreach (var key in s.Feedback.OrderBy(f => f.Value.At).Take(s.Feedback.Count - MaxFeedback).Select(f => f.Key).ToList())
            {
                s.Feedback.Remove(key);
            }
        }

        if (s.Shown.Count > MaxShown)
        {
            foreach (var key in s.Shown.OrderBy(f => f.Value.At).Take(s.Shown.Count - (MaxShown * 4 / 5)).Select(f => f.Key).ToList())
            {
                s.Shown.Remove(key);
            }
        }

        if (s.Requests.Count > MaxRequests)
        {
            foreach (var key in s.Requests.OrderBy(f => f.Value).Take(s.Requests.Count - MaxRequests).Select(f => f.Key).ToList())
            {
                s.Requests.Remove(key);
            }
        }

        if (s.Muted.Count > MaxMuted)
        {
            s.Muted.RemoveRange(0, s.Muted.Count - MaxMuted);
        }

        if (s.Skipped.Count > MaxSkipped)
        {
            foreach (var key in s.Skipped.OrderBy(f => f.Value).Take(s.Skipped.Count - (MaxSkipped * 4 / 5)).Select(f => f.Key).ToList())
            {
                s.Skipped.Remove(key);
            }
        }

        if (s.Arrived.Count > MaxArrived)
        {
            foreach (var key in s.Arrived.OrderBy(f => f.Value.At).Take(s.Arrived.Count - MaxArrived).Select(f => f.Key).ToList())
            {
                s.Arrived.Remove(key);
            }
        }
    }

    private Holder Get(Guid user) => _users.GetOrAdd(user, id => new Holder { State = Read(id) });

    private string PathOf(Guid user) => Path.Combine(_folder(), user.ToString("N", CultureInfo.InvariantCulture) + ".json");

    private UserState Read(Guid user)
    {
        try
        {
            var path = PathOf(user);
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var state = JsonSerializer.Deserialize<UserState>(stream, FileJson);
                if (state is not null)
                {
                    state.Feedback ??= new(StringComparer.Ordinal);
                    state.Muted ??= new();
                    state.Worlds ??= new();
                    state.Shown ??= new(StringComparer.Ordinal);
                    state.Requests ??= new(StringComparer.Ordinal);
                    state.Arrived ??= new(StringComparer.Ordinal);
                    state.Skipped ??= new(StringComparer.Ordinal);
                    return state;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // Unlesbar: mit leerem Zustand weitermachen, die Datei wird beim nächsten Schreiben ersetzt.
        }

        return new UserState();
    }

    private void Write(Guid user, UserState state)
    {
        try
        {
            var folder = _folder();
            Directory.CreateDirectory(folder);
            var path = PathOf(user);
            var temp = path + ".tmp";
            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, state, FileJson);
            }

            File.Move(temp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Der Zustand gilt im Arbeitsspeicher weiter; der nächste Schreibversuch holt es nach.
        }
    }

    private sealed class Holder
    {
        public UserState State { get; init; } = new();

        public long Revision { get; set; }
    }
}
