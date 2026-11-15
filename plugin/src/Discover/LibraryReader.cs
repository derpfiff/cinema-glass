using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>
/// Liest aus Jellyfin, was ein Konto in der Bibliothek hat und was es davon gesehen oder mit Herz versehen hat.
/// Nur echte Bibliotheken zählen: Titel aus Kanälen (etwa einer Mediathek) sind flüchtig und sagen nichts über den Bestand.
/// </summary>
internal sealed class LibraryReader
{
    /// <summary>Personen werden nur für so viele Titel nachgeschlagen; bei riesigen Bibliotheken wäre die Abfrage sonst schwer.</summary>
    private const int MaxPeopleItems = 600;

    private static readonly BaseItemKind[] Kinds = { BaseItemKind.Movie, BaseItemKind.Series };

    private readonly ILibraryManager _library;
    private readonly IUserDataManager _userData;

    public LibraryReader(ILibraryManager library, IUserDataManager userData)
    {
        _library = library;
        _userData = userData;
    }

    public LibrarySnapshot Read(User user)
    {
        var snapshot = new LibrarySnapshot();
        var now = DateTime.UtcNow;

        // Erst alles ohne Rücksicht auf das Konto: was auf dem Server liegt, aber für dieses Konto gesperrt ist, soll weder
        // als fehlend empfohlen noch als Signal gewertet werden.
        var all = _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = Kinds, Recursive = true }, false);
        var visibleIds = new HashSet<Guid>(_library.GetItemIds(new InternalItemsQuery(user) { IncludeItemTypes = Kinds, Recursive = true }));

        var visible = new List<BaseItem>();
        var byItem = new Dictionary<Guid, LibEntry>();
        foreach (var item in all)
        {
            if (item.ChannelId != Guid.Empty || item.IsVirtualItem || item.ExtraType.HasValue || item.OwnerId != Guid.Empty)
            {
                continue;
            }

            var tv = item is Series;
            if (!tv && item is not Movie)
            {
                continue;
            }

            if (!int.TryParse(item.GetProviderId(MetadataProvider.Tmdb), NumberStyles.None, CultureInfo.InvariantCulture, out var tmdb) || tmdb <= 0)
            {
                continue;
            }

            var key = new MediaKey(tv, tmdb);
            var isVisible = visibleIds.Contains(item.Id);
            if (snapshot.Items.TryGetValue(key, out var known) && (known.Visible || !isVisible))
            {
                // Derselbe Titel mehrfach (zwei Fassungen als eigene Einträge): der erste sichtbare gilt.
                if (isVisible)
                {
                    byItem[item.Id] = known;
                    visible.Add(item);
                }

                continue;
            }

            int.TryParse(item.GetProviderId(MetadataProvider.TmdbCollection), NumberStyles.None, CultureInfo.InvariantCulture, out var collection);
            var entry = new LibEntry
            {
                Key = key,
                ItemId = item.Id,
                Name = item.Name ?? string.Empty,
                Year = item.ProductionYear ?? item.PremiereDate?.Year ?? 0,
                Visible = isVisible,
                RuntimeMinutes = item.RunTimeTicks is > 0 ? (int)Math.Round(item.RunTimeTicks.Value / (double)TimeSpan.TicksPerMinute) : 0,
                Genres = item.Genres ?? Array.Empty<string>(),
                Collection = tv ? 0 : collection,
                Added = item.DateCreated,
            };
            snapshot.Items[key] = entry;
            if (isVisible)
            {
                byItem[item.Id] = entry;
                visible.Add(item);
                if (tv)
                {
                    snapshot.Series++;
                }
                else
                {
                    snapshot.Movies++;
                }
            }
        }

        if (visible.Count > 0)
        {
            ReadUserData(user, visible, byItem);
            ReadSeries(user, visible, byItem, now);
            ReadPeople(visible, byItem);
        }

        snapshot.Fingerprint = Fingerprint(snapshot);
        return snapshot;
    }

    /// <summary>Nur die Nummern der vorhandenen Titel, für Abrufe ohne fertige Berechnung.</summary>
    public HashSet<MediaKey> Keys()
    {
        var keys = new HashSet<MediaKey>();
        foreach (var item in _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = Kinds, Recursive = true }, false))
        {
            if (item.ChannelId == Guid.Empty && !item.IsVirtualItem
                && int.TryParse(item.GetProviderId(MetadataProvider.Tmdb), NumberStyles.None, CultureInfo.InvariantCulture, out var tmdb) && tmdb > 0)
            {
                keys.Add(new MediaKey(item is Series, tmdb));
            }
        }

        return keys;
    }

    /// <summary>
    /// Schlägt gezielt nach, ob bestimmte Titel für das Konto in der Bibliothek liegen und was es davon gesehen hat; bei
    /// Serien je Staffel. Für den Stand angefragter Titel: wenige Titel, dafür oft und genau.
    /// </summary>
    public Dictionary<MediaKey, LibEntry> Find(User user, IReadOnlyCollection<MediaKey> keys)
    {
        var found = new Dictionary<MediaKey, LibEntry>();
        foreach (var tv in new[] { false, true })
        {
            var ids = keys.Where(k => k.Tv == tv).Select(k => k.Id.ToString(CultureInfo.InvariantCulture)).Distinct().ToArray();
            if (ids.Length == 0)
            {
                continue;
            }

            var items = _library.GetItemList(
                new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[] { tv ? BaseItemKind.Series : BaseItemKind.Movie },
                    Recursive = true,
                    HasAnyProviderIds = new Dictionary<string, string[]> { [MetadataProvider.Tmdb.ToString()] = ids },
                },
                false);
            var wanted = items.Where(i => i.ChannelId == Guid.Empty && !i.IsVirtualItem && !i.ExtraType.HasValue).ToList();
            if (wanted.Count == 0)
            {
                continue;
            }

            var data = _userData.GetUserDataBatch(wanted, user);
            foreach (var item in wanted)
            {
                if (!int.TryParse(item.GetProviderId(MetadataProvider.Tmdb), NumberStyles.None, CultureInfo.InvariantCulture, out var tmdb))
                {
                    continue;
                }

                var key = new MediaKey(tv, tmdb);
                if (found.ContainsKey(key) || !keys.Contains(key))
                {
                    continue;
                }

                var entry = new LibEntry
                {
                    Key = key,
                    ItemId = item.Id,
                    Name = item.Name ?? string.Empty,
                    Year = item.ProductionYear ?? item.PremiereDate?.Year ?? 0,
                    Visible = true,
                    RuntimeMinutes = item.RunTimeTicks is > 0 ? (int)Math.Round(item.RunTimeTicks.Value / (double)TimeSpan.TicksPerMinute) : 0,
                    Genres = item.Genres ?? Array.Empty<string>(),
                    Added = item.DateCreated,
                };
                if (data.TryGetValue(item.Id, out var d) && d is not null)
                {
                    entry.Favorite = d.IsFavorite;
                    if (!tv)
                    {
                        entry.Played = d.Played;
                        entry.PlayCount = d.PlayCount;
                        if (!d.Played && d.PlaybackPositionTicks > 0)
                        {
                            entry.Progress = item.RunTimeTicks is > 0 ? (float)Math.Clamp(d.PlaybackPositionTicks / (double)item.RunTimeTicks.Value, 0.001, 1.0) : 0.001f;
                        }
                    }
                }

                if (tv)
                {
                    ReadSeasons(user, item, entry);
                }

                found[key] = entry;
            }
        }

        return found;
    }

    private void ReadSeasons(User user, BaseItem series, LibEntry entry)
    {
        var seasons = new Dictionary<int, SeasonHave>();
        var episodes = _library.GetItemList(
            new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                AncestorIds = new[] { series.Id },
                Recursive = true,
                IsVirtualItem = false,
            },
            false);
        if (episodes.Count > 0)
        {
            var data = _userData.GetUserDataBatch(episodes, user);
            foreach (var item in episodes)
            {
                if (item is not Episode episode || episode.IsVirtualItem || episode.ParentIndexNumber is not { } number)
                {
                    continue;
                }

                // Eine Datei kann mehrere Folgen enthalten (S01E01-E02).
                var count = episode.IndexNumber is { } from && episode.IndexNumberEnd is { } to && to > from ? to - from + 1 : 1;
                var played = data.TryGetValue(item.Id, out var d) && d is { Played: true } ? count : 0;
                seasons.TryGetValue(number, out var have);
                seasons[number] = new SeasonHave(have.Count + count, have.Played + played, item.DateCreated > have.Newest ? item.DateCreated : have.Newest);
                entry.EpisodesTotal += count;
                entry.EpisodesPlayed += played;
            }
        }

        entry.SeasonsHave = seasons;
        entry.Seasons = seasons.Keys.Count(n => n > 0);
    }

    private void ReadUserData(User user, List<BaseItem> visible, Dictionary<Guid, LibEntry> byItem)
    {
        var data = _userData.GetUserDataBatch(visible, user);
        foreach (var item in visible)
        {
            if (!data.TryGetValue(item.Id, out var d) || d is null || !byItem.TryGetValue(item.Id, out var entry))
            {
                continue;
            }

            // Mehrere Einträge desselben Titels: gesehen oder mit Herz versehen gilt, sobald es für einen gilt.
            entry.Favorite |= d.IsFavorite;
            if (item is Movie)
            {
                entry.Played |= d.Played;
                entry.PlayCount = Math.Max(entry.PlayCount, d.PlayCount);
                if (!d.Played && d.PlaybackPositionTicks > 0 && item.RunTimeTicks is > 0)
                {
                    entry.Progress = Math.Max(entry.Progress, (float)Math.Clamp(d.PlaybackPositionTicks / (double)item.RunTimeTicks.Value, 0.001, 1.0));
                }
            }

            if (d.LastPlayedDate.HasValue && (!entry.LastPlayed.HasValue || d.LastPlayedDate > entry.LastPlayed))
            {
                entry.LastPlayed = d.LastPlayedDate;
            }
        }
    }

    private void ReadSeries(User user, List<BaseItem> visible, Dictionary<Guid, LibEntry> byItem, DateTime now)
    {
        var seriesIds = visible.Where(i => i is Series).Select(i => i.Id).ToList();
        if (seriesIds.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var count in _library.GetPlayedAndTotalCountBatch(seriesIds, user))
            {
                if (byItem.TryGetValue(count.Key, out var entry))
                {
                    entry.EpisodesPlayed = Math.Max(entry.EpisodesPlayed, count.Value.Played);
                    entry.EpisodesTotal = Math.Max(entry.EpisodesTotal, count.Value.Total);
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ohne Zählung gelten Serien als ungesehen; die Seite entsteht trotzdem.
        }

        try
        {
            // Staffeln ohne Specials, für „Schon da · 3 Staffeln“ und die Frage, ob eine Serie vollständig ist.
            foreach (var item in _library.GetItemList(new InternalItemsQuery(user) { IncludeItemTypes = new[] { BaseItemKind.Season }, Recursive = true }, false))
            {
                if (item is Season season && !season.IsVirtualItem && season.IndexNumber is > 0 && byItem.TryGetValue(season.SeriesId, out var entry))
                {
                    entry.Seasons++;
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Die Zahl der Staffeln ist nur Beiwerk.
        }

        try
        {
            // Wann zuletzt geschaut wurde, steht an den Folgen. Die jüngsten genügen: Was hier fehlt, liegt lange zurück.
            var episodes = _library.GetItemList(
                new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    Recursive = true,
                    IsPlayed = true,
                    OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                    Limit = 300,
                },
                false);
            if (episodes.Count > 0)
            {
                var data = _userData.GetUserDataBatch(episodes, user);
                foreach (var item in episodes)
                {
                    if (item is Episode episode && byItem.TryGetValue(episode.SeriesId, out var entry)
                        && data.TryGetValue(item.Id, out var d) && d?.LastPlayedDate is { } played && played <= now.AddDays(1)
                        && (!entry.LastPlayed.HasValue || played > entry.LastPlayed))
                    {
                        entry.LastPlayed = played;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ohne Datum zählen angefangene Serien wie lange zurückliegende.
        }
    }

    private void ReadPeople(List<BaseItem> visible, Dictionary<Guid, LibEntry> byItem)
    {
        try
        {
            // Zuerst alles mit starkem Signal, dann das zuletzt Hinzugefügte.
            var ids = visible
                .Where(i => byItem.ContainsKey(i.Id))
                .OrderByDescending(i => byItem[i.Id].Favorite || byItem[i.Id].Played || byItem[i.Id].EpisodesPlayed > 0)
                .ThenByDescending(i => i.DateCreated)
                .Take(MaxPeopleItems)
                .Select(i => i.Id)
                .ToList();
            foreach (var people in _library.GetPeopleByItems(ids))
            {
                if (!byItem.TryGetValue(people.Key, out var entry) || entry.Directors.Length > 0 || entry.Cast.Length > 0)
                {
                    continue;
                }

                var directors = new List<string>(2);
                var cast = new List<string>(5);
                foreach (var p in people.Value)
                {
                    if (string.IsNullOrWhiteSpace(p.Name))
                    {
                        continue;
                    }

                    if (p.Type is PersonKind.Director or PersonKind.Creator)
                    {
                        if (directors.Count < 3)
                        {
                            directors.Add(p.Name);
                        }
                    }
                    else if (p.Type is PersonKind.Actor && cast.Count < 5)
                    {
                        cast.Add(p.Name);
                    }
                }

                entry.Directors = directors.ToArray();
                entry.Cast = cast.ToArray();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ohne Personen bleiben Genres und Jahr.
        }
    }

    private static ulong Fingerprint(LibrarySnapshot snapshot)
    {
        // Unabhängig von der Reihenfolge: je Titel ein Wert, alle aufsummiert.
        ulong sum = (ulong)snapshot.Items.Count * 0x9E3779B97F4A7C15UL;
        foreach (var e in snapshot.Items.Values)
        {
            ulong h = 1469598103934665603UL;
            void Mix(long v)
            {
                h ^= (ulong)v;
                h *= 1099511628211UL;
            }

            Mix(e.Key.Id);
            Mix(e.Key.Tv ? 1 : 0);
            Mix(e.Visible ? 1 : 0);
            Mix(e.Played ? 1 : 0);
            Mix(Math.Min(e.PlayCount, 2));
            Mix(e.Favorite ? 1 : 0);
            Mix((long)(e.Progress * 20));
            Mix(e.EpisodesPlayed);
            Mix(e.EpisodesTotal);
            Mix(e.ItemId.GetHashCode());
            sum += h;
        }

        return sum;
    }
}
