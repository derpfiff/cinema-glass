using System.Globalization;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>Alle Texte, die das Theme fertig vom Server bekommt, in Deutsch und Englisch.</summary>
internal static class Texts
{
    /// <summary>Begründungen sollen auf eine Karte passen.</summary>
    private const int ReasonLength = 45;

    private static readonly string[] MonthsDe = { "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember" };
    private static readonly string[] MonthsEn = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
    private static readonly string[] ShortMonthsDe = { "Jan.", "Feb.", "März", "Apr.", "Mai", "Juni", "Juli", "Aug.", "Sep.", "Okt.", "Nov.", "Dez." };
    private static readonly string[] ShortMonthsEn = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    public static string Lang(string? lang) => string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "de";

    public static string Rating(float rating, string lang) =>
        rating.ToString("0.0", CultureInfo.InvariantCulture).Replace(".", lang == "en" ? "." : ",", StringComparison.Ordinal);

    public static string Quote(string name, string lang) => lang == "en" ? "“" + name + "”" : "„" + name + "“";

    public static string Decade(int decade, string lang) => decade.ToString(CultureInfo.InvariantCulture) + (lang == "en" ? "s" : "er");

    public static string Role(bool directs, bool tv, string lang) => lang == "en"
        ? (directs ? (tv ? "Creator" : "Director") : "Cast")
        : (directs ? (tv ? "Idee" : "Regie") : "Darsteller");

    public static string Duration(int minutes, string lang)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        var inv = CultureInfo.InvariantCulture;
        if (lang == "en")
        {
            return h > 0 ? h.ToString(inv) + "h " + m.ToString(inv) + "m" : m.ToString(inv) + "m";
        }

