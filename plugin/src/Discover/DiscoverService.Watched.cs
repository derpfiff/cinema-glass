using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>
/// Wann ein Konto einen Film gesehen hat – zu Ende geschaut oder abgehakt, auch wenn eine App eine Offline-Wiedergabe
/// später nachmeldet. „Nach dem Schauen bewerten“ fragt nur kurz danach, nie nach Älterem. Kleine Datei neben dem Zustand.
/// </summary>
public sealed partial class DiscoverService
{
    private const int WatchedKeep = 200;
    private static readonly TimeSpan WatchedMaxAge = TimeSpan.FromDays(60);
    private readonly object _watchedLock = new();
    private Dictionary<string, Dictionary<string, long>>? _watched;

    private string WatchedFile => Path.Combine(Folder(), "watched.json");

    private void NoteWatched(UserDataSaveEventArgs e)
    {
        // zu Ende geschaut oder abgehakt – beides heißt „gesehen“; gefragt wird kurz danach
        if (e.SaveReason is not (UserDataSaveReason.PlaybackFinished or UserDataSaveReason.TogglePlayed) || e.Item is not Movie || e.UserData is not { Played: true })
        {
            return;
        }

        try
        {
            lock (_watchedLock)
            {
                var all = LoadWatched();
                var user = e.UserId.ToString("N");
                if (!all.TryGetValue(user, out var mine))
                {
                    mine = all[user] = new Dictionary<string, long>();
                }

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                mine[e.Item.Id.ToString("N")] = now;
                var cut = now - (long)WatchedMaxAge.TotalSeconds;
                foreach (var old in mine.Where(x => x.Value < cut).Select(x => x.Key).ToList())
                {
                    mine.Remove(old);
                }

                foreach (var extra in mine.OrderByDescending(x => x.Value).Skip(WatchedKeep).Select(x => x.Key).ToList())
                {
                    mine.Remove(extra);
                }

                Directory.CreateDirectory(Folder());
                var tmp = WatchedFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(all));
                File.Move(tmp, WatchedFile, true);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Gesehen-Liste ließ sich nicht schreiben");
        }
    }

    /// <summary>Zu Ende geschaute Filme des Kontos: Jellyfin-Id → Zeitpunkt (Unix-Sekunden).</summary>
    internal Dictionary<string, long> Watched(Guid user)
    {
        lock (_watchedLock)
        {
            return LoadWatched().TryGetValue(user.ToString("N"), out var mine) ? new Dictionary<string, long>(mine) : new Dictionary<string, long>();
        }
    }

    private Dictionary<string, Dictionary<string, long>> LoadWatched()
    {
        if (_watched is not null)
        {
            return _watched;
        }

        try
        {
            _watched = File.Exists(WatchedFile) ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, long>>>(File.ReadAllText(WatchedFile)) : null;
        }
        catch (Exception)
        {
            _watched = null;
        }

        return _watched ??= new Dictionary<string, Dictionary<string, long>>();
    }
}
