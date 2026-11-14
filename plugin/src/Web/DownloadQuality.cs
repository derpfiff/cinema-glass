using System.Collections.Concurrent;
using System.Globalization;
using MediaBrowser.Model.Dlna;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.CinemaGlass.Web;

/// <summary>
/// Die Qualität, in der ein Konto einen Titel herunterladen möchte.
/// </summary>
/// <remarks>
/// Die Jellyfin-App fürs Handy kennt zwei Arten zu laden. Ohne ihre Einstellung „transkodierte Downloads“ lädt sie die
/// Adresse, die ihr die Weboberfläche nennt (darin steckt die gewählte Qualität), spielt die Datei danach aber nicht
/// selbst ab. Mit der Einstellung fragt sie den Server selbst, wie der Titel zu laden ist, und weiß nichts von der Wahl
/// im Theme. Das Theme meldet die Wahl deshalb vorab hier an, und <see cref="DownloadQualityFilter"/> setzt sie in der
/// Anfrage der App durch.
/// </remarks>
public static class DownloadQuality
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(48);
    private static readonly ConcurrentDictionary<(Guid User, Guid Item), (int Height, int VideoBitrate, int AudioBitrate, DateTime At)> Wishes = new();

    public static void Set(Guid user, Guid item, int height, int videoBitrate, int audioBitrate)
    {
        if (height <= 0)
        {
            Wishes.TryRemove((user, item), out _);
            return;
        }

        Wishes[(user, item)] = (height, videoBitrate, audioBitrate, DateTime.UtcNow);
        if (Wishes.Count > 4000)
        {
            foreach (var old in Wishes.Where(w => DateTime.UtcNow - w.Value.At > Lifetime).Select(w => w.Key).ToList())
            {
                Wishes.TryRemove(old, out _);
            }
        }
    }

    public static (int Height, int VideoBitrate, int AudioBitrate)? Get(Guid user, Guid item) =>
        Wishes.TryGetValue((user, item), out var wish) && DateTime.UtcNow - wish.At < Lifetime
            ? (wish.Height, wish.VideoBitrate, wish.AudioBitrate)
            : null;
}

/// <summary>Nimmt die Qualitätswahl des Themes für anstehende Downloads entgegen.</summary>
[ApiController]
[Route("CinemaGlass/download")]
public class DownloadQualityController : ControllerBase
{
    /// <summary>Die Wahl für einen oder mehrere Titel; Höhe 0 heißt Original und löscht eine frühere Wahl.</summary>
    public sealed class Wish
    {
        public Guid[] Ids { get; set; } = Array.Empty<Guid>();

        public int MaxHeight { get; set; }

        public int VideoBitrate { get; set; }

        public int AudioBitrate { get; set; }
    }

    [HttpPost("quality")]
    [Authorize]
    public ActionResult SetQuality([FromBody] Wish wish)
    {
        if (!Guid.TryParse(User.FindFirst("Jellyfin-UserId")?.Value, out var user))
        {
            return Unauthorized();
        }

        if (wish.Ids.Length is 0 or > 500 || wish.MaxHeight is < 0 or > 4320 || wish.VideoBitrate is < 0 or > 200_000_000 || wish.AudioBitrate is < 0 or > 2_000_000)
        {
            return BadRequest();
        }

        foreach (var id in wish.Ids)
        {
            DownloadQuality.Set(user, id, wish.MaxHeight, Math.Max(wish.VideoBitrate, 200_000), Math.Max(wish.AudioBitrate, 64_000));
        }

        return NoContent();
    }
}

