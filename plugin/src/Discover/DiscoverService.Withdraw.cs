using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>Ergebnis von „Anfrage zurückziehen“.</summary>
internal sealed class WithdrawDto
{
    /// <summary>ok, notyours, there, file, asleep, off, error.</summary>
    public string Status { get; set; } = "ok";

    public string? Message { get; set; }

    /// <summary>Abgebrochene Downloads.</summary>
    public int Downloads { get; set; }
}

/// <summary>
/// Eine eigene Anfrage zurückziehen: Downloads abbrechen, Titel aus Radarr/Sonarr nehmen, Anfrage und Eintrag in Seerr löschen.
/// Nur für Anfragen, die das Konto selbst über Cinema Glass gestellt hat, und nur solange noch keine Datei da ist – so kann
/// nichts verloren gehen, was schon in der Mediathek liegt. Zugang zu Radarr/Sonarr kommt aus den Einstellungen von Seerr.
/// </summary>
public sealed partial class DiscoverService
{
    private IHttpClientFactory? _withdrawHttp;

    private void InitWithdraw(IHttpClientFactory http) => _withdrawHttp = http;

    internal async Task<WithdrawDto> WithdrawAsync(Guid user, MediaKey key, bool german, CancellationToken ct)
    {
        string T(string de, string en) => german ? de : en;
        var c = Plugin.Instance?.Configuration;
        if (c is null || !c.Seerr || c.SeerrUrl.Length == 0 || c.SeerrApiKey.Length == 0 || _withdrawHttp is null)
        {
            return new WithdrawDto { Status = "off", Message = T("Seerr ist nicht eingerichtet.", "Seerr is not set up.") };
        }

        // Gehört die Anfrage diesem Konto? (Seerr kennt nur sein festes Konto; wer wollte, weiß das Theme.)
        var info = await _requests.GetAsync(TimeSpan.Zero, TimeSpan.FromSeconds(8), ct).ConfigureAwait(false);
        var own = RequestOwnership.For(user, info, _store.View(user), _store.ThemeRequests());
        if (!own.TryGetValue(key, out var mine) || mine.Requests.Count == 0)
        {
            return new WithdrawDto { Status = "notyours", Message = T("Zurückziehen geht nur bei eigenen Anfragen.", "You can only withdraw your own requests.") };
        }

        var http = _withdrawHttp.CreateClient("CinemaGlass");
        var seerr = c.SeerrUrl.TrimEnd('/') + "/api/v1/";
        using var media = await JsonAsync(http, HttpMethod.Get, seerr + key.Type + "/" + key.Id.ToString(CultureInfo.InvariantCulture), "X-Api-Key", c.SeerrApiKey, ct).ConfigureAwait(false);
        if (media is null || !media.RootElement.TryGetProperty("mediaInfo", out var mi) || mi.ValueKind != JsonValueKind.Object)
        {
            return new WithdrawDto { Status = "error", Message = T("Seerr kennt diese Anfrage nicht mehr.", "Seerr no longer knows this request.") };
        }

        var mediaId = Int(mi, "id");
        if (Int(mi, "status") == 5)
        {
            return new WithdrawDto { Status = "there", Message = T("Der Titel ist schon da.", "This title is already here.") };
        }

        var result = new WithdrawDto();
        var serviceId = Int(mi, "serviceId");
        var external = Int(mi, "externalServiceId");
        if (external > 0)
        {
            // Zugang zum Dienst aus den Einstellungen von Seerr
            using var services = await JsonAsync(http, HttpMethod.Get, seerr + "settings/" + (key.Tv ? "sonarr" : "radarr"), "X-Api-Key", c.SeerrApiKey, ct).ConfigureAwait(false);
            JsonElement? svc = null;
            if (services?.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in services.RootElement.EnumerateArray())
                {
                    if (Int(s, "id") == serviceId || (svc is null && s.TryGetProperty("isDefault", out var d) && d.ValueKind == JsonValueKind.True))
                    {
                        svc = s;
                        if (Int(s, "id") == serviceId)
                        {
                            break;
                        }
                    }
                }
            }

            if (svc is not { } sv)
            {
                return new WithdrawDto { Status = "error", Message = T("Den Download-Dienst finde ich in Seerr nicht.", "Could not find the download service in Seerr.") };
            }

            var ssl = sv.TryGetProperty("useSsl", out var us) && us.ValueKind == JsonValueKind.True;
            var baseUrl = sv.TryGetProperty("baseUrl", out var bu) && bu.ValueKind == JsonValueKind.String ? bu.GetString() ?? string.Empty : string.Empty;
            var api = (ssl ? "https://" : "http://") + Str(sv, "hostname") + ":" + Int(sv, "port").ToString(CultureInfo.InvariantCulture) + baseUrl.TrimEnd('/') + "/api/v3/";
            var arrKey = Str(sv, "apiKey");
            var path = (key.Tv ? "series/" : "movie/") + external.ToString(CultureInfo.InvariantCulture);

            // Schon eine Datei da? Dann nicht anfassen.
            using (var item = await JsonAsync(http, HttpMethod.Get, api + path, "X-Api-Key", arrKey, ct).ConfigureAwait(false))
            {
                if (item is null)
                {
                    return new WithdrawDto { Status = "asleep", Message = T("Der Download-Dienst antwortet gerade nicht (Ruhezeit?). Bitte später nochmal.", "The download service isn't answering right now. Try again later.") };
                }

                var root = item.RootElement;
                var files = key.Tv
                    ? (root.TryGetProperty("statistics", out var st) ? Int(st, "episodeFileCount") : 0)
                    : (root.TryGetProperty("hasFile", out var hf) && hf.ValueKind == JsonValueKind.True ? 1 : 0);
                if (files > 0)
                {
                    return new WithdrawDto { Status = "file", Message = T("Es liegt schon eine Datei vor – zurückziehen geht nicht mehr.", "A file is already there – it can't be withdrawn anymore.") };
                }
            }

            // Laufende Downloads abbrechen (aus dem Download-Programm entfernen, nicht sperren)
            using (var queue = await JsonAsync(http, HttpMethod.Get, api + "queue?pageSize=500", "X-Api-Key", arrKey, ct).ConfigureAwait(false))
            {
                if (queue is not null && queue.RootElement.TryGetProperty("records", out var recs) && recs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in recs.EnumerateArray())
                    {
                        if (Int(r, key.Tv ? "seriesId" : "movieId") != external)
                        {
                            continue;
                        }

                        using var gone = await JsonAsync(http, HttpMethod.Delete, api + "queue/" + Int(r, "id").ToString(CultureInfo.InvariantCulture) + "?removeFromClient=true&blocklist=false", "X-Api-Key", arrKey, ct, allowEmpty: true).ConfigureAwait(false);
                        result.Downloads++;
                    }
                }
            }

