using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>
/// Liest bei Seerr, dem einzigen Dienst, mit dem die Empfehlungen sprechen. Höchstens vier Abrufe zugleich, jeder mit
/// Zeitlimit; bei einem Verbindungs- oder Serverfehler gibt es einen zweiten Versuch. Ein Fehler ergibt <c>null</c> und
/// nie eine Ausnahme, damit eine einzelne ausgefallene Liste die Seite nicht verhindert.
/// </summary>
internal sealed class SeerrClient
{
    public const int ListTimeout = 12000;

    /// <summary>Für Abrufe, auf die jemand wartet (Regal auf der Detailseite, Geschmacks-Check).</summary>
    public const int QuickTimeout = 6000;

    public const int DetailTimeout = 15000;

    private readonly IHttpClientFactory _http;
    private readonly SemaphoreSlim _gate = new(4, 4);
    private long _requests;
    private long _failed;
    private long _bytes;
    private int _unreachable;
    private long _pauseUntil;

    public SeerrClient(IHttpClientFactory http) => _http = http;

    public long Requests => Interlocked.Read(ref _requests);

    public long Failed => Interlocked.Read(ref _failed);

    public long Bytes => Interlocked.Read(ref _bytes);

    /// <summary>Kurzer Grund des letzten Fehlers, nur für die Diagnose.</summary>
    public string? LastError { get; private set; }

    /// <param name="path">Pfad hinter <c>api/v1/</c> samt Abfrage.</param>
    /// <param name="probe">Der Abruf soll gerade herausfinden, ob Seerr erreichbar ist, und läuft deshalb auch während einer Pause.</param>
    public async Task<JsonDocument?> GetAsync(string path, int timeoutMs, CancellationToken ct, bool probe = false)
    {
        // Auch eine schon laufende Berechnung fragt nichts mehr ab, sobald die Funktion abgeschaltet wird.
        var c = Plugin.Instance?.Configuration;
        if (c is null || !DiscoverService.Enabled)
        {
            return null;
        }

        // Antwortet Seerr mehrmals hintereinander gar nicht, wird eine halbe Minute lang nicht mehr gefragt. Sonst liefe bei
        // einem Ausfall jeder der vielen Abrufe einer Berechnung einzeln ins Zeitlimit.
        if (!probe && Environment.TickCount64 < Interlocked.Read(ref _pauseUntil))
        {
            Interlocked.Increment(ref _failed);
            return null;
        }

        var url = c.SeerrUrl.TrimEnd('/') + "/api/v1/" + path;
        var unreachable = false;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(300, ct).ConfigureAwait(false);
            }

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(timeoutMs);
                using var msg = new HttpRequestMessage(HttpMethod.Get, url);
                msg.Headers.TryAddWithoutValidation("X-Api-Key", c.SeerrApiKey);
                if (c.SeerrUserId.Length > 0)
                {
                    msg.Headers.TryAddWithoutValidation("X-Api-User", c.SeerrUserId);
                }

                msg.Headers.Accept.ParseAdd("application/json");
                Interlocked.Increment(ref _requests);
                using var res = await _http.CreateClient("CinemaGlass").SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
                unreachable = false;
                if (res.IsSuccessStatusCode)
                {
                    var stream = await res.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
                    await using (stream.ConfigureAwait(false))
                    {
                        var doc = await JsonDocument.ParseAsync(stream, default, limit.Token).ConfigureAwait(false);
                        Interlocked.Add(ref _bytes, res.Content.Headers.ContentLength ?? 0);
                        Interlocked.Exchange(ref _unreachable, 0);
                        Interlocked.Exchange(ref _pauseUntil, 0);
                        return doc;
                    }
                }

                var code = (int)res.StatusCode;
                LastError = "HTTP " + code.ToString(CultureInfo.InvariantCulture);
                if (code is >= 400 and < 500 and not 408 and not 429)
                {
                    // Falscher Schlüssel, unbekannter Titel: ein zweiter Versuch ändert daran nichts.
                    break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // Das Zeitlimit ist schon verbraucht; noch einmal so lange zu warten lohnt nicht.
                LastError = "Zeitlimit";
                unreachable = true;
                break;
            }
            catch (HttpRequestException)
            {
                LastError = "keine Verbindung";
                unreachable = true;
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or UriFormatException)
            {
                LastError = ex is UriFormatException or InvalidOperationException ? "Adresse ungültig" : "Antwort unlesbar";
                unreachable = ex is not JsonException;
            }
            finally
            {
                _gate.Release();
            }
        }

        Interlocked.Increment(ref _failed);
        if (unreachable && (Interlocked.Increment(ref _unreachable) >= 3 || probe))
        {
            Interlocked.Exchange(ref _pauseUntil, Environment.TickCount64 + 30000);
        }

        return null;
    }

}

