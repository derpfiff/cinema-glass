using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>Übereinstimmung einzelner Titel, etwa der Treffer zum Anfragen in der Suche.</summary>
internal sealed class MatchDto
{
    /// <summary>ready, building, empty.</summary>
    public string Status { get; set; } = "ready";

    public double Confidence { get; set; }

    /// <summary>Schlüssel (<c>movie:603</c>) → Übereinstimmung in Prozent.</summary>
    public Dictionary<string, int> Items { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Die Suche zeigt Titel, die (noch) nicht in der Mediathek liegen. Sie werden wie „Könnte dir auch gefallen“ gegen das
/// Profil des Kontos bewertet: Genres, Personen, Jahrzehnt und Qualität aus den Details von Seerr; was die Empfehlungen
/// des Kontos ohnehin nennen, bringt seine Stütze mit.
/// </summary>
public sealed partial class DiscoverService
{
    /// <summary>Höchstens so viele Titel je Abruf.</summary>
    private const int MatchLimit = 24;

    /// <summary>Stütze für Titel, die keine Empfehlung des Kontos nennt: wie eine schwache Empfehlung eines gemochten Titels.</summary>
    private const float MatchSupport = 1f;

    internal async Task<MatchDto> MatchAsync(Guid user, string lang, IReadOnlyList<MediaKey> keys, CancellationToken ct)
    {
        var dto = new MatchDto();
        if (!Enabled || keys.Count == 0)
        {
            dto.Status = "empty";
            return dto;
        }

        var session = Touch(user, lang);
        var snap = session.Snap ?? AnySnapshot(user, lang);
        if (snap is null)
        {
            EnsureBuild(session, "match");
            dto.Status = "building";
            return dto;
        }

        dto.Confidence = Math.Round(snap.Profile.Alpha, 2);
        var wanted = keys.Distinct().Take(MatchLimit).ToList();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await _builder.DetailsAsync(wanted.Select(k => (k, true)).ToList(), lang, null, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Was bis dahin da ist, wird bewertet; der Rest beim nächsten Mal.
        }

        foreach (var key in wanted)
        {
            if (snap.ByKey.TryGetValue(key, out var ranked) && ranked.Match > 0)
            {
                dto.Items[Key(key)] = ranked.Match;
                continue;
            }

            var title = _catalog.FromDetail(lang, key) ?? _catalog.Get(lang, key);
            if (title is null || title.Adult)
            {
                continue;
            }

            dto.Items[Key(key)] = Scorer.ScoreLoose(snap, title, _catalog, user, MatchSupport).Match;
        }

        return dto;

        static string Key(MediaKey k) => (k.Tv ? "tv:" : "movie:") + k.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