            using var removed = await JsonAsync(http, HttpMethod.Delete, api + path + (key.Tv ? "?deleteFiles=false&addImportListExclusion=false" : "?deleteFiles=false&addImportExclusion=false"), "X-Api-Key", arrKey, ct, allowEmpty: true).ConfigureAwait(false);
            if (removed is null)
            {
                return new WithdrawDto { Status = "error", Message = T("Der Titel ließ sich nicht aus dem Download-Dienst entfernen.", "Could not remove the title from the download service."), Downloads = result.Downloads };
            }
        }

        // Anfragen und Eintrag in Seerr löschen
        if (mi.TryGetProperty("requests", out var reqs) && reqs.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in reqs.EnumerateArray())
            {
                using var _ = await JsonAsync(http, HttpMethod.Delete, seerr + "request/" + Int(r, "id").ToString(CultureInfo.InvariantCulture), "X-Api-Key", c.SeerrApiKey, ct, allowEmpty: true).ConfigureAwait(false);
            }
        }

        if (mediaId > 0)
        {
            using var _ = await JsonAsync(http, HttpMethod.Delete, seerr + "media/" + mediaId.ToString(CultureInfo.InvariantCulture), "X-Api-Key", c.SeerrApiKey, ct, allowEmpty: true).ConfigureAwait(false);
        }

        // Vermerk des Themes weg; die Empfehlungen dürfen den Titel wieder zeigen
        _store.Update(user, s => s.Requests.Remove(key.ToString()));
        _requests.Forget();
        foreach (var session in SessionsOf(user))
        {
            Rerank(session);
        }

        _log.LogInformation("Anfrage zurückgezogen: {Key} ({Downloads} Downloads abgebrochen)", key, result.Downloads);
        return result;
    }

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    // Einfacher Aufruf mit Schlüssel; null bei Fehler. allowEmpty: leere Antwort (204/200 ohne Inhalt) zählt als Erfolg.
    private static async Task<JsonDocument?> JsonAsync(HttpClient http, HttpMethod method, string url, string header, string key, CancellationToken ct, bool allowEmpty = false)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(20));
            using var msg = new HttpRequestMessage(method, url);
            msg.Headers.TryAddWithoutValidation(header, key);
            msg.Headers.Accept.ParseAdd("application/json");
            using var res = await http.SendAsync(msg, limit.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                return null;
            }

            var text = await res.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                return allowEmpty ? JsonDocument.Parse("{}") : null;
            }

            return JsonDocument.Parse(text);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