        return h > 0 ? h.ToString(inv) + " Std. " + m.ToString(inv) + " Min." : m.ToString(inv) + " Min.";
    }

    public static string Seasons(int seasons, string lang) => lang == "en"
        ? seasons.ToString(CultureInfo.InvariantCulture) + (seasons == 1 ? " season" : " seasons")
        : seasons.ToString(CultureInfo.InvariantCulture) + (seasons == 1 ? " Staffel" : " Staffeln");

    /// <summary>„12. Dezember“; mit Jahr, wenn es nicht das laufende ist.</summary>
    public static string Day(int date, string lang, int currentYear)
    {
        var year = date / 10000;
        var month = Math.Clamp(date / 100 % 100, 1, 12);
        var day = date % 100;
        var inv = CultureInfo.InvariantCulture;
        var text = lang == "en"
            ? MonthsEn[month - 1] + (day > 0 ? " " + day.ToString(inv) : string.Empty)
            : (day > 0 ? day.ToString(inv) + ". " : string.Empty) + MonthsDe[month - 1];
        return year == currentYear ? text : text + (lang == "en" && day > 0 ? ", " : " ") + year.ToString(inv);
    }

    /// <summary>„8. Dez.“ für eine Zeile mit wenig Platz; mit Jahr erst, wenn der Tag so weit weg ist, dass der Monat allein täuschen könnte.</summary>
    public static string ShortDay(int date, string lang, int today)
    {
        var month = Math.Clamp(date / 100 % 100, 1, 12);
        var day = date % 100;
        var inv = CultureInfo.InvariantCulture;
        var text = lang == "en"
            ? ShortMonthsEn[month - 1] + (day > 0 ? " " + day.ToString(inv) : string.Empty)
            : (day > 0 ? day.ToString(inv) + ". " : string.Empty) + ShortMonthsDe[month - 1];
        var days = RequestTracker.DaysBetween(today, date);
        return days is > -300 and < 300 ? text : text + (lang == "en" && day > 0 ? ", " : " ") + (date / 10000).ToString(inv);
    }

    /// <summary>„am 8. Dez.“, „morgen“ oder „heute“, klein geschrieben für die Satzmitte.</summary>
    public static string OnDay(int date, string lang, int today)
    {
        var en = lang == "en";
        var days = RequestTracker.DaysBetween(today, date);
        return days <= 0 ? (en ? "today" : "heute")
            : days == 1 ? (en ? "tomorrow" : "morgen")
            : (en ? "on " : "am ") + ShortDay(date, lang, today);
    }

    // ---- Begründungen ------------------------------------------------------------------------------------------

    public static string Because(SignalKind kind, string name, string lang)
    {
        var (before, after) = kind switch
        {
            SignalKind.Favorite or SignalKind.Like => lang == "en" ? ("Because you like ", string.Empty) : ("Weil dir ", " gefällt"),
            SignalKind.Requested => lang == "en" ? ("Because you requested ", string.Empty) : ("Weil du ", " angefragt hast"),
            _ => lang == "en" ? ("Because you watched ", string.Empty) : ("Weil du ", " gesehen hast"),
        };
        return before + Quote(Shorten(name, ReasonLength - before.Length - after.Length - 2), lang) + after;
    }

    public static string Person(string name, bool directs, bool tv, string lang)
    {
        var before = lang == "en"
            ? (directs ? (tv ? "Created by " : "Directed by ") : "With ")
            : (directs ? (tv ? "Von " : "Regie: ") : "Mit ");
        return before + Shorten(name, ReasonLength - before.Length);
    }

    public static string GenreRating(string genre, float rating, string lang) =>
        rating > 0f ? genre + " · " + Rating(rating, lang) : genre;

    public static string Trending(string lang) => lang == "en" ? "Trending now" : "Gerade angesagt";

    public static string Classic(float rating, string lang) => (lang == "en" ? "Classic · " : "Klassiker · ") + Rating(rating, lang);

    public static string Gem(float rating, string lang) => (lang == "en" ? "Hidden gem · " : "Geheimtipp · ") + Rating(rating, lang);

    public static string Upcoming(int date, string lang, int currentYear) =>
        date > 0 ? (lang == "en" ? "From " : "Ab ") + Day(date, lang, currentYear) : (lang == "en" ? "Coming soon" : "Demnächst");

    public static string Explore(string lang) => lang == "en" ? "Something different" : "Mal was anderes";

    public static string InLibrary(string? extra, string lang) =>
        (lang == "en" ? "In your library" : "Schon da") + (string.IsNullOrEmpty(extra) ? string.Empty : " · " + extra);

    public static string Collection(string name, string lang)
    {
        var before = lang == "en" ? "Missing from " : "Fehlt dir aus ";
        return before + Quote(Shorten(name, ReasonLength - before.Length - 2), lang);
    }

    // ---- Stand angefragter Titel -------------------------------------------------------------------------------

    public static string RequestsTitle(string lang) => lang == "en" ? "On the way to you" : "Unterwegs zu dir";

    public static string ArrivedTitle(string lang) => lang == "en" ? "New for you" : "Neu für dich";

    /// <summary>„43 %“ – im Deutschen mit geschütztem Leerzeichen, damit die Zahl nicht vom Zeichen getrennt wird.</summary>
    public static string Percent(int percent, string lang) =>
        percent.ToString(CultureInfo.InvariantCulture) + (lang == "en" ? "%" : "\u00A0%");

    public static string Clock(DateTime local) => local.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Wann ein Download fertig ist: bis zu einer Stunde in Minuten, bis zwei Stunden grob in Stunden, darüber als Uhrzeit
    /// (auf fünf Minuten gerundet, in der Zeit des Servers).
    /// </summary>
    public static string Eta(DateTime etaLocal, DateTime nowLocal, string lang)
    {
        var en = lang == "en";
        var minutes = Math.Max(1, (int)Math.Ceiling((etaLocal - nowLocal).TotalMinutes));
        var inv = CultureInfo.InvariantCulture;
        if (minutes < 60)
        {
            return en ? minutes.ToString(inv) + " min left" : "noch " + minutes.ToString(inv) + " Min.";
        }

        if (minutes <= 120)
        {
            var hours = minutes < 90 ? 1 : 2;
            return en ? "about " + hours.ToString(inv) + (hours == 1 ? " hr left" : " hrs left") : "noch etwa " + hours.ToString(inv) + " Std.";
        }

        var rounded = new DateTime(etaLocal.Year, etaLocal.Month, etaLocal.Day, etaLocal.Hour, 0, 0).AddMinutes(Math.Round(etaLocal.Minute / 5.0) * 5);
        return DayWord(rounded, nowLocal, lang) + (en ? " around " : " gegen ") + Clock(rounded);
    }

    /// <summary>„heute“, „morgen“ oder das Datum, klein geschrieben für die Satzmitte.</summary>
    public static string DayWord(DateTime local, DateTime nowLocal, string lang)
    {
        var days = (local.Date - nowLocal.Date).Days;
        var en = lang == "en";
        return days <= 0 ? (en ? "today" : "heute")
            : days == 1 ? (en ? "tomorrow" : "morgen")
            : (en ? "on " : "am ") + Day((local.Year * 10000) + (local.Month * 100) + local.Day, lang, nowLocal.Year);
    }

    /// <summary>Der Stand als fertiger Satz, z. B. „Lädt · 43 % · noch 12 Min.“.</summary>
    public static string RequestText(RequestEntry e, string lang, DateTime nowLocal)
    {
        var en = lang == "en";
        var inv = CultureInfo.InvariantCulture;
        var today = Scorer.Today(nowLocal);
        switch (e.Phase)
        {
            case RequestPhase.Approval:
                return en ? "Waiting for approval" : "Wartet auf Freigabe";
            case RequestPhase.Searching:
                // Nach zwei Tagen ohne Fund klingt „Wird gesucht“ wie ein Hänger; dann lieber sagen, was Sache ist.
                return e.LongSearch
                    ? (en ? "Nothing found yet – still searching" : "Noch nichts gefunden – Suche läuft weiter")
                    : (en ? "Searching" : "Wird gesucht");
            case RequestPhase.Unreleased:
                return e.Wait switch
                {
                    WaitKind.HomeRelease => (en ? "Expected " : "Erscheint voraussichtlich ") + OnDay(e.WaitDate, lang, today),
                    WaitKind.CinemaSoon => en ? "In cinemas " + OnDay(e.WaitDate, lang, today) : "Kommt " + OnDay(e.WaitDate, lang, today) + " ins Kino",
                    WaitKind.InCinema => en ? "Still in cinemas" : "Noch im Kino",
                    WaitKind.SeasonStart => (en ? "Starts " : "Startet ") + OnDay(e.WaitDate, lang, today),
                    _ => en ? "Not started yet – date to be announced" : "Noch nicht gestartet – Termin offen",
                };
            case RequestPhase.Scheduled:
                return e.StartsAt is { } start
                    ? (en ? "Starts " : "Startet ") + DayWord(start, nowLocal, lang) + (en ? " around " : " gegen ") + Clock(start)
                    : (en ? "Starts later" : "Startet später");
            case RequestPhase.Queued:
                return en ? "Queued" : "In der Warteschlange";
            case RequestPhase.Downloading:
            {
                var text = e.Key.Tv && e.Files > 1
                    ? (en ? "Downloading " + e.Files.ToString(inv) + " episodes" : "Lädt " + e.Files.ToString(inv) + " Folgen")
                    : (en ? "Downloading" : "Lädt");
                if (e.Percent is { } percent)
                {
                    text += " · " + Percent(percent, lang);
                }

                if (e.Eta is { } eta)
                {
                    text += " · " + Eta(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(eta, DateTimeKind.Utc), TimeZoneInfo.Local), nowLocal, lang);
                }

                return text;
            }

            case RequestPhase.Importing:
                return en ? "Being added to the library" : "Wird eingeordnet";
            case RequestPhase.Partial:
            {
                var text = en ? "Partly here" : "Teilweise da";
                if (e.Expected > 0)
                {
                    text = e.Have.ToString(inv) + (en ? " of " : " von ") + e.Expected.ToString(inv) + (en ? " episodes here" : " Folgen da");
                    if (e.PartialSeason > 0)
                    {
                        text = (en ? "Season " : "Staffel ") + e.PartialSeason.ToString(inv) + " · " + text;
                    }
                }

                // Läuft die Staffel noch, sagt der Termin der nächsten Folge, wann es weitergeht.
                return e.AvailableFrom > 0 ? text + " · " + (en ? "next " : "nächste ") + OnDay(e.AvailableFrom, lang, today) : text;
            }

            case RequestPhase.Available:
                return en ? "Here – watch now" : "Da – jetzt ansehen";
            case RequestPhase.Declined:
                return en ? "Declined" : "Abgelehnt";
            default:
                return en ? "Failed" : "Fehlgeschlagen";
        }
    }

    /// <summary>Sehr kurze Fassung für eine Marke auf der Karte („43 %“, „Gesucht“, „Morgen“, „Neu“).</summary>
    public static string RequestShort(RequestEntry e, string lang, DateTime nowLocal)
    {
        var en = lang == "en";
        var inv = CultureInfo.InvariantCulture;
        switch (e.Phase)
        {
            case RequestPhase.Approval:
                return en ? "Pending" : "Freigabe";
            case RequestPhase.Searching:
                return en ? "Searching" : "Gesucht";
            case RequestPhase.Unreleased:
            {
                if (e.Wait is WaitKind.CinemaSoon or WaitKind.InCinema)
                {
                    return en ? "In cinemas" : "Im Kino";
                }

                if (e.WaitDate <= 0)
                {
                    return en ? "Announced" : "Angekündigt";
                }

                var today = Scorer.Today(nowLocal);
                var days = RequestTracker.DaysBetween(today, e.WaitDate);
                return days <= 0 ? (en ? "Today" : "Heute") : days == 1 ? (en ? "Tomorrow" : "Morgen") : ShortDay(e.WaitDate, lang, today);
            }

            case RequestPhase.Scheduled:
            {
                var word = e.StartsAt is { } start ? DayWord(start, nowLocal, lang) : (en ? "later" : "später");
                return word.Length > 0 && !word.Contains(' ', StringComparison.Ordinal) ? char.ToUpperInvariant(word[0]) + word[1..] : (en ? "Later" : "Später");
            }

            case RequestPhase.Queued:
                return en ? "Queued" : "Wartet";
            case RequestPhase.Downloading:
                return e.Percent is { } percent ? Percent(percent, lang) : (en ? "Loading" : "Lädt");
            case RequestPhase.Importing:
                return en ? "Almost" : "Gleich da";
            case RequestPhase.Partial:
                return e.Expected > 0 ? e.Have.ToString(inv) + "/" + e.Expected.ToString(inv) : (en ? "Partly" : "Teils");
            case RequestPhase.Available:
                return en ? "New" : "Neu";
            case RequestPhase.Declined:
                return en ? "Declined" : "Abgelehnt";
            default:
                return en ? "Failed" : "Fehler";
        }
    }

    /// <summary>Welche Staffeln angefragt sind: „Staffel 2“, „Staffeln 1–3“, „Alle Staffeln“.</summary>
    public static string? RequestSeasons(int[] seasons, int known, string lang)
    {
        if (seasons.Length == 0)
        {
            return null;
        }

        var en = lang == "en";
        var inv = CultureInfo.InvariantCulture;
        if (seasons.Length == 1 && seasons[0] == 0)
        {
            return "Specials";
        }

        var list = seasons.Where(n => n > 0).Distinct().OrderBy(n => n).ToArray();
        if (list.Length == 0)
        {
            return null;
        }

        if (list.Length == 1)
        {
            return (en ? "Season " : "Staffel ") + list[0].ToString(inv);
        }

        if (known > 1 && list.Length >= known && list[0] == 1)
        {
            return en ? "All seasons" : "Alle Staffeln";
        }

        var word = en ? "Seasons " : "Staffeln ";
        if (list[^1] - list[0] == list.Length - 1)
        {
            return word + list[0].ToString(inv) + "–" + list[^1].ToString(inv);
        }

        if (list.Length > 4)
        {
            return list.Length.ToString(inv) + (en ? " seasons" : " Staffeln");
        }

        return word + string.Join(", ", list[..^1].Select(n => n.ToString(inv))) + (en ? " and " : " und ") + list[^1].ToString(inv);
    }

    public static string Requested(string lang) => lang == "en" ? "Requested" : "Angefragt";

    // ---- Überschriften -----------------------------------------------------------------------------------------

    public static string Home(string lang) => lang == "en" ? "For you" : "Für dich";

    public static string Row(string id, string lang, string? genre)
    {
        var en = lang == "en";
        if (genre is not null)
        {
            return id switch
            {
                "top10" => (en ? "Picked for you · " : "Für dich ausgewählt · ") + genre,
                "library" => (en ? "Already here · " : "Schon da · ") + genre,
                "trending" => (en ? "Trending now · " : "Gerade angesagt · ") + genre,
                "classics" => (en ? "Classics · " : "Klassiker · ") + genre,
                "gems" => (en ? "Hidden gems · " : "Geheimtipps · ") + genre,
                "tv" => (en ? "Series · " : "Serien · ") + genre,
                "movies" => (en ? "Movies · " : "Filme · ") + genre,
                "collections" => (en ? "Missing from your collections · " : "Fehlt dir aus deinen Filmreihen · ") + genre,
                "upcoming" => (en ? "Coming soon · " : "Demnächst · ") + genre,
                "explore" => (en ? "Something different · " : "Mal was anderes · ") + genre,
                "requests" => RequestsTitle(lang) + " · " + genre,
                _ => genre,
            };
        }

        return id switch
        {
            "top10" => en ? "Picked for you" : "Für dich ausgewählt",
            "library" => en ? "Already here – a match for you" : "Schon da – passt zu dir",
            "trending" => en ? "Trending now" : "Gerade angesagt",
            "classics" => en ? "Classics you are missing" : "Klassiker, die dir fehlen",
            "gems" => en ? "Hidden gems" : "Geheimtipps",
            "tv" => en ? "Series for you" : "Serien für dich",
            "movies" => en ? "Movies for you" : "Filme für dich",
            "collections" => en ? "Missing from your collections" : "Fehlt dir aus deinen Filmreihen",
            "upcoming" => en ? "Coming soon" : "Demnächst",
            "explore" => en ? "Something different" : "Mal was anderes",
            "requests" => RequestsTitle(lang),
            _ => id,
        };
    }

    public static string GenreRow(string genre, string lang) => genre + (lang == "en" ? " for you" : " für dich");

    public static string PersonRow(string name, string lang) => (lang == "en" ? "More from " : "Mehr von ") + name;

    public static string BecauseRow(SignalKind kind, string name, string lang) => kind switch
    {
        SignalKind.Favorite or SignalKind.Like => lang == "en" ? "Because you like " + Quote(name, lang) : "Weil dir " + Quote(name, lang) + " gefällt",
        SignalKind.Requested => lang == "en" ? "Because you requested " + Quote(name, lang) : "Weil du " + Quote(name, lang) + " angefragt hast",
        _ => lang == "en" ? "Because you watched " + Quote(name, lang) : "Weil du " + Quote(name, lang) + " gesehen hast",
    };

    /// <summary>Kürzt an einer Wortgrenze und hängt Auslassungspunkte an.</summary>
    public static string Shorten(string text, int max)
    {
        max = Math.Max(max, 8);
        if (text.Length <= max)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', max - 1);
        if (cut < max / 2)
        {
            cut = max - 1;
        }

        return text[..cut].TrimEnd(' ', ',', ':', '-', '–', '.') + "…";
    }
}
