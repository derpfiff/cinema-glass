using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.CinemaGlass.Web;

/// <summary>Liefert Stylesheet und Skript aus und reicht Seerr-/MDBList-Abrufe für angemeldete Nutzer weiter.</summary>
[ApiController]
[Route("CinemaGlass")]
public class CinemaGlassController : ControllerBase
{
    // Was angemeldete Nutzer bei Seerr abrufen dürfen. Alles andere wird abgelehnt.
    private static readonly Regex SeerrGet = new(
        @"^(api/v1/(status|search|tv/[^?]+|movie/[^?]+|person/[^?]+|service/(radarr|sonarr)(/\d+)?)|imageproxy/tmdb/[^?]+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SeerrPost = new(@"^api/v1/request$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, string> Cache = new();

    private readonly IHttpClientFactory _http;

    public CinemaGlassController(IHttpClientFactory http) => _http = http;

    private static string Resource(string name)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var hit))
            {
                return hit;
            }

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CinemaGlass." + name);
            if (stream is null)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return Cache[name] = reader.ReadToEnd();
        }
    }

    private static object Flags()
    {
        var c = Plugin.Instance!.Configuration;
        return new
        {
            version = Plugin.Instance.Version.ToString(3),
            features = new
            {
                hero = c.Hero,
                detailPage = c.DetailPage,
                homeCardPlay = c.HomeCardPlay,
                episodeMenu = c.EpisodeMenu,
                versionPicker = c.VersionPicker,
                trailerModal = c.TrailerModal,
                introTransition = c.IntroTransition,
                swipeBack = c.SwipeBack,
                pageGate = c.PageGate,
                downloads = c.Downloads,
                downloadProgress = c.DownloadProgress,
                nativeApp = c.NativeApp,
                updateCheck = c.UpdateCheck,
                seerr = c.Seerr && c.SeerrUrl.Length > 0 && c.SeerrApiKey.Length > 0,
                ratings = c.Ratings && c.MdbListApiKey.Length > 0
            }
        };
    }

    private void CacheLong()
    {
        // Die Adresse trägt Version und Einstellungsstempel, darf also lange im Cache bleiben.
        Response.Headers["Cache-Control"] = Request.Query.ContainsKey("v") ? "public, max-age=604800" : "no-cache";
    }

    [HttpGet("config")]
    [AllowAnonymous]
    public ActionResult GetConfig()
    {
        if (Plugin.Instance is null)
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = "no-cache";
        return new JsonResult(Flags());
    }

    [HttpGet("theme.css")]
    [AllowAnonymous]
    public ActionResult GetCss()
    {
        if (Plugin.Instance is null || !Plugin.Instance.Configuration.EnableTheme)
        {
            return Content("/* aus */", "text/css", Encoding.UTF8);
        }

        CacheLong();
        return Content(Resource("theme.css"), "text/css", Encoding.UTF8);
    }

    [HttpGet("client.js")]
    [AllowAnonymous]
    public ActionResult GetJs()
    {
        if (Plugin.Instance is null || !Plugin.Instance.Configuration.EnableClient)
        {
            return Content("/* aus */", "application/javascript", Encoding.UTF8);
        }

        var c = Plugin.Instance.Configuration;
        var sb = new StringBuilder();
        sb.Append("(function(){var g=").Append(JsonSerializer.Serialize(Flags()))
          .Append(";var s=document.currentScript;g.base=s&&s.src?s.src.replace(/\\/client\\.js.*$/,''):'';window.__cinemaGlass=g;})();\n");
        sb.Append(Resource("core.js")).Append("\n;\n");
        if (c.Seerr && c.SeerrUrl.Length > 0 && c.SeerrApiKey.Length > 0)
        {
            sb.Append(Resource("seerr.js")).Append("\n;\n");
        }

        if (c.Ratings && c.MdbListApiKey.Length > 0)
        {
            sb.Append(Resource("ratings.js")).Append("\n;\n");
        }

        CacheLong();
        return Content(sb.ToString(), "application/javascript", Encoding.UTF8);
    }

    [HttpGet("seerr/{**path}")]
    [Authorize]
    public Task<ActionResult> SeerrGetAsync([FromRoute] string path, CancellationToken ct) => SeerrAsync(HttpMethod.Get, path, SeerrGet, ct);

    [HttpPost("seerr/{**path}")]
    [Authorize]
    public Task<ActionResult> SeerrPostAsync([FromRoute] string path, CancellationToken ct) => SeerrAsync(HttpMethod.Post, path, SeerrPost, ct);

    private async Task<ActionResult> SeerrAsync(HttpMethod method, string path, Regex allowed, CancellationToken ct)
    {
        var c = Plugin.Instance?.Configuration;
        if (c is null || !c.Seerr || c.SeerrUrl.Length == 0 || c.SeerrApiKey.Length == 0)
        {
            return NotFound();
        }

        path = (path ?? string.Empty).TrimStart('/');
        if (path.Contains("..", StringComparison.Ordinal) || !allowed.IsMatch(path))
        {
            return NotFound();
        }

        var target = c.SeerrUrl.TrimEnd('/') + "/" + path + Request.QueryString.Value;
        using var msg = new HttpRequestMessage(method, target);
        msg.Headers.TryAddWithoutValidation("X-Api-Key", c.SeerrApiKey);
        if (c.SeerrUserId.Length > 0)
        {
            msg.Headers.TryAddWithoutValidation("X-Api-User", c.SeerrUserId);
        }

        msg.Headers.Accept.ParseAdd("*/*");
        if (method == HttpMethod.Post)
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            if (body.Length > 16384)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            msg.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await RelayAsync(msg, path.StartsWith("imageproxy/", StringComparison.OrdinalIgnoreCase), ct).ConfigureAwait(false);
    }

    [HttpGet("mdb/tmdb/{kind}/{id}")]
    [Authorize]
    public async Task<ActionResult> MdbAsync([FromRoute] string kind, [FromRoute] long id, CancellationToken ct)
    {
        var c = Plugin.Instance?.Configuration;
        if (c is null || !c.Ratings || c.MdbListApiKey.Length == 0 || (kind != "movie" && kind != "show"))
        {
            return NotFound();
        }

        var target = "https://api.mdblist.com/tmdb/" + kind + "/" + id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "?apikey=" + Uri.EscapeDataString(c.MdbListApiKey);
        using var msg = new HttpRequestMessage(HttpMethod.Get, target);
        msg.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await RelayAsync(msg, false, ct).ConfigureAwait(false);
    }

    private async Task<ActionResult> RelayAsync(HttpRequestMessage msg, bool cacheable, CancellationToken ct)
    {
        try
        {
            using var res = await _http.CreateClient("CinemaGlass").SendAsync(msg, ct).ConfigureAwait(false);
            var bytes = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var type = res.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            Response.StatusCode = (int)res.StatusCode;
            Response.Headers["Cache-Control"] = cacheable && res.IsSuccessStatusCode ? "private, max-age=86400" : "no-store";
            return File(bytes, type);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}
