using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>Eine zwischengespeicherte Listenantwort: nur die Reihenfolge der Titel, die Titel selbst liegen im Katalog.</summary>
internal sealed class CachedList
{
    public DateTime At { get; init; }

    public DateTime Used { get; set; }

    public MediaKey[] Keys { get; init; } = Array.Empty<MediaKey>();

    /// <summary>Werkliste einer Person: ob sie beim Titel an gleicher Stelle Regie führte.</summary>
    public bool[]? Directs { get; init; }

    /// <summary>Name der Filmreihe.</summary>
    public string? Name { get; init; }
}

/// <summary>
/// Was alle Konten gemeinsam nutzen: Titel aus den Listen (je Sprache), die Listen selbst und die verdichteten Details.
/// Listen und Titel leben nur im Arbeitsspeicher und sind begrenzt; die Details liegen zusätzlich in einer Datei, weil
/// ihr Abruf teuer ist und sich ihr Inhalt kaum ändert.
/// </summary>
internal sealed class Catalog
{
    private const int MaxLists = 420;
    private const int MaxTitles = 7000;
    private const int MaxDetails = 2000;
    private const string FileName = "cache-details.json";

    private static readonly JsonSerializerOptions FileJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<MediaKey, Title>> _titles = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CachedList> _lists = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<MediaKey, Detail> _details = new();
    private readonly ConcurrentDictionary<int, string> _people = new();
    private readonly ConcurrentDictionary<int, string> _keywords = new();
    private readonly ConcurrentDictionary<string, int> _personIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int[]> _languageCheck = new(StringComparer.Ordinal);
    private readonly object _fileLock = new();
    private readonly Func<string> _folder;
    private int _dirty;
    private int _loaded;

    public Catalog(Func<string> folder) => _folder = folder;

    public int ListCount => _lists.Count;

    public int DetailCount => _details.Count;

    public int TitleCount => _titles.Values.Sum(m => m.Count);

    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // ---- Titel -------------------------------------------------------------------------------------------------

    public Title? Get(string lang, MediaKey key) =>
        _titles.TryGetValue(lang, out var map) && map.TryGetValue(key, out var title) ? title : null;

    /// <summary>
    /// Nimmt einen Titel auf. Manche Listen von Seerr liefern Namen in der Sprache des Seerr-Kontos statt in der
    /// gewünschten; ein Name aus einem Abruf mit sicherer Sprache wird deshalb nie durch einen unsicheren ersetzt.
    /// </summary>
    public Title Put(string lang, Title title)
    {
        var map = _titles.GetOrAdd(lang, _ => new ConcurrentDictionary<MediaKey, Title>());
        if (map.TryGetValue(title.Key, out var old))
        {
            if (old.Sure != title.Sure)
            {
                // Nebenbei mitzählen, ob beide Arten von Abrufen dieselben Namen liefern (siehe LanguageDiffers).
                var stat = _languageCheck.GetOrAdd(lang, _ => new int[2]);
                Interlocked.Increment(ref stat[string.Equals(old.Name, title.Name, StringComparison.Ordinal) ? 0 : 1]);
            }

            if (old.Sure && !title.Sure)
            {
                return old;
            }
        }

        map[title.Key] = title;
        return title;
    }

    /// <summary>
    /// Ob Seerr die gefilterten Listen (<c>discover/movies</c>, <c>discover/tv</c>) in einer anderen Sprache liefert als
    /// gewünscht. Dort bestimmt die Sprache des Seerr-Kontos den Text; der Parameter <c>language</c> filtert stattdessen
    /// nach Originalsprache. Stimmen die Sprachen nicht überein, holt die Berechnung Namen und Texte über die Details nach.
    /// </summary>
    public bool LanguageDiffers(string lang) =>
        _languageCheck.TryGetValue(lang, out var stat) && stat[1] >= 4 && stat[1] > 0.2 * (stat[0] + stat[1]);