/// <summary>
/// Fragt die Jellyfin-App für einen Download nach den Wiedergabe-Angaben eines Titels (ihr Geräteprofil heißt dann
/// „… Download Profile“) und liegt für Konto und Titel eine Qualitätswahl vor, trägt dieser Filter sie in die Anfrage
/// ein: Der Server rechnet dann um, statt das Original zu schicken. In der Antwort ersetzt er die Adresse durch
/// dieselbe, die das Theme ohne die Einstellung der App verwendet; beide Wege liefern damit dieselbe Datei. Alle anderen
/// Anfragen bleiben, wie sie sind.
/// </summary>
public sealed class DownloadQualityFilter : IAsyncActionFilter, IAsyncResultFilter
{
    private const string Key = "CinemaGlass.DownloadWish";

    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        try
        {
            if (context.HttpContext.Items.TryGetValue(Key, out var kept) && kept is ValueTuple<Guid, int, int, int> wish
                && context.Result is ObjectResult { Value: MediaBrowser.Model.MediaInfo.PlaybackInfoResponse info } && info.MediaSources is not null)
            {
                foreach (var source in info.MediaSources)
                {
                    if (string.IsNullOrEmpty(source.TranscodingUrl))
                    {
                        continue;
                    }

                    var cut = source.TranscodingUrl.IndexOf('?', StringComparison.Ordinal);
                    var old = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(cut >= 0 ? source.TranscodingUrl[(cut + 1)..] : string.Empty);
                    string Old(string name) => old.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value.ToString();
                    // Jellyfin begrenzt die Bildgröße nur über die Breite; die Höhe allein bleibt ohne Wirkung.
                    var width = (int)Math.Round(wish.Item2 * 16 / 9.0 / 2) * 2;
                    var query = new Dictionary<string, string?>
                    {
                        ["static"] = "false",
                        ["mediaSourceId"] = source.Id,
                        ["videoCodec"] = "h264",
                        ["audioCodec"] = "aac",
                        ["maxAudioChannels"] = "2",
                        ["videoBitRate"] = wish.Item3.ToString(CultureInfo.InvariantCulture),
                        ["audioBitRate"] = wish.Item4.ToString(CultureInfo.InvariantCulture),
                        ["maxHeight"] = wish.Item2.ToString(CultureInfo.InvariantCulture),
                        ["maxWidth"] = width.ToString(CultureInfo.InvariantCulture),
                        ["deviceId"] = Old("DeviceId"),
                        ["playSessionId"] = Old("PlaySessionId"),
                        ["ApiKey"] = Old("ApiKey"),
                    };
                    source.TranscodingUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/Videos/" + wish.Item1.ToString("N", CultureInfo.InvariantCulture) + "/stream.mp4", query);
                    source.TranscodingContainer = "mp4";
                    source.SupportsDirectPlay = false;
                    source.SupportsDirectStream = false;
                }
            }
        }
        catch (Exception)
        {
            // Dann bleibt die Antwort, wie Jellyfin sie gebaut hat.
        }

        return next();
    }

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            Apply(context);
        }
        catch (Exception)
        {
            // Im Zweifel bleibt die Anfrage unverändert; der Download läuft dann in der Qualität, die die App vorsieht.
        }

        return next();
    }

    private static void Apply(ActionExecutingContext context)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsPost(request.Method) || !request.Path.Value!.EndsWith("/PlaybackInfo", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var dto = context.ActionArguments.Values.FirstOrDefault(v => v is not null && v.GetType().Name == "PlaybackInfoDto");
        if (dto is null
            || dto.GetType().GetProperty("DeviceProfile")?.GetValue(dto) is not DeviceProfile profile
            || profile.Name is null || !profile.Name.Contains("Download Profile", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(context.RouteData.Values["itemId"]?.ToString(), out var item)
            || !Guid.TryParse(context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value, out var user)
            || DownloadQuality.Get(user, item) is not { } wish)
        {
            return;
        }

        context.HttpContext.Items[Key] = (item, wish.Height, wish.VideoBitrate, wish.AudioBitrate);
        var total = wish.VideoBitrate + wish.AudioBitrate;
        void Put(string name, object value) => dto.GetType().GetProperty(name)?.SetValue(dto, value);
        Put("EnableDirectPlay", false);
        Put("EnableDirectStream", false);
        Put("MaxStreamingBitrate", total);
        Put("MaxAudioChannels", 2);
        profile.MaxStreamingBitrate = total;
        profile.MaxStaticBitrate = total;
    }
}
