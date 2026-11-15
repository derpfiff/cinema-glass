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
