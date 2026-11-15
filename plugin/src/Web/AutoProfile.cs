using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.CinemaGlass.Configuration;
using Jellyfin.Plugin.CinemaGlass.Discover;

namespace Jellyfin.Plugin.CinemaGlass.Web;

/// <summary>
/// Wählt beim Durchreichen einer Anfrage an Seerr das Qualitätsprofil selbst: nach der Originalsprache des Titels (Regeln
/// „Sprache=Textstück im Profilnamen“) und in der Ruhezeit der Download-Dienste die Variante mit dem Kennzeichen
/// (z. B. „(morgen)“), damit nachts nichts die Platten weckt. Geht etwas schief, bleibt die Anfrage unverändert.
/// </summary>
internal static class AutoProfile
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<string, (DateTime At, List<(int Id, string Name)> Profiles, int? Server)> Services = new();

    public static async Task<string> ApplyAsync(IHttpClientFactory http, PluginConfiguration c, string body, CancellationToken ct)
    {
        var rules = Parse(c.AutoProfileRules);
        if (rules.Count == 0)
        {
            return body;
        }

        try
        {
            if (JsonNode.Parse(body) is not JsonObject root
                || root["mediaType"]?.GetValue<string>() is not ("movie" or "tv") || root["mediaId"] is null)
            {
                return body;
            }

            var tv = root["mediaType"]!.GetValue<string>() == "tv";
            var id = root["mediaId"]!.GetValue<int>();
            var service = tv ? "sonarr" : "radarr";
            int? server = root["serverId"] is JsonValue sv && sv.TryGetValue<int>(out var s) ? s : null;
            var (profiles, defaultServer) = await ProfilesAsync(http, c, service, server, ct).ConfigureAwait(false);
            if (profiles.Count == 0)
            {
                return body;
            }

            var marker = c.DownloadsDeferMarker.Trim();
            string? baseName = null;
            if (root["profileId"] is JsonValue pv && pv.TryGetValue<int>(out var chosen))
            {
                // Ausdrücklich gewählt: dabei bleibt es, nur nachts gilt die Variante mit Kennzeichen.
                baseName = profiles.FirstOrDefault(p => p.Id == chosen).Name;
            }
            else
            {
                var lang = await LanguageAsync(http, c, tv, id, ct).ConfigureAwait(false);
                var part = (lang is not null && rules.TryGetValue(lang, out var r)) ? r : rules.GetValueOrDefault("*");
                if (part is not null)
                {
                    baseName = profiles.Select(p => p.Name)
                        .FirstOrDefault(n => n.Contains(part, StringComparison.OrdinalIgnoreCase) && (marker.Length == 0 || !n.Contains(marker, StringComparison.OrdinalIgnoreCase)));
                }
            }

            if (baseName is null)
            {
                return body;
            }

            var name = baseName;
            var quiet = QuietHours.Read();
            if (quiet is { } q && marker.Length > 0 && q.EndIfWithin(DateTime.Now) is not null && !name.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                var later = profiles.FirstOrDefault(p => p.Name.Contains(name, StringComparison.OrdinalIgnoreCase) && p.Name.Contains(marker, StringComparison.OrdinalIgnoreCase));
                if (later.Name is not null)
                {
                    name = later.Name;
                }
            }

            var profile = profiles.First(p => p.Name == name);
            root["profileId"] = profile.Id;
            if (server is null && defaultServer is { } d)
            {
                root["serverId"] = d;
            }

            return root.ToJsonString();
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException or FormatException or TaskCanceledException)
        {
            return body;
        }
    }

    private static Dictionary<string, string> Parse(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (text ?? string.Empty).Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var i = line.IndexOf('=', StringComparison.Ordinal);
            if (i > 0 && line[(i + 1)..].Trim().Length > 0)
            {
                map[line[..i].Trim()] = line[(i + 1)..].Trim();
            }
        }

        return map;
    }

    private static HttpRequestMessage Get(PluginConfiguration c, string path)
    {
        var msg = new HttpRequestMessage(HttpMethod.Get, c.SeerrUrl.TrimEnd('/') + "/api/v1/" + path);
        msg.Headers.TryAddWithoutValidation("X-Api-Key", c.SeerrApiKey);
        return msg;
    }

    private static async Task<JsonNode?> CallAsync(IHttpClientFactory http, PluginConfiguration c, string path, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(8));
        using var msg = Get(c, path);
        using var res = await http.CreateClient("CinemaGlass").SendAsync(msg, cts.Token).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            return null;
        }

        return JsonNode.Parse(await res.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false));
    }

    private static async Task<string?> LanguageAsync(IHttpClientFactory http, PluginConfiguration c, bool tv, int id, CancellationToken ct)
    {
        var d = await CallAsync(http, c, (tv ? "tv/" : "movie/") + id.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        return d?["originalLanguage"]?.GetValue<string>();
    }

    private static async Task<(List<(int Id, string Name)> Profiles, int? Server)> ProfilesAsync(IHttpClientFactory http, PluginConfiguration c, string service, int? server, CancellationToken ct)
    {
        var key = service + "|" + server + "|" + c.SeerrUrl;
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Services.TryGetValue(key, out var known) && DateTime.UtcNow - known.At < TimeSpan.FromHours(1))
            {
                return (known.Profiles, known.Server);
            }

            var id = server;
            if (id is null && await CallAsync(http, c, "service/" + service, ct).ConfigureAwait(false) is JsonArray list)
            {
                var pick = list.OfType<JsonObject>().Where(x => x["is4k"]?.GetValue<bool>() != true)
                    .OrderByDescending(x => x["isDefault"]?.GetValue<bool>() == true).FirstOrDefault();
                id = pick?["id"]?.GetValue<int>();
            }

            var profiles = new List<(int, string)>();
            if (id is not null && await CallAsync(http, c, "service/" + service + "/" + id.Value.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false) is JsonObject d
                && d["profiles"] is JsonArray ps)
            {
                foreach (var p in ps.OfType<JsonObject>())
                {
                    if (p["id"] is JsonValue pid && pid.TryGetValue<int>(out var n) && p["name"]?.GetValue<string>() is { } name)
                    {
                        profiles.Add((n, name));
                    }
                }
            }

            if (profiles.Count > 0)
            {
                Services[key] = (DateTime.UtcNow, profiles, id);
            }

            return (profiles, id);
        }
        finally
        {
            Gate.Release();
        }
    }
}
