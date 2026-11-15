using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>
/// Letterboxd in „Entdecken“: eigene Bewertungen zählen als Signale (siehe <see cref="Profile.BuildSignals"/>), und was
/// Freunde gut fanden, steht in der Reihe „Von deinen Freunden“. Nur mit dem Schalter <c>Letterboxd</c>.
/// </summary>
public sealed partial class DiscoverService
{
    private const int FriendsRowSize = 18;
    private const int FriendsRowMin = 4;

    private Letterboxd _letterboxd = null!;

    private void InitLetterboxd(IHttpClientFactory http)
    {
        _letterboxd = new Letterboxd(http, Folder, _log);
        _store.LetterboxdOwn = _letterboxd.Own;
        _builder.FriendKeys = user => Letterboxd.On ? _letterboxd.GoodKeys(_store.View(user).Letterboxd) : Array.Empty<MediaKey>();
    }

    internal LetterboxdDto LetterboxdStatus(Guid user) => _letterboxd.Status(user, _store.View(user).Letterboxd);

    /// <summary>Verknüpft ein Letterboxd-Konto (leerer Name = Verknüpfung lösen) und gleicht im Hintergrund ab.</summary>
    internal LetterboxdDto LetterboxdSave(Guid user, LetterboxdInput input)
    {
        var name = Letterboxd.CleanName(input.User);
        var invalid = new List<string>();
        var friends = new List<string>();
        foreach (var part in (input.Friends ?? string.Empty).Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var friend = Letterboxd.CleanName(part);
            if (friend is null)
            {
                invalid.Add(part.Length > 40 ? part[..40] : part);
            }
            else if (friend != name && !friends.Contains(friend) && friends.Count < Letterboxd.MaxManual)
            {
                friends.Add(friend);
            }
        }

        if (name is null && !string.IsNullOrWhiteSpace(input.User))
        {
            var bad = _letterboxd.Status(user, _store.View(user).Letterboxd);
            bad.Error = "name";
            return bad;
        }

        _store.Update(user, s =>
        {
            if (name is null)
            {
                if (s.Letterboxd is null)
                {
                    return false;
                }

                s.Letterboxd = null;
                return true;
            }

            var old = s.Letterboxd;
            if (old is not null && old.User == name && old.Only == input.Only && old.Friends.SequenceEqual(friends))
            {
                return false;
            }

            s.Letterboxd = new LetterboxdLink { User = name, Friends = friends, Only = input.Only };
            return true;
        });

        var link = _store.View(user).Letterboxd;
        if (link is not null)
        {
            _letterboxd.Start(user, link, false, LetterboxdChanged, _stop.Token);
        }

        // Eigene Einträge zählen sofort (oder nicht mehr), ohne auf den Abgleich zu warten.
        LetterboxdChanged(user);
        var dto = _letterboxd.Status(user, link);
        if (invalid.Count > 0)
        {
            dto.Invalid = invalid;
        }

        return dto;
    }

    /// <summary>„Jetzt abgleichen“.</summary>
    internal LetterboxdDto LetterboxdSync(Guid user)
    {
        var link = _store.View(user).Letterboxd;
        if (link is not null)
        {
            _letterboxd.Start(user, link, true, LetterboxdChanged, _stop.Token);
        }

        return _letterboxd.Status(user, link);
    }

    /// <summary>Neue Einträge: sofort neu ordnen; Titel der Freunde brauchen Details, dafür im Hintergrund neu berechnen.</summary>
    private void LetterboxdChanged(Guid user)
    {
        foreach (var session in SessionsOf(user))
        {
            Rerank(session);
            if (Enabled && Letterboxd.On)
            {
                session.Pending = true;
                EnsureBuild(session, "letterboxd");
            }
        }
    }

    /// <summary>Fügt der Seite die Reihe „Von deinen Freunden“ hinzu und stößt bei Bedarf einen Abgleich an.</summary>
    private void AddFriendsRow(Guid user, Snapshot snap, PageDto page, string type, int genre)
    {
        if (!Letterboxd.On || page.Status != "ready")
        {
            return;
        }

        var link = snap.State.Letterboxd;
        if (link is null || link.User.Length == 0)
        {
            return;
        }

        if (_letterboxd.Due(user, link))
        {
            _letterboxd.Start(user, link, false, LetterboxdChanged, _stop.Token);
        }

        try
        {
            InsertFriendsRow(page, snap, _letterboxd.Votes(link), type, genre);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Die Seite kommt auch ohne diese Reihe.
            _log.LogDebug("Cinema Glass Letterboxd: Reihe fehlgeschlagen ({Type}: {Message})", ex.GetType().Name, ex.Message);
        }
    }

