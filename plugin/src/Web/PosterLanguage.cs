using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CinemaGlass.Web;

/// <summary>
/// „Plakate nach Originalsprache“: Neue Filme und Serien bekommen bei deutscher Originalsprache das deutsche Plakat, bei
/// englischer das englische Original, sonst das deutsche (fehlt es, das englische). Sammlungen deutsch. Die Originalsprache
/// kommt aus Seerr. Läuft zehn Minuten nach dem Hinzufügen, wenn Jellyfin die Metadaten hat; ohne Treffer bleibt das Plakat.
/// </summary>
public sealed class PosterLanguage : IHostedService, IDisposable
{
    private static readonly TimeSpan Delay = TimeSpan.FromMinutes(10);
    private readonly ILibraryManager _library;
    private readonly IProviderManager _providers;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<PosterLanguage> _log;
    private readonly ConcurrentDictionary<Guid, (DateTime Due, int Tries)> _queue = new();
    private readonly CancellationTokenSource _stop = new();
    private Timer? _timer;
    private int _busy;

    public PosterLanguage(ILibraryManager library, IProviderManager providers, IHttpClientFactory http, ILogger<PosterLanguage> log)
    {
        _library = library;
        _providers = providers;
        _http = http;
        _log = log;
    }

    private static bool On => Plugin.Instance?.Configuration is { PosterLanguage: true } c && c.SeerrUrl.Length > 0 && c.SeerrApiKey.Length > 0;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _library.ItemAdded += OnAdded;
        _timer = new Timer(_ => _ = TickAsync(), null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _library.ItemAdded -= OnAdded;
        _stop.Cancel();
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _stop.Dispose();
    }

    private void OnAdded(object? sender, ItemChangeEventArgs e)
    {
        if (On && e.Item is Movie or Series or BoxSet && e.Item.LocationType != LocationType.Remote)
        {
            _queue[e.Item.Id] = (DateTime.UtcNow + Delay, 0);
        }
    }

    private async Task TickAsync()
    {
        if (_queue.IsEmpty || !On || Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            foreach (var (id, entry) in _queue.Where(q => q.Value.Due <= now).ToList())
            {
                if (_stop.IsCancellationRequested)
                {
                    return;
                }

                var done = await ApplyAsync(id, _stop.Token).ConfigureAwait(false);
                if (done || entry.Tries >= 3)
                {
                    _queue.TryRemove(id, out _);
                }
                else
                {
                    _queue[id] = (now + Delay, entry.Tries + 1);   // Metadaten noch nicht da: später nochmal
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Plakate nach Originalsprache");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>true = erledigt (gesetzt oder nichts Passendes), false = später nochmal versuchen.</summary>
    private async Task<bool> ApplyAsync(Guid id, CancellationToken ct)
    {
        var item = _library.GetItemById(id);
        if (item is null)
        {
            return true;
        }

        string lang = "de";
        if (item is not BoxSet)
        {
            var tmdb = item.GetProviderId(MetadataProvider.Tmdb);
            if (string.IsNullOrEmpty(tmdb) || !int.TryParse(tmdb, NumberStyles.None, CultureInfo.InvariantCulture, out var tmdbId))
            {
                return false;
            }

            lang = await LanguageAsync(item is Series, tmdbId, ct).ConfigureAwait(false) ?? string.Empty;
            if (lang.Length == 0)
            {
                return false;
            }
        }

        var want = lang == "de" ? new[] { "de" } : lang == "en" ? new[] { "en" } : new[] { "de", "en" };
        var images = (await _providers.GetAvailableRemoteImages(item, new RemoteImageQuery(string.Empty) { ImageType = ImageType.Primary, IncludeAllLanguages = true, IncludeDisabledProviders = true }, ct).ConfigureAwait(false)).ToList();
        foreach (var w in want)
        {
            var pick = images.Where(i => string.Equals(i.Language, w, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(i => i.VoteCount ?? 0).ThenByDescending(i => i.CommunityRating ?? 0).FirstOrDefault();
            if (pick is null)
            {
                continue;
            }

            await _providers.SaveImage(item, pick.Url, ImageType.Primary, null, ct).ConfigureAwait(false);
            await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, ct).ConfigureAwait(false);
            _log.LogInformation("Plakat für {Name}: {Lang} (Originalsprache {Orig})", item.Name, w, lang);
            return true;
        }

        return true;
    }

    private async Task<string?> LanguageAsync(bool tv, int id, CancellationToken ct)
    {
        var c = Plugin.Instance!.Configuration;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            using var msg = new HttpRequestMessage(HttpMethod.Get, c.SeerrUrl.TrimEnd('/') + "/api/v1/" + (tv ? "tv/" : "movie/") + id.ToString(CultureInfo.InvariantCulture));
            msg.Headers.TryAddWithoutValidation("X-Api-Key", c.SeerrApiKey);
            using var res = await _http.CreateClient("CinemaGlass").SendAsync(msg, cts.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                return null;
            }

            return JsonNode.Parse(await res.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false))?["originalLanguage"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
