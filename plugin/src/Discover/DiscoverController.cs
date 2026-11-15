using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>
/// Die Schnittstelle von „Entdecken“ für das Theme. Jede Adresse gilt für das angemeldete Konto; die Antworten sind
/// JSON in camelCase. Kein Abruf wartet auf eine Berechnung, und kein Fehler erreicht Jellyfin als Ausnahme.
/// </summary>
[ApiController]
[Route("CinemaGlass/discover")]
public partial class DiscoverController : ControllerBase
{
    private const int MaxBody = 65536;

    private readonly DiscoverService _discover;
    private readonly ILogger<DiscoverController> _log;

    public DiscoverController(DiscoverService discover, ILogger<DiscoverController> log)
    {
        _discover = discover;
        _log = log;
    }

    [GeneratedRegex(@"^(genre:\d{1,6}|kw:\d{1,9}|decade:\d{4}|person:[^\u0000-\u001f]{1,80})$", RegexOptions.CultureInvariant)]
    private static partial Regex FeaturePattern();

    /// <summary>Die Seite „Entdecken“.</summary>
    [HttpGet("")]
    [Authorize]
    public ActionResult GetPage([FromQuery] string? type, [FromQuery] string? genre, [FromQuery] string? lang, [FromQuery] string? have)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        try
        {
            var kind = type is "movie" or "tv" ? type : "all";
            int.TryParse(genre, NumberStyles.None, CultureInfo.InvariantCulture, out var genreId);
            return Send(_discover.GetPage(user, Texts.Lang(lang), kind, genreId, have == "1"));
        }
        catch (Exception ex)
        {
            return Send(new PageDto { Status = "empty", Error = "none", Progress = 0 }, ex);
        }
    }

    /// <summary>Die eine Reihe für die Startseite.</summary>
    [HttpGet("home")]
    [Authorize]
    public async Task<ActionResult> GetHomeAsync([FromQuery] string? lang, CancellationToken ct)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        var language = Texts.Lang(lang);
        try
        {
            return Send(await _discover.GetHomeAsync(user, language, ct).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            var home = new HomeDto { Status = "empty", Title = Texts.Home(language) };
            home.Requests.Title = Texts.RequestsTitle(language);
            home.Arrived.Title = Texts.ArrivedTitle(language);
            return Send(home, ex);
        }
    }

    /// <summary>„Unterwegs zu dir“: Stand der angefragten Titel des Kontos.</summary>
    [HttpGet("requests")]
    [Authorize]
    public async Task<ActionResult> GetRequestsAsync([FromQuery] string? lang, CancellationToken ct)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        try
        {
            return Send(await _discover.GetRequestsAsync(user, Texts.Lang(lang), ct).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return Send(new RequestsDto { Status = "empty" }, ex);
        }
    }

    /// <summary>„Könnte dir auch gefallen“ zu einem Titel.</summary>
    [HttpGet("similar")]
    [Authorize]
    public async Task<ActionResult> GetSimilarAsync([FromQuery] string? key, [FromQuery] string? lang, CancellationToken ct)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        if (!MediaKey.TryParse(key, out var media))
        {
            return BadRequest();
        }

        try
        {
            return Send(await _discover.SimilarAsync(user, Texts.Lang(lang), media, ct).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return Send(new SimilarDto { Status = "empty" }, ex);
        }
    }

    /// <summary>Rückmeldungen zu Titeln: gefällt mir, nicht meins, kein Interesse, zurücknehmen.</summary>
    [HttpPost("feedback")]
    [Authorize]
    public async Task<ActionResult> PostFeedbackAsync(CancellationToken ct)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        try
        {
            var input = await ReadAsync<FeedbackInput>(ct).ConfigureAwait(false);
            if (input is null || (input.Items?.Count ?? 0) > 500)
            {
                return BadRequest();
            }

            if (!DiscoverService.Enabled)
            {
                return Send(new OkDto { Ok = false, Error = "off" });
            }

            return Send(_discover.Feedback(user, input));
        }
        catch (Exception ex)
        {
            return Send(new OkDto { Ok = false, Error = "none" }, ex);
        }
    }

    /// <summary>Titel für den Geschmacks-Check.</summary>
    [HttpGet("taste")]
    [Authorize]
    public async Task<ActionResult> GetTasteAsync([FromQuery] string? lang, [FromQuery] string? groups, CancellationToken ct)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        try
        {
            return Send(await _discover.TasteAsync(user, Texts.Lang(lang), groups is { Length: <= 400 } ? groups : null, ct).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return Send(new TasteDto(), ex);
        }
    }

    /// <summary>Die im Geschmacks-Check gewählten „Welten“ des Kontos; eine leere Liste löscht die Wahl.</summary>
    [HttpPost("taste/groups")]
    [Authorize]
    public async Task<ActionResult> PostTasteGroupsAsync(CancellationToken ct)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        try
        {
            var input = await ReadAsync<GroupsInput>(ct).ConfigureAwait(false);
            if (input is null || (input.Features?.Count ?? 0) > 64)
            {
                return BadRequest();
            }

            if (!DiscoverService.Enabled)
            {
                return Send(new OkDto { Ok = false, Error = "off" });
            }

            return Send(_discover.SetWorlds(user, input.Features));
        }
        catch (Exception ex)
        {
            return Send(new OkDto { Ok = false, Error = "none" }, ex);
        }
    }

    /// <summary>„Dein Geschmack“: woraus die Empfehlungen entstehen.</summary>
    [HttpGet("profile")]
    [Authorize]
    public ActionResult GetProfile([FromQuery] string? lang)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        try
        {
            return Send(_discover.GetProfile(user, Texts.Lang(lang)));
        }
        catch (Exception ex)
        {
            return Send(new ProfileDto(), ex);
        }
    }

    /// <summary>Ein Merkmal aus dem Geschmack herausnehmen oder wieder hinein.</summary>
    [HttpPost("profile/mute")]
    [Authorize]
    public async Task<ActionResult> PostMuteAsync(CancellationToken ct)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        try
        {
            var input = await ReadAsync<MuteInput>(ct).ConfigureAwait(false);
            var feature = input?.Feature?.Trim().ToLowerInvariant();
            if (feature is null || !FeaturePattern().IsMatch(feature))
            {
                return BadRequest();
            }

            if (!DiscoverService.Enabled)
            {
                return Send(new OkDto { Ok = false, Error = "off" });
            }

            return Send(new OkDto { Ok = _discover.Mute(user, feature, input!.Muted) });
        }
        catch (Exception ex)
        {
            return Send(new OkDto { Ok = false, Error = "none" }, ex);
        }
    }

    /// <summary>Titel, die das Konto auf der Seite gesehen hat.</summary>
    [HttpPost("impressions")]
    [Authorize]
    public async Task<ActionResult> PostImpressionsAsync(CancellationToken ct)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        try
        {
            var input = await ReadAsync<ImpressionsInput>(ct).ConfigureAwait(false);
            if (input?.Keys is null || input.Keys.Count > 600)
            {
                return BadRequest();
            }

            if (!DiscoverService.Enabled)
            {
                return Send(new OkDto { Ok = false, Error = "off" });
            }

            _discover.Impressions(user, input.Keys);
            return Send(new OkDto { Ok = true });
        }
        catch (Exception ex)
        {
            return Send(new OkDto { Ok = false, Error = "none" }, ex);
        }
    }

    /// <summary>Für Administratoren: wie die Empfehlungen eines Kontos zustande kommen.</summary>
    /// <summary>Wann das Konto welche Filme gesehen hat (für „Nach dem Schauen bewerten“).</summary>
    [HttpGet("watched")]
    [Authorize]
    public ActionResult GetWatched()
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        return Send(new { items = _discover.Watched(user).Select(x => new { itemId = x.Key, at = x.Value }).ToList() });
    }

    /// <summary>Eigene Anfrage zurückziehen (Download abbrechen, aus Radarr/Sonarr und Seerr entfernen), solange keine Datei da ist.</summary>
    [HttpPost("withdraw")]
    [Authorize]
    public async Task<ActionResult> PostWithdrawAsync([FromQuery] string? key, [FromQuery] string? lang, CancellationToken ct)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        if (!MediaKey.TryParse(key, out var media))
        {
            return BadRequest();
        }

        try
        {
            return Send(await _discover.WithdrawAsync(user, media, Texts.Lang(lang) != "en", ct).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return Send(new WithdrawDto { Status = "error" }, ex);
        }
    }

    [HttpGet("debug")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult GetDebug([FromQuery] string? userId, [FromQuery] string? lang, [FromQuery] string? build, [FromQuery] string? top, [FromQuery] string? find, [FromQuery] string? requests, [FromQuery] string? at, [FromQuery] string? refresh)
    {
        Guid user;
        if (string.IsNullOrEmpty(userId))
        {
            if (!TryUser(out user))
            {
                return Unauthorized();
            }
        }
        else if (!Guid.TryParse(userId, out user))
        {
            return BadRequest();
        }

        try
        {
            if (requests is "1" or "true")
            {
                // Stand der Anfragen statt der Empfehlungen; at: gestellte Uhrzeit des Servers, z. B. 2026-01-31T23:30;
                // refresh: Die Termine der angefragten Titel werden beim nächsten Abruf von discover/requests neu geholt.
                DateTime? moment = DateTime.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
                return Send(_discover.DebugRequests(user, Texts.Lang(lang), moment, refresh is "1" or "true"));
            }

            // top: wie viele der besten Kandidaten (Voreinstellung 40); find: stattdessen Kandidaten, deren Titel den Text enthält.
            var count = int.TryParse(top, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 40;
            return Send(_discover.Debug(user, Texts.Lang(lang), build is "1" or "true", count, find is { Length: <= 60 } ? find : null));
        }
        catch (Exception ex)
        {
            return Send(new { status = "empty", error = "none" }, ex);
        }
    }

    /// <summary>
    /// Für Administratoren: misst die Empfehlungen eines Kontos an seinen jüngsten Signalen (zurückgehalten, dann gesucht).
    /// Läuft im Hintergrund; derselbe Abruf liefert erst den Fortschritt, dann das Ergebnis. Ändert nichts am Konto.
    /// </summary>
    [HttpGet("eval")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult GetEval([FromQuery] string? userId, [FromQuery] string? lang, [FromQuery] int? holdout, [FromQuery] int? folds, [FromQuery] string? set, [FromQuery] string? restart, [FromQuery] string? watched) =>
        Eval(userId, lang, holdout, folds, set, false, restart, watched);

    /// <summary>Für Administratoren: dieselbe Messung für eine Reihe von Varianten der Gewichte, als Tabelle.</summary>
    [HttpGet("eval/sweep")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult GetEvalSweep([FromQuery] string? userId, [FromQuery] string? lang, [FromQuery] int? holdout, [FromQuery] int? folds, [FromQuery] string? restart, [FromQuery] string? watched) =>
        Eval(userId, lang, holdout, folds, null, true, restart, watched);

    private ActionResult Eval(string? userId, string? lang, int? holdout, int? folds, string? set, bool sweep, string? restart, string? watched)
    {
        Guid user;
        if (string.IsNullOrEmpty(userId))
        {
            if (!TryUser(out user))
            {
                return Unauthorized();
            }
        }
        else if (!Guid.TryParse(userId, out user))
        {
            return BadRequest();
        }

        if (set is { Length: > 300 })
        {
            return BadRequest();
        }

        try
        {
            return Send(_discover.Eval(user, Texts.Lang(lang), holdout ?? 10, folds ?? 1, set, sweep, restart is "1" or "true", watched is "1" or "true"));
        }
        catch (Exception ex)
        {
            return Send(new { status = "empty", error = "none" }, ex);
        }
    }

    private bool TryUser(out Guid user) => Guid.TryParse(User.FindFirst("Jellyfin-UserId")?.Value, out user) && user != Guid.Empty;

    private async Task<T?> ReadAsync<T>(CancellationToken ct)
        where T : class
    {
        if (Request.ContentLength > MaxBody)
        {
            return null;
        }

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var buffer = new char[MaxBody + 1];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = await reader.ReadAsync(buffer.AsMemory(length, buffer.Length - length), ct).ConfigureAwait(false)) > 0)
        {
            length += read;
        }

        if (length == 0 || length > MaxBody)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(buffer.AsSpan(0, length), DiscoverJson.Input);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private ActionResult Send(object value, Exception? failure = null)
    {
        if (failure is not null and not OperationCanceledException)
        {
            // Nur eine Zeile: das Theme bekommt eine leere Antwort, Jellyfin keine Ausnahme.
            _log.LogError("Cinema Glass Entdecken: Abruf fehlgeschlagen ({Type}: {Message})", failure.GetType().Name, failure.Message);
        }

        Response.Headers["Cache-Control"] = "no-store";
        return File(JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), DiscoverJson.Options), "application/json; charset=utf-8");
    }
}
