using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>Letterboxd-Verknüpfung des angemeldeten Kontos (nur mit dem Schalter <c>Letterboxd</c>).</summary>
public partial class DiscoverController
{
    /// <summary>Verknüpftes Letterboxd-Konto, Freunde und Stand des Abgleichs.</summary>
    [HttpGet("letterboxd")]
    [Authorize]
    public ActionResult GetLetterboxd()
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        try
        {
            return Send(_discover.LetterboxdStatus(user));
        }
        catch (Exception ex)
        {
            return Send(new LetterboxdDto { Enabled = Letterboxd.On, Error = "none" }, ex);
        }
    }

    /// <summary>Letterboxd-Namen und weitere Freunde speichern (leerer Name = Verknüpfung lösen); gleicht im Hintergrund ab.</summary>
    [HttpPost("letterboxd")]
    [Authorize]
    public async Task<ActionResult> PostLetterboxdAsync(CancellationToken ct)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        try
        {
            var input = await ReadAsync<LetterboxdInput>(ct).ConfigureAwait(false);
            if (input is null || input.User is { Length: > 200 } || input.Friends is { Length: > 4000 })
            {
                return BadRequest();
            }

            if (!Letterboxd.On)
            {
                return Send(new OkDto { Ok = false, Error = "off" });
            }

            return Send(_discover.LetterboxdSave(user, input));
        }
        catch (Exception ex)
        {
            return Send(new OkDto { Ok = false, Error = "none" }, ex);
        }
    }

    /// <summary>„Jetzt abgleichen“: holt, was veraltet ist (das eigene Tagebuch frühestens nach zehn Minuten erneut).</summary>
    /// <summary>Alle eigenen Bewertungen von Letterboxd (das Theme holt sie bei Jellyscribe) ins Profil übernehmen.</summary>
    [HttpPost("letterboxd/rated")]
    [Authorize]
    public async Task<ActionResult> PostLetterboxdRatedAsync(CancellationToken ct)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        try
        {
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(Request.Body, default, ct).ConfigureAwait(false);
            var list = new List<(MediaKey, float, bool)>();
            if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var it in items.EnumerateArray())
                {
                    if (!it.TryGetProperty("tmdbId", out var id) || !id.TryGetInt32(out var tmdb) || tmdb <= 0)
                    {
                        continue;
                    }

                    var rating = it.TryGetProperty("rating", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.Number ? r.GetSingle() : 0f;
                    var like = it.TryGetProperty("liked", out var l) && l.ValueKind == System.Text.Json.JsonValueKind.True;
                    list.Add((new MediaKey(false, tmdb), rating, like));
                }
            }

            return Send(_discover.ImportLetterboxdRated(user, list));
        }
        catch (Exception ex)
        {
            return Send(new LetterboxdDto { Enabled = Letterboxd.On, Error = "none" }, ex);
        }
    }

    [HttpPost("letterboxd/sync")]
    [Authorize]
    public ActionResult PostLetterboxdSync()
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        try
        {
            if (!Letterboxd.On)
            {
                return Send(new OkDto { Ok = false, Error = "off" });
            }

            return Send(_discover.LetterboxdSync(user));
        }
        catch (Exception ex)
        {
            return Send(new OkDto { Ok = false, Error = "none" }, ex);
        }
    }
}