    /// <summary>Titel aus den Details bauen oder vorhandene mit sicherem Namen und Text versehen.</summary>
    public Title? FromDetail(string lang, MediaKey key)
    {
        if (!_details.TryGetValue(key, out var d) || !d.Texts.TryGetValue(lang, out var text))
        {
            return null;
        }

        var old = Get(lang, key);
        if (old is not null && old.Sure)
        {
            return old;
        }

        return Put(lang, new Title
        {
            Key = key,
            Name = text.Name,
            Overview = text.Overview ?? old?.Overview,
            Poster = old?.Poster ?? d.Poster,
            Backdrop = old?.Backdrop ?? d.Backdrop,
            Genres = old?.Genres ?? d.Genres,
            Rating = old?.Rating ?? d.Rating,
            Votes = old?.Votes ?? d.Votes,
            Popularity = old?.Popularity ?? d.Popularity,
            Date = old?.Date > 0 ? old.Date : d.Date,
            Lang = d.Lang,
            Adult = d.Adult,
            Sure = true,
        });
    }

    // ---- Listen ------------------------------------------------------------------------------------------------

    public CachedList? List(string cacheKey, TimeSpan maxAge)
    {
        if (_lists.TryGetValue(cacheKey, out var list) && DateTime.UtcNow - list.At < maxAge)
        {
            list.Used = DateTime.UtcNow;
            return list;
        }

        return null;
    }

    public void PutList(string cacheKey, CachedList list)
    {
        list.Used = DateTime.UtcNow;
        _lists[cacheKey] = list;
        if (_lists.Count > MaxLists)
        {
            foreach (var old in _lists.OrderBy(p => p.Value.Used).Take(MaxLists / 8).Select(p => p.Key).ToList())
            {
                _lists.TryRemove(old, out _);
            }
        }
    }

    public void DropList(string cacheKey) => _lists.TryRemove(cacheKey, out _);

    /// <summary>Hält den Katalog klein: Titel, die keine Liste und kein Konto mehr braucht, fallen heraus.</summary>
    public void Trim(string lang, Func<IEnumerable<MediaKey>> pinned)
    {
        if (!_titles.TryGetValue(lang, out var map) || map.Count <= MaxTitles)
        {
            return;
        }

        var keep = new HashSet<MediaKey>(pinned());
        var prefix = lang + "|";
        foreach (var list in _lists)
        {
            if (list.Key.StartsWith(prefix, StringComparison.Ordinal))
            {
                keep.UnionWith(list.Value.Keys);
            }
        }

        foreach (var key in map.Keys)
        {
            if (!keep.Contains(key))
            {
                map.TryRemove(key, out _);
            }
        }
    }

    public void Clear()
    {
        _lists.Clear();
        _titles.Clear();
        _languageCheck.Clear();
    }

    // ---- Details -----------------------------------------------------------------------------------------------

    public Detail? Detail(MediaKey key)
    {
        EnsureLoaded();
        if (_details.TryGetValue(key, out var d))
        {
            d.Used = Now;
            return d;
        }

        return null;
    }

    /// <summary>Ob Details fehlen, veraltet sind oder der Text in dieser Sprache fehlt.</summary>
    /// <param name="schedule">Für den Stand einer Anfrage: Es braucht die Termine (Folgen je Staffel, nächste Folge, Termin für zu Hause).</param>
    /// <param name="maxAge">Höchstalter in Sekunden, wenn es frischer sein soll als üblich; 0 = üblich.</param>
    public bool NeedsDetail(MediaKey key, string? lang, int today, bool schedule = false, long maxAge = 0)
    {
        EnsureLoaded();
        if (!_details.TryGetValue(key, out var d))
        {
            return true;
        }

        if (lang is not null && !d.Texts.ContainsKey(lang))
        {
            return true;
        }

        if ((schedule && (key.Tv ? d.Episodes is null || d.EpisodeCounts is null : d.HomeRelease is null)) || (maxAge > 0 && Now - d.At > maxAge))
        {
            return true;
        }

        // Bei neuen und kommenden Titeln ändern sich Termin, Bewertung und Plakat noch; alles andere hält lange.
        var fresh = d.Date == 0 || d.Date > today - 200;
        return Now - d.At > (fresh ? 3 * 86400 : 45 * 86400);
    }

    /// <summary>Lässt die Details eines Titels als veraltet gelten; der nächste, der sie braucht, holt sie neu.</summary>
    public void Expire(MediaKey key)
    {
        EnsureLoaded();
        if (_details.TryGetValue(key, out var d))
        {
            d.At = 0;
        }
    }

    public void PutDetail(Detail detail)
    {
        EnsureLoaded();
        detail.At = Now;
        detail.Used = detail.At;
        if (_details.TryGetValue(detail.Key, out var old))
        {
            // Texte in anderen Sprachen bleiben erhalten.
            foreach (var text in old.Texts)
            {
                detail.Texts.TryAdd(text.Key, text.Value);
            }
        }

        _details[detail.Key] = detail;
        Interlocked.Exchange(ref _dirty, 1);
    }