/// <summary>Liest aus den Antworten von Seerr nur das, was gebraucht wird.</summary>
internal static partial class SeerrParse
{
    /// <summary>Beschreibungen werden für Karten und das große Bild gebraucht, nicht in voller Länge.</summary>
    private const int OverviewLength = 420;

    [GeneratedRegex(@"^/[A-Za-z0-9_\-]{6,80}\.(jpg|jpeg|png|webp|svg)$", RegexOptions.CultureInvariant)]
    private static partial Regex ImagePath();

    /// <summary>Ein Eintrag aus einer Liste (Empfehlungen, Entdecken, Werkliste, Teile einer Filmreihe).</summary>
    public static Title? Item(JsonElement e, bool? tv, bool sure)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var type = Str(e, "mediaType");
        bool isTv;
        if (type == "tv")
        {
            isTv = true;
        }
        else if (type == "movie")
        {
            isTv = false;
        }
        else if (type is null && tv.HasValue)
        {
            isTv = tv.Value;
        }
        else
        {
            return null;
        }

        var id = Int(e, "id");
        var name = Str(e, isTv ? "name" : "title") ?? Str(e, isTv ? "originalName" : "originalTitle");
        if (id <= 0 || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new Title
        {
            Key = new MediaKey(isTv, id),
            Name = name.Trim(),
            Overview = Cut(Str(e, "overview")),
            Poster = Image(Str(e, "posterPath")),
            Backdrop = Image(Str(e, "backdropPath")),
            Genres = Ints(e, "genreIds", 8),
            Rating = Float(e, "voteAverage"),
            Votes = Int(e, "voteCount"),
            Popularity = Float(e, "popularity"),
            Date = Date(Str(e, isTv ? "firstAirDate" : "releaseDate")),
            Lang = string.Intern(Str(e, "originalLanguage") ?? string.Empty),
            Adult = Bool(e, "adult"),
            Sure = sure,
        };
    }

    public static List<Title> Items(JsonElement root, string array, bool? tv, bool sure)
    {
        var list = new List<Title>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(array, out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in items.EnumerateArray())
            {
                var title = Item(e, tv, sure);
                if (title is not null)
                {
                    list.Add(title);
                }
            }
        }