    /// <summary>
    /// Die Reihe „Von deinen Freunden“: was Freunde mit 3,5 Sternen oder mehr bewertet oder mit Herz markiert haben und das
    /// Konto weder gesehen noch abgelehnt hat. Gewertet halb nach den Freunden (wie viele, wie gut, wie frisch), halb nach
    /// dem eigenen Geschmack (Übereinstimmung aus der Rangfolge). Steht gleich hinter den zehn Besten; ihre Titel fallen
    /// aus den späteren Reihen heraus, damit nichts doppelt erscheint.
    /// </summary>
    internal static void InsertFriendsRow(PageDto page, Snapshot snap, Dictionary<MediaKey, List<FriendVote>> votes, string type, int genre)
    {
        if (votes.Count == 0)
        {
            return;
        }

        var exclude = new HashSet<MediaKey>();
        foreach (var row in page.Rows.Where(r => r.Id is "top10" or "requests"))
        {
            foreach (var item in row.Items)
            {
                if (MediaKey.TryParse(item.Key, out var key))
                {
                    exclude.Add(key);
                }
            }
        }

        var now = DateTime.UtcNow;
        var picks = new List<(Scored S, float Value, List<FriendVote> Good)>();
        foreach (var v in votes)
        {
            if (exclude.Contains(v.Key) || snap.State.Feedback.ContainsKey(v.Key) || snap.State.LetterboxdOwn.ContainsKey(v.Key))
            {
                continue;
            }

            // Nur, was das Konto noch nicht kennt: fehlend und erschienen, oder in der Bibliothek und unberührt.
            if (!snap.ByKey.TryGetValue(v.Key, out var s) || s.Upcoming || (s.Lib is null && !s.Missing))
            {
                continue;
            }

            if ((type != "all" && (type == "tv") != s.Key.Tv) || (genre > 0 && !GenreMap.Matches(s.Genres, genre)))
            {
                continue;
            }

            float positive = 0f, negative = 0f;
            var good = new List<FriendVote>();
            foreach (var vote in v.Value)
            {
                var e = vote.Entry;
                var days = e.When is { } when ? Math.Max(0, (now - when).TotalDays) : 365;
                var recency = 0.5f + (0.5f * (float)Math.Pow(2, -days / 180.0));
                if (Letterboxd.Good(e))
                {
                    // 3,5 Sterne → 0,4, 5 Sterne → 1; ein Herz zählt etwas dazu, ein Herz allein wie gut 4 Sterne.
                    var value = e.Rating > 0f ? (e.Rating - 2.5f) / 2.5f : 0.55f;
                    positive += (value + (e.Like ? 0.3f : 0f)) * recency;
                    good.Add(vote);
                }
                else if (e.Rating is > 0f and <= 2f)
                {
                    negative += (3f - e.Rating) / 2.5f * recency;
                }
            }

            if (good.Count == 0)
            {
                continue;
            }

            var friends = 1f - MathF.Exp(-Math.Max(0f, positive - (0.5f * negative)) / 1.2f);
            var taste = Math.Clamp((s.Match - 60) / 39f, 0f, 1f);
            picks.Add((s, (0.5f * friends) + (0.5f * taste), good));
        }

        if (picks.Count < FriendsRowMin)
        {
            return;
        }

        var lang = snap.Lang;
        var friendsRow = new RowDto
        {
            Id = "friends",
            Kind = "poster",
            Title = lang == "en" ? "From your friends" : "Von deinen Freunden",
        };
        foreach (var (s, _, good) in picks.OrderByDescending(p => p.Value).ThenBy(p => p.S.Key.Id).Take(FriendsRowSize))
        {
            var item = Pages.Item(s, snap, "friends");
            item.Reason = FriendsReason(good);
            item.ReasonKind = "friends";
            friendsRow.Items.Add(item);
        }

        // Gleich hinter den zehn Besten und „Unterwegs zu dir“.
        var at = 0;
        for (var i = 0; i < page.Rows.Count; i++)
        {
            if (page.Rows[i].Id is "top10" or "requests")
            {
                at = i + 1;
            }
        }

        var taken = new HashSet<string>(friendsRow.Items.Select(i => i.Key), StringComparer.Ordinal);
        for (var i = page.Rows.Count - 1; i >= at; i--)
        {
            var row = page.Rows[i];
            if (row.Items.RemoveAll(item => taken.Contains(item.Key)) > 0 && row.Items.Count < FriendsRowMin)
            {
                page.Rows.RemoveAt(i);
            }
        }

        page.Rows.Insert(Math.Min(at, page.Rows.Count), friendsRow);
    }

    /// <summary>„Lisa ★★★★½ · Tom ♥ · +2“: die zwei stärksten Stimmen, dann wie viele noch.</summary>
    internal static string FriendsReason(List<FriendVote> good)
    {
        var sorted = good.OrderByDescending(v => v.Entry.Rating + (v.Entry.Like ? 0.75f : 0f)).ThenByDescending(v => v.Entry.Watched).ToList();
        var parts = new List<string>(3);
        foreach (var v in sorted.Take(2))
        {
            // Auf der Karte ist wenig Platz: nur der Vorname („Lisa“), der Letterboxd-Name bleibt, wie er ist.
            var first = v.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? v.User;
            var text = new StringBuilder(Texts.Shorten(first, 16));
            if (v.Entry.Rating > 0f)
            {
                text.Append(' ').Append(Stars(v.Entry.Rating));
            }

            if (v.Entry.Like)
            {
                text.Append(' ').Append('♥');
            }

            parts.Add(text.ToString());
        }

        if (sorted.Count > 2)
        {
            parts.Add("+" + (sorted.Count - 2).ToString(CultureInfo.InvariantCulture));
        }

        return string.Join(" · ", parts);
    }

    internal static string Stars(float rating)
    {
        var halves = (int)MathF.Round(Math.Clamp(rating, 0f, 5f) * 2f);
        return new string('★', halves / 2) + (halves % 2 == 1 ? "½" : string.Empty);
    }
}
