using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CinemaGlass.Web;

/// <summary>
/// Hängt Stylesheet und Skript in die Startseite der Weboberfläche (index.html) und trägt das Wiedergabe-Plugin des Themes in
/// deren Plugin-Liste (config.json) ein, ohne Dateien auf der Platte zu ändern.
/// Jeder Fehler führt dazu, dass die Antwort unverändert ausgeliefert wird.
/// </summary>
public class InjectStartupFilter : IStartupFilter
{
    /// <summary>Name, unter dem das Theme sein Wiedergabe-Plugin in <c>window</c> bereitstellt.</summary>
    private const string BridgeName = "CinemaGlassBridge";

    // Steht vor allen Skripten der Weboberfläche: die Plugin-Liste wird beim Start geladen, das Skript des Themes erst danach.
    // Die Funktion liefert die Klasse, sobald das Theme sie meldet; kommt nichts, nach vier Sekunden ein Platzhalter ohne Wirkung,
    // damit der Start der Weboberfläche nie daran hängt.
    private const string BridgeLoader = "<script>window." + BridgeName + "=function(){return new Promise(function(r){var d=false,f=function(c){if(!d){d=true;r(c);}};"
        + "window.__cinemaGlassBridge=f;if(window.__cinemaGlassBridgeClass){f(window.__cinemaGlassBridgeClass);}"
        + "setTimeout(function(){f(function(){this.name='Cinema Glass';this.type='cinemaglass';this.id='cinemaglassbridge';});},4000);});};</script>";

    private readonly ILogger<InjectStartupFilter> _logger;

    public InjectStartupFilter(ILogger<InjectStartupFilter> logger) => _logger = logger;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                var isConfig = IsWebConfig(context.Request);
                if ((!isConfig && !IsIndex(context.Request)) || Plugin.Instance is null)
                {
                    await nextMiddleware().ConfigureAwait(false);
                    return;
                }

                var cfg = Plugin.Instance.Configuration;
                var bridge = cfg.EnableClient && cfg.NativeApp;
                if (isConfig ? !bridge : (!cfg.EnableTheme && !cfg.EnableClient))
                {
                    await nextMiddleware().ConfigureAwait(false);
                    return;
                }

                // unkomprimiert und vollständig holen (kein 304, kein gzip), sonst lässt sich nichts einfügen
                context.Request.Headers.Remove("Accept-Encoding");
                context.Request.Headers.Remove("If-None-Match");
                context.Request.Headers.Remove("If-Modified-Since");
                context.Request.Headers.Remove("Range");

                var original = context.Response.Body;
                await using var buffer = new MemoryStream();
                context.Response.Body = buffer;
                try
                {
                    await nextMiddleware().ConfigureAwait(false);
                }
                finally
                {
                    context.Response.Body = original;
                }

                buffer.Position = 0;
                if (isConfig)
                {
                    await WriteConfigAsync(context, buffer, original).ConfigureAwait(false);
                    return;
                }

                var isHtml = context.Response.StatusCode == 200
                    && (context.Response.ContentType ?? string.Empty).Contains("text/html", StringComparison.OrdinalIgnoreCase)
                    && !context.Response.Headers.ContainsKey("Content-Encoding");
                if (!isHtml)
                {
                    await buffer.CopyToAsync(original).ConfigureAwait(false);
                    return;
                }

                byte[] output;
                try
                {
                    var html = Encoding.UTF8.GetString(buffer.ToArray());
                    var v = Plugin.Instance.Version + "-" + Plugin.Instance.ConfigurationStamp();
                    if (bridge && !html.Contains("window." + BridgeName + "=", StringComparison.Ordinal))
                    {
                        html = InsertAfterHeadStart(html, BridgeLoader);
                    }

                    if (cfg.EnableTheme && !html.Contains("CinemaGlass/theme.css", StringComparison.Ordinal))
                    {
                        // ans Ende des Dokuments: so steht das Stylesheet hinter allen Stilen der Weboberfläche und gewinnt bei gleicher Gewichtung
                        html = InsertBefore(html, "</body>", "<link rel=\"stylesheet\" href=\"../CinemaGlass/theme.css?v=" + v + "\">");
                    }

                    if (cfg.EnableClient && !html.Contains("CinemaGlass/client.js", StringComparison.Ordinal))
                    {
                        html = InsertBefore(html, "</body>", "<script defer src=\"../CinemaGlass/client.js?v=" + v + "\"></script>");
                    }

                    output = Encoding.UTF8.GetBytes(html);
                    context.Response.Headers.Remove("ETag");
                    context.Response.Headers.Remove("Last-Modified");
                    context.Response.Headers["Cache-Control"] = "no-cache";
                    context.Response.ContentLength = output.Length;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Cinema Glass: Einbindung fehlgeschlagen, Seite wird unverändert ausgeliefert");
                    buffer.Position = 0;
                    await buffer.CopyToAsync(original).ConfigureAwait(false);
                    return;
                }

                await original.WriteAsync(output).ConfigureAwait(false);
            });
            next(app);
        };
    }

    /// <summary>Ergänzt die Plugin-Liste in config.json um das Wiedergabe-Plugin des Themes.</summary>
    private async Task WriteConfigAsync(HttpContext context, MemoryStream buffer, Stream original)
    {
        var usable = context.Response.StatusCode == 200 && !context.Response.Headers.ContainsKey("Content-Encoding");
        byte[]? output = null;
        if (usable)
        {
            try
            {
                var root = JsonNode.Parse(Encoding.UTF8.GetString(buffer.ToArray())) as JsonObject;
                if (root is not null && root["plugins"] is JsonArray plugins)
                {
                    if (!plugins.Any(p => p is JsonValue v && v.TryGetValue<string>(out var name) && name == BridgeName))
                    {
                        plugins.Add(BridgeName);
                    }

                    output = Encoding.UTF8.GetBytes(root.ToJsonString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cinema Glass: config.json ließ sich nicht ergänzen und wird unverändert ausgeliefert");
                output = null;
            }
        }

        if (output is null)
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(original).ConfigureAwait(false);
            return;
        }

        context.Response.Headers.Remove("ETag");
        context.Response.Headers.Remove("Last-Modified");
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.ContentLength = output.Length;
        await original.WriteAsync(output).ConfigureAwait(false);
    }

    private static bool IsWebConfig(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method))
        {
            return false;
        }

        var path = (request.PathBase.Value ?? string.Empty) + (request.Path.Value ?? string.Empty);
        return path.EndsWith("/web/config.json", StringComparison.OrdinalIgnoreCase);
    }

    private static string InsertAfterHeadStart(string html, string snippet)
    {
        var i = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            return html;
        }

        var end = html.IndexOf('>', i);
        return end < 0 ? html : html.Insert(end + 1, snippet);
    }

    private static bool IsIndex(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method))
        {
            return false;
        }

        var path = (request.PathBase.Value ?? string.Empty) + (request.Path.Value ?? string.Empty);
        return path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web", StringComparison.OrdinalIgnoreCase);
    }

    private static string InsertBefore(string html, string marker, string snippet)
    {
        var i = html.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return i < 0 ? html : html.Insert(i, snippet);
    }
}