        return list;
    }

    /// <summary>Die Details eines Titels, verdichtet. Namen von Personen und Stichworten gehen in die Tabellen.</summary>
    public static Detail? Detail(JsonElement e, MediaKey key, string lang, Action<int, string> person, Action<int, string> keyword, int today)
    {
        if (e.ValueKind != JsonValueKind.Object || Int(e, "id") != key.Id)
        {
            return null;
        }

        var name = Str(e, key.Tv ? "name" : "title") ?? Str(e, key.Tv ? "originalName" : "originalTitle");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var d = new Detail
        {
            Key = key,
            Lang = string.Intern(Str(e, "originalLanguage") ?? string.Empty),
            Date = Date(Str(e, key.Tv ? "firstAirDate" : "releaseDate")),
            Rating = Float(e, "voteAverage"),
            Votes = Int(e, "voteCount"),
            Popularity = Float(e, "popularity"),
            Runtime = key.Tv ? 0 : Int(e, "runtime"),
            Adult = Bool(e, "adult"),
            Poster = Image(Str(e, "posterPath")),
            Backdrop = Image(Str(e, "backdropPath")),
        };

        var genres = new List<int>(6);
        if (e.TryGetProperty("genres", out var gs) && gs.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in gs.EnumerateArray())
            {
                var id = Int(g, "id");
                if (id > 0 && genres.Count < 8)
                {
                    genres.Add(id);
                }
            }
        }

        d.Genres = genres.ToArray();

        var keywords = new List<int>(16);
        if (e.TryGetProperty("keywords", out var ks) && ks.ValueKind == JsonValueKind.Array)
        {
            foreach (var k in ks.EnumerateArray())
            {
                var id = Int(k, "id");
                var kn = Str(k, "name");
                if (id > 0 && kn is not null && keywords.Count < 24)
                {
                    keywords.Add(id);
                    keyword(id, kn);
                }
            }
        }

        d.Keywords = keywords.ToArray();

        var directors = new List<int>(3);
        var cast = new List<int>(5);
        if (e.TryGetProperty("credits", out var credits) && credits.ValueKind == JsonValueKind.Object)
        {
            if (credits.TryGetProperty("cast", out var cs) && cs.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in cs.EnumerateArray())
                {
                    var id = Int(c, "id");
                    var pn = Str(c, "name");
                    if (id > 0 && !string.IsNullOrWhiteSpace(pn))
                    {
                        cast.Add(id);
                        person(id, pn);
                        if (cast.Count >= 5)
                        {
                            break;
                        }
                    }
                }
            }

            if (!key.Tv && credits.TryGetProperty("crew", out var crew) && crew.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in crew.EnumerateArray())
                {
                    if (Str(c, "job") != "Director")
                    {
                        continue;
                    }

                    var id = Int(c, "id");
                    var pn = Str(c, "name");
                    if (id > 0 && !string.IsNullOrWhiteSpace(pn) && !directors.Contains(id))
                    {
                        directors.Add(id);
                        person(id, pn);
                        if (directors.Count >= 3)
                        {
                            break;
                        }
                    }
                }
            }
        }

        if (key.Tv && e.TryGetProperty("createdBy", out var creators) && creators.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in creators.EnumerateArray())
            {
                var id = Int(c, "id");
                var pn = Str(c, "name");
                if (id > 0 && !string.IsNullOrWhiteSpace(pn) && directors.Count < 3)
                {
                    directors.Add(id);
                    person(id, pn);
                }
            }
        }

        d.Directors = directors.ToArray();
        d.Cast = cast.ToArray();

        string? collection = null;
        if (!key.Tv && e.TryGetProperty("collection", out var col) && col.ValueKind == JsonValueKind.Object)
        {
            d.Collection = Int(col, "id");
            collection = Str(col, "name");
        }

        if (!key.Tv)
        {
            // Ab wann es den Film für zu Hause gibt: der früheste digitale Termin (Art 4) oder der auf Disc (Art 5), egal in
            // welchem Land. Davor ist er nur im Kino zu sehen und nicht zu bekommen.
            var home = 0;
            if (e.TryGetProperty("releases", out var releases) && releases.ValueKind == JsonValueKind.Object
                && releases.TryGetProperty("results", out var countries) && countries.ValueKind == JsonValueKind.Array)
            {
                foreach (var country in countries.EnumerateArray())
                {
                    if (country.ValueKind != JsonValueKind.Object || !country.TryGetProperty("release_dates", out var dates) || dates.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var release in dates.EnumerateArray())
                    {
                        var date = Int(release, "type") is 4 or 5 ? Date(Str(release, "release_date")) : 0;
                        if (date > 0 && (home == 0 || date < home))
                        {
                            home = date;
                        }
                    }
                }
            }

            d.HomeRelease = home;
        }

        if (key.Tv)
        {
            // Angekündigte Staffeln zählen nicht: gefragt ist, wie viel es schon zu sehen gibt. Für die laufende Staffel
            // nennt TMDb die zuletzt ausgestrahlte Folge; alles davor ist ganz gelaufen, alles danach noch gar nicht.
            int lastSeason = -1, lastEpisode = 0;
            if (e.TryGetProperty("lastEpisodeToAir", out var last) && last.ValueKind == JsonValueKind.Object)
            {
                lastSeason = last.TryGetProperty("seasonNumber", out var ls) && ls.ValueKind == JsonValueKind.Number ? Int(last, "seasonNumber") : -1;
                lastEpisode = Int(last, "episodeNumber");
            }

            if (e.TryGetProperty("nextEpisodeToAir", out var next) && next.ValueKind == JsonValueKind.Object)
            {
                d.NextDate = Date(Str(next, "airDate"));
                d.NextSeason = Int(next, "seasonNumber");
                d.NextEpisode = Int(next, "episodeNumber");
            }

            var aired = 0;
            var episodes = new List<int>();
            var totals = new List<int>();
            if (e.TryGetProperty("seasons", out var seasons) && seasons.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in seasons.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object || !s.TryGetProperty("seasonNumber", out var sn) || sn.ValueKind != JsonValueKind.Number)
                    {
                        continue;
                    }

                    var number = Int(s, "seasonNumber");
                    var count = Int(s, "episodeCount");
                    var air = Date(Str(s, "airDate"));
                    var started = air > 0 && air <= today;
                    if (number > 0 && started)
                    {
                        aired++;
                    }

                    if (number is < 0 or > 99)
                    {
                        continue;
                    }

                    while (totals.Count <= number)
                    {
                        totals.Add(0);
                    }

                    totals[number] = Math.Max(0, count);
                    if (number > 0 && lastSeason > 0)
                    {
                        count = number < lastSeason ? count : number == lastSeason ? Math.Min(count, Math.Max(lastEpisode, 0)) : 0;
                    }
                    else if (number > 0 && !started)
                    {
                        count = 0;
                    }

                    while (episodes.Count <= number)
                    {
                        episodes.Add(0);
                    }

                    episodes[number] = Math.Max(0, count);
                }
            }

            d.Seasons = aired > 0 ? aired : Int(e, "numberOfSeasons");
            d.Episodes = episodes.ToArray();
            d.EpisodeCounts = totals.ToArray();
        }

        d.Texts[lang] = new DetailText { Name = name.Trim(), Overview = Cut(Str(e, "overview")), Collection = collection };
        return d;
    }

    /// <summary>Die Werkliste einer Person: Titel samt der Angabe, ob sie Regie führte bzw. die Serie erfand.</summary>
    public static List<(Title Title, bool Directs)> Credits(JsonElement root)
    {
        var list = new List<(Title, bool)>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return list;
        }

        if (root.TryGetProperty("crew", out var crew) && crew.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in crew.EnumerateArray())
            {
                var job = Str(e, "job");
                if (job is not ("Director" or "Creator"))
                {
                    continue;
                }

                var title = Item(e, null, true);
                if (title is not null)
                {
                    list.Add((title, true));
                }
            }
        }

        if (root.TryGetProperty("cast", out var cast) && cast.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in cast.EnumerateArray())
            {
                // Auftritte als Gast in Talkshows und Dokumentationen sagen nichts über den Geschmack.
                var role = Str(e, "character") ?? string.Empty;
                if (role.Length == 0 || role.Contains("Self", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var title = Item(e, null, true);
                if (title is not null && Array.IndexOf(title.Genres, 10767) < 0 && Array.IndexOf(title.Genres, 10763) < 0 && Array.IndexOf(title.Genres, 99) < 0)
                {
                    list.Add((title, false));
                }
            }
        }

        return list;
    }

    public static List<SeerrRequest> Requests(JsonElement root, DateTime fetched)
    {
        var list = new List<SeerrRequest>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var e in results.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = Int(media, "tmdbId");
            var type = Str(media, "mediaType") ?? Str(e, "type");
            if (id <= 0 || type is not ("movie" or "tv"))
            {
                continue;
            }

            string? user = null;
            if (e.TryGetProperty("requestedBy", out var by) && by.ValueKind == JsonValueKind.Object)
            {
                user = Str(by, "jellyfinUserId");
            }

            var seasons = new List<(int, int)>();
            if (e.TryGetProperty("seasons", out var ss) && ss.ValueKind == JsonValueKind.Array)
            {
                foreach (var season in ss.EnumerateArray())
                {
                    if (season.ValueKind == JsonValueKind.Object && season.TryGetProperty("seasonNumber", out var number) && number.ValueKind == JsonValueKind.Number && seasons.Count < 80)
                    {
                        seasons.Add((Int(season, "seasonNumber"), Int(season, "status")));
                    }
                }
            }

            var is4k = Bool(e, "is4k");
            var downloads = new List<DownloadEntry>();
            if (media.TryGetProperty(is4k ? "downloadStatus4k" : "downloadStatus", out var ds) && ds.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in ds.EnumerateArray())
                {
                    if (d.ValueKind != JsonValueKind.Object || downloads.Count >= 200)
                    {
                        continue;
                    }

                    DateTime? eta = Time(Str(d, "estimatedCompletionTime")) is var at && at > DateTime.MinValue ? at : null;
                    if (eta is null && TimeSpan.TryParse(Str(d, "timeLeft"), CultureInfo.InvariantCulture, out var left) && left > TimeSpan.Zero)
                    {
                        eta = fetched + left;
                    }

                    downloads.Add(new DownloadEntry
                    {
                        Size = Number(d, "size"),
                        SizeLeft = Number(d, "sizeLeft"),
                        Status = (Str(d, "status") ?? string.Empty).ToLowerInvariant(),
                        Eta = eta,
                        Season = d.TryGetProperty("episode", out var episode) && episode.ValueKind == JsonValueKind.Object && episode.TryGetProperty("seasonNumber", out var sn) && sn.ValueKind == JsonValueKind.Number
                            ? Int(episode, "seasonNumber")
                            : -1,
                    });
                }
            }

            var created = Time(Str(e, "createdAt"));
            var updated = Time(Str(e, "updatedAt"));
            var added = Time(Str(media, "mediaAddedAt"));
            list.Add(new SeerrRequest
            {
                Id = Int(e, "id"),
                Key = new MediaKey(type == "tv", id),
                Status = Int(e, "status"),
                MediaStatus = Int(media, is4k ? "status4k" : "status"),
                Created = created,
                Updated = updated > created ? updated : created,
                MediaAdded = added > DateTime.MinValue ? added : null,
                JellyfinUser = string.IsNullOrEmpty(user) ? null : user.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant(),
                Is4k = is4k,
                ServerId = e.TryGetProperty("serverId", out var server) && server.ValueKind == JsonValueKind.Number ? Int(e, "serverId") : -1,
                ProfileId = e.TryGetProperty("profileId", out var profile) && profile.ValueKind == JsonValueKind.Number ? Int(e, "profileId") : -1,
                Seasons = seasons.ToArray(),
                Downloads = downloads.ToArray(),
            });
        }

        return list;
    }

    /// <summary>Die Dienste einer Art (Radarr oder Sonarr), wie Seerr sie führt: Nummer, Voreinstellung, übliches Profil.</summary>
    public static List<(int Id, bool Default, bool Is4k, int Profile)> Servers(JsonElement root)
    {
        var list = new List<(int, bool, bool, int)>();
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in root.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                {
                    list.Add((Int(e, "id"), Bool(e, "isDefault"), Bool(e, "is4k"), Int(e, "activeProfileId")));
                }
            }
        }

        return list;
    }

    /// <summary>Die Qualitätsprofile eines Dienstes: Nummer und Name.</summary>
    public static Dictionary<int, string> Profiles(JsonElement root)
    {
        var map = new Dictionary<int, string>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("profiles", out var profiles) && profiles.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in profiles.EnumerateArray())
            {
                var name = Str(e, "name");
                if (name is not null && e.ValueKind == JsonValueKind.Object && e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                {
                    map[Int(e, "id")] = name;
                }
            }
        }

        return map;
    }

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static int Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && d is > int.MinValue and < int.MaxValue
            ? (int)d
            : 0;

    public static double Number(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d)
            ? d
            : 0d;

    public static float Float(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d)
            ? (float)d
            : 0f;

    public static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static int[] Ints(JsonElement e, string name, int max)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<int>();
        }

        var list = new List<int>(4);
        foreach (var x in v.EnumerateArray())
        {
            if (x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var i) && i > 0 && list.Count < max)
            {
                list.Add(i);
            }
        }

        return list.Count == 0 ? Array.Empty<int>() : list.ToArray();
    }

    /// <summary>„2026-12-16“ (auch mit Uhrzeit dahinter) als 20261216.</summary>
    public static int Date(string? text)
    {
        if (text is null || text.Length < 10 || text[4] != '-' || text[7] != '-')
        {
            return 0;
        }

        return int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            && int.TryParse(text.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            && int.TryParse(text.AsSpan(8, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var d)
            && y is > 1870 and < 2200 && m is >= 1 and <= 12 && d is >= 1 and <= 31
            ? (y * 10000) + (m * 100) + d
            : 0;
    }

    private static DateTime Time(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.MinValue;

    private static string? Image(string? path) => path is not null && ImagePath().IsMatch(path) ? path : null;

    private static string? Cut(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();
        return text.Length <= OverviewLength ? text : Texts.Shorten(text, OverviewLength);
    }
}