    public void NotePerson(int id, string name)
    {
        _people[id] = name;
        _personIds[name.Trim().ToLowerInvariant()] = id;
    }

    public void NoteKeyword(int id, string name) => _keywords[id] = name;

    public string? Person(int id) => _people.TryGetValue(id, out var name) ? name : null;

    public string? Keyword(int id) => _keywords.TryGetValue(id, out var name) ? name : null;

    /// <summary>TMDb-Nummer einer Person zu ihrem Namen (klein geschrieben), soweit sie in Details vorkam.</summary>
    public int PersonId(string feature) => _personIds.TryGetValue(feature, out var id) ? id : 0;

    // ---- Datei -------------------------------------------------------------------------------------------------

    private void EnsureLoaded()
    {
        if (Volatile.Read(ref _loaded) == 1)
        {
            return;
        }

        lock (_fileLock)
        {
            if (_loaded == 1)
            {
                return;
            }

            try
            {
                var path = Path.Combine(_folder(), FileName);
                if (!File.Exists(path))
                {
                    return;
                }

                using var stream = File.OpenRead(path);
                var file = JsonSerializer.Deserialize<DetailFile>(stream, FileJson);
                if (file is null || file.Version != DetailFile.Current)
                {
                    return;
                }

                foreach (var p in file.People)
                {
                    NotePerson(p.Key, p.Value);
                }

                foreach (var k in file.Keywords)
                {
                    _keywords[k.Key] = k.Value;
                }

                foreach (var item in file.Items)
                {
                    if (MediaKey.TryParse(item.Key, out var key))
                    {
                        _details.TryAdd(key, item.ToDetail(key));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
            {
                // Eine unlesbare Datei kostet nur neue Abrufe.
            }
            finally
            {
                Volatile.Write(ref _loaded, 1);
            }
        }
    }

    /// <summary>Schreibt die Details, wenn sich etwas geändert hat. Atomar: erst daneben schreiben, dann umbenennen.</summary>
    public void Save()
    {
        if (Interlocked.Exchange(ref _dirty, 0) == 0)
        {
            return;
        }

        lock (_fileLock)
        {
            try
            {
                if (_details.Count > MaxDetails)
                {
                    foreach (var old in _details.OrderBy(p => p.Value.Used).Take(_details.Count - (MaxDetails * 9 / 10)).Select(p => p.Key).ToList())
                    {
                        _details.TryRemove(old, out _);
                    }
                }

                var file = new DetailFile();
                var people = new HashSet<int>();
                var keywords = new HashSet<int>();
                foreach (var d in _details.Values)
                {
                    file.Items.Add(DetailRow.From(d));
                    people.UnionWith(d.Directors);
                    people.UnionWith(d.Cast);
                    keywords.UnionWith(d.Keywords);
                }

                // Nur Namen behalten, die noch ein Titel braucht.
                foreach (var id in people)
                {
                    if (_people.TryGetValue(id, out var name))
                    {
                        file.People[id] = name;
                    }
                }

                foreach (var id in keywords)
                {
                    if (_keywords.TryGetValue(id, out var name))
                    {
                        file.Keywords[id] = name;
                    }
                }

                var folder = _folder();
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, FileName);
                var temp = path + ".tmp";
                using (var stream = File.Create(temp))
                {
                    JsonSerializer.Serialize(stream, file, FileJson);
                }

                File.Move(temp, path, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Interlocked.Exchange(ref _dirty, 1);
            }
        }
    }

    private sealed class DetailFile
    {
        public const int Current = 1;

        [JsonPropertyName("v")]
        public int Version { get; set; } = Current;

        [JsonPropertyName("people")]
        public Dictionary<int, string> People { get; set; } = new();

        [JsonPropertyName("keywords")]
        public Dictionary<int, string> Keywords { get; set; } = new();

        [JsonPropertyName("items")]
        public List<DetailRow> Items { get; set; } = new();
    }

    private sealed class DetailRow
    {
        [JsonPropertyName("k")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("at")]
        public long At { get; set; }

        [JsonPropertyName("u")]
        public long Used { get; set; }

        [JsonPropertyName("g")]
        public int[]? Genres { get; set; }

        [JsonPropertyName("kw")]
        public int[]? Keywords { get; set; }

        [JsonPropertyName("d")]
        public int[]? Directors { get; set; }

        [JsonPropertyName("c")]
        public int[]? Cast { get; set; }

        [JsonPropertyName("l")]
        public string? Lang { get; set; }

        [JsonPropertyName("dt")]
        public int Date { get; set; }

        [JsonPropertyName("r")]
        public float Rating { get; set; }

        [JsonPropertyName("n")]
        public int Votes { get; set; }

        [JsonPropertyName("p")]
        public float Popularity { get; set; }

        [JsonPropertyName("rt")]
        public int Runtime { get; set; }

        [JsonPropertyName("s")]
        public int Seasons { get; set; }

        [JsonPropertyName("e")]
        public int[]? Episodes { get; set; }

        [JsonPropertyName("ec")]
        public int[]? EpisodeCounts { get; set; }

        [JsonPropertyName("nd")]
        public int NextDate { get; set; }

        [JsonPropertyName("ns")]
        public int NextSeason { get; set; }

        [JsonPropertyName("ne")]
        public int NextEpisode { get; set; }

        /// <summary>Um eins erhöht gespeichert, damit „kein Termin bekannt“ (0) nicht mit „nicht erhoben“ zusammenfällt.</summary>
        [JsonPropertyName("hr")]
        public int HomeRelease { get; set; }

        [JsonPropertyName("col")]
        public int Collection { get; set; }

        [JsonPropertyName("a")]
        public bool Adult { get; set; }

        [JsonPropertyName("po")]
        public string? Poster { get; set; }

        [JsonPropertyName("bd")]
        public string? Backdrop { get; set; }

        [JsonPropertyName("t")]
        public Dictionary<string, TextRow>? Texts { get; set; }

        public static DetailRow From(Detail d) => new()
        {
            Key = d.Key.ToString(),
            At = d.At,
            Used = d.Used,
            Genres = d.Genres,
            Keywords = d.Keywords,
            Directors = d.Directors,
            Cast = d.Cast,
            Lang = d.Lang,
            Date = d.Date,
            Rating = MathF.Round(d.Rating, 2),
            Votes = d.Votes,
            Popularity = MathF.Round(d.Popularity, 1),
            Runtime = d.Runtime,
            Seasons = d.Seasons,
            Episodes = d.Episodes,
            EpisodeCounts = d.EpisodeCounts,
            NextDate = d.NextDate,
            NextSeason = d.NextSeason,
            NextEpisode = d.NextEpisode,
            HomeRelease = d.HomeRelease is { } home ? home + 1 : 0,
            Collection = d.Collection,
            Adult = d.Adult,
            Poster = d.Poster,
            Backdrop = d.Backdrop,
            Texts = d.Texts.ToDictionary(t => t.Key, t => new TextRow { Name = t.Value.Name, Overview = t.Value.Overview, Collection = t.Value.Collection }, StringComparer.Ordinal),
        };

        public Detail ToDetail(MediaKey key)
        {
            var d = new Detail
            {
                Key = key,
                At = At,
                Used = Used,
                Genres = Genres ?? Array.Empty<int>(),
                Keywords = Keywords ?? Array.Empty<int>(),
                Directors = Directors ?? Array.Empty<int>(),
                Cast = Cast ?? Array.Empty<int>(),
                Lang = string.Intern(Lang ?? string.Empty),
                Date = Date,
                Rating = Rating,
                Votes = Votes,
                Popularity = Popularity,
                Runtime = Runtime,
                Seasons = Seasons,
                Episodes = Episodes,
                EpisodeCounts = EpisodeCounts,
                NextDate = NextDate,
                NextSeason = NextSeason,
                NextEpisode = NextEpisode,
                HomeRelease = HomeRelease > 0 ? HomeRelease - 1 : null,
                Collection = Collection,
                Adult = Adult,
                Poster = Poster,
                Backdrop = Backdrop,
            };
            if (Texts is not null)
            {
                foreach (var t in Texts)
                {
                    d.Texts[t.Key] = new DetailText { Name = t.Value.Name ?? string.Empty, Overview = t.Value.Overview, Collection = t.Value.Collection };
                }
            }

            return d;
        }
    }

    private sealed class TextRow
    {
        [JsonPropertyName("n")]
        public string? Name { get; set; }

        [JsonPropertyName("o")]
        public string? Overview { get; set; }

        [JsonPropertyName("c")]
        public string? Collection { get; set; }
    }
}
