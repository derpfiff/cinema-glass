using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CinemaGlass.Web;

/// <summary>
/// Hängt Stylesheet und Skript in die Startseite der Weboberfläche (index.html), ohne Dateien auf der Platte zu ändern.
/// Jeder Fehler führt dazu, dass die Seite unverändert ausgeliefert wird.
/// </summary>
public class InjectStartupFilter : IStartupFilter
{
    private readonly ILogger<InjectStartupFilter> _logger;

    public InjectStartupFilter(ILogger<InjectStartupFilter> logger) => _logger = logger;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (!IsIndex(context.Request) || Plugin.Instance is null)
                {
                    await nextMiddleware().ConfigureAwait(false);
                    return;
                }

                var cfg = Plugin.Instance.Configuration;
                if (!cfg.EnableTheme && !cfg.EnableClient)
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
