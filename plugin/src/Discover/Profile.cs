namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>Eine Person im Profil: wie stark sie zählt und ob vor allem als Regie.</summary>
internal sealed class PersonPref
{
    public string Name { get; init; } = string.Empty;

    public float Raw { get; set; }

    public float DirectRaw { get; set; }

    public float Weight { get; set; }

    /// <summary>In wie vielen starken positiven Signalen die Person vorkommt.</summary>
    public int StrongHits { get; set; }

    public bool Tv { get; set; }

    public bool Directs => DirectRaw >= Raw - DirectRaw;
}

/// <summary>Ein Merkmal für die Ansicht „Dein Geschmack“.</summary>
internal sealed record FeatureView(string Feature, string Name, float Weight, bool Muted, bool Directs = false, bool Tv = false);

/// <summary>
/// Der Geschmack eines Kontos: aus den Signalen gewichtete Merkmale. Gewichte liegen zwischen −1 und 1 (Genres) bzw.
/// 0 und 1; stummgeschaltete Merkmale stehen nur noch in den Listen für die Ansicht.
/// </summary>
internal sealed class Profile
{
    // Gewichte der Signale. Starke positive bestimmen, wie viel der Geschmack gegenüber der Qualität zählt.
    public const float Favorite = 3f;
    public const float Like = 3f;
    public const float Completed = 1.5f;
    public const float Seen = 1f;
    public const float PartialMin = 0.4f;
    public const float Requested = 1f;
    public const float Unseen = 0.1f;
    public const float Abandoned = -0.5f;
    public const float Hidden = -0.5f;
    public const float Dislike = -2f;

    /// <summary>Ungesehene Bibliothekstitel dürfen zusammen höchstens 30 % des positiven Gewichts ausmachen.</summary>
    private const float UnseenShare = 0.3f;

    /// <summary>Bei so viel starkem Gewicht zählen Geschmack und Qualität gleich viel.</summary>
    public const float HalfConfidence = 12f;

    /// <summary>Mehr als so viel bestimmt der Geschmack die Rangfolge nie, wie viele Signale es auch gibt.</summary>
    public const float AlphaCap = 0.70f;

    // Stichworte, die fast überall stehen und nichts über den Geschmack sagen (Romanvorlage, Fortsetzung, Abspannszene …).
    private static readonly HashSet<int> DullKeywords = new() { 818, 9663, 179431, 179430, 187056, 9717, 9672, 209714, 155477, 11800, 6054 };

    public List<Signal> Signals { get; } = new();

    /// <summary>Summe der starken positiven Gewichte, dazu die Hälfte der ausdrücklichen Ablehnungen.</summary>
    public float N { get; private set; }

    public float Alpha { get; private set; }

    public int StrongCount { get; private set; }

    public Dictionary<int, float> Genres { get; } = new();

    public float GenreNorm { get; private set; }

    /// <summary>Summe der Signal-Gewichte je Genre, ungewichtet und nicht normiert (ein gesehener Film mit vier Genres: je 0,5).</summary>
    public Dictionary<int, float> GenreRaw { get; } = new();

    public Dictionary<string, PersonPref> People { get; } = new(StringComparer.Ordinal);

    public Dictionary<int, float> Keywords { get; } = new();

    public Dictionary<int, float> Decades { get; } = new();

    /// <summary>Positives Gewicht je Originalsprache.</summary>
    public Dictionary<string, float> Langs { get; } = new(StringComparer.Ordinal);

    public List<FeatureView> ViewGenres { get; } = new();

    public List<FeatureView> ViewPeople { get; } = new();

    public List<FeatureView> ViewDecades { get; } = new();

    public List<FeatureView> ViewKeywords { get; } = new();

    /// <summary>Genres mit positivem Gewicht, stärkste zuerst.</summary>
    public List<int> TopGenres { get; } = new();

    /// <summary>Sanfter Abfall über Monate: nach acht Monaten rund drei Viertel, nie unter gut der Hälfte.</summary>
    public static float Recency(DateTime? when, DateTime now)
    {
        if (!when.HasValue)
        {
            return 0.85f;
        }

        var days = Math.Max(0, (now - when.Value).TotalDays);
        return 0.55f + (0.45f * (float)Math.Pow(2, -days / 240.0));
    }

    /// <summary>Leitet aus Bibliothek, Rückmeldungen und Anfragen die Signale ab (noch ohne Merkmale).</summary>
    /// <param name="tuning">Gewichte für einen Messlauf; ohne Angabe die üblichen.</param>
    public static List<Signal> BuildSignals(LibrarySnapshot lib, StateView state, IReadOnlyDictionary<MediaKey, DateTime> ownRequests, DateTime now, Tuning? tuning = null)
    {
        var t = tuning ?? Tuning.Default;
        var map = new Dictionary<MediaKey, Signal>();
        foreach (var e in lib.Items.Values)
        {
            if (!e.Visible || state.Feedback.ContainsKey(e.Key))
            {
                continue;
            }

            Signal s;
            if (e.Favorite)
            {
                s = New(e, SignalKind.Favorite, t.Favorite, e.LastPlayed, true);
            }
            else if (!e.Key.Tv)
            {
                if (e.Played)
                {
                    s = e.PlayCount >= 2 ? New(e, SignalKind.Rewatched, t.Completed, e.LastPlayed, true) : New(e, SignalKind.Seen, t.Seen, e.LastPlayed, true);
                }
                else if (e.Progress > 0f && e.Progress < 0.2f && e.LastPlayed.HasValue && now - e.LastPlayed.Value > TimeSpan.FromDays(14))
                {
                    s = New(e, SignalKind.Abandoned, t.Abandoned, e.LastPlayed, false);
                }
                else
                {
                    s = New(e, SignalKind.Unseen, t.Unseen, null, false);
                }
            }
            else
            {
                var share = e.EpisodesTotal > 0 ? Math.Clamp(e.EpisodesPlayed / (float)e.EpisodesTotal, 0f, 1f) : 0f;
                if (e.EpisodesPlayed > 0 && share >= 0.8f)
                {
                    s = New(e, SignalKind.Rewatched, t.Completed, e.LastPlayed, true);
                }
                else if (e.EpisodesPlayed > 0)
                {
                    // Abgebrochen heißt bei Serien: nach höchstens drei Folgen (und unter 20 %) seit über zwei Wochen liegen gelassen.
                    var idle = !e.LastPlayed.HasValue || now - e.LastPlayed.Value > TimeSpan.FromDays(14);
                    s = share < 0.2f && e.EpisodesPlayed <= 3 && idle
                        ? New(e, SignalKind.Abandoned, t.Abandoned, e.LastPlayed, false)
                        : New(e, SignalKind.Partial, Math.Max(t.PartialMin, share * t.Seen), e.LastPlayed, true);
                }
                else
                {
                    s = New(e, SignalKind.Unseen, t.Unseen, null, false);
                }
            }

            map[e.Key] = s;
        }

        // Ausdrückliche Rückmeldungen gehen allem vor, was sich aus dem Schauen ergibt.
        foreach (var f in state.Feedback)
        {
            var when = DateTimeOffset.FromUnixTimeSeconds(f.Value.At).UtcDateTime;
            lib.Items.TryGetValue(f.Key, out var entry);
            var visible = entry is { Visible: true } ? entry : null;
            var signal = f.Value.Action switch
            {
                "like" => new Signal { Key = f.Key, Kind = SignalKind.Like, Base = t.Like, When = when, Strong = true, Lib = visible },
                "dislike" => new Signal { Key = f.Key, Kind = SignalKind.Dislike, Base = t.Dislike, When = when, Strong = true, Lib = visible },
                "hide" => new Signal { Key = f.Key, Kind = SignalKind.Hidden, Base = t.Hidden, When = when, Strong = false, Lib = visible },
                _ => null,
            };
            if (signal is not null)
            {
                signal.Name = visible?.Name ?? string.Empty;
                map[f.Key] = signal;
            }
        }

        foreach (var r in ownRequests)
        {
            if (!map.TryGetValue(r.Key, out var old) || old.Kind == SignalKind.Unseen)
            {
                map[r.Key] = new Signal { Key = r.Key, Name = old?.Name ?? string.Empty, Kind = SignalKind.Requested, Base = t.Requested, When = r.Value, Strong = true, Lib = old?.Lib };
            }
        }

        var list = map.Values.ToList();
        float strong = 0f, unseen = 0f;
        foreach (var s in list)
        {
            s.Weight = s.Kind == SignalKind.Unseen ? s.Base : s.Base * Recency(s.When, now);
            if (s.Kind == SignalKind.Unseen)
            {
                unseen += s.Weight;
            }
            else if (s.Weight > 0f)
            {
                strong += s.Weight;
            }
        }

        // Viele ungesehene Titel sollen wenige gesehene nicht übertönen. Ohne jedes starke Signal bleibt ihr Gewicht stehen:
        // Dann beschreibt allein die Bibliothek den Geschmack, die Rangfolge der Empfehlungen folgt ohnehin der Qualität.
        if (strong > 0f && unseen > 0f)
        {
            var cap = strong * UnseenShare / (1f - UnseenShare);
            if (unseen > cap)
            {
                var scale = cap / unseen;
                foreach (var s in list)
                {
                    if (s.Kind == SignalKind.Unseen)
                    {
                        s.Weight *= scale;
                    }
                }
            }
        }

        list.Sort((a, b) =>
        {
            var c = Math.Abs(b.Weight).CompareTo(Math.Abs(a.Weight));
            return c != 0 ? c : a.Key.Id.CompareTo(b.Key.Id);
        });
        return list;

        static Signal New(LibEntry e, SignalKind kind, float weight, DateTime? when, bool strong) =>
            new() { Key = e.Key, Name = e.Name, Kind = kind, Base = weight, When = when, Strong = strong, Lib = e };
    }

    /// <summary>Baut das Profil aus den Signalen. Merkmale kommen aus den Details, sonst aus der Bibliothek, sonst aus den Listen.</summary>
    /// <param name="worlds">Im Geschmacks-Check gewählte Welten (Genre-Nummern).</param>
    public static Profile Build(List<Signal> signals, Catalog catalog, string lang, HashSet<string> muted, int[]? worlds = null, Tuning? tuning = null)
    {
        var tune = tuning ?? Tuning.Default;
        var p = new Profile();
        p.Signals.AddRange(signals);

        var genreRaw = new Dictionary<int, float>();
        var keywordRaw = new Dictionary<int, float>();
        var decadeRaw = new Dictionary<int, float>();
        var canonical = new List<(int Id, float Part)>(8);
        float positive = 0f, rejected = 0f;

        foreach (var s in signals)
        {
            if (s.Strong)
            {
                p.StrongCount++;
                if (s.Weight > 0f)
                {
                    positive += s.Weight;
                }
                else if (s.Kind == SignalKind.Dislike)
                {
                    rejected += -s.Weight;
                }
            }

            var detail = catalog.Detail(s.Key);
            var title = catalog.Get(lang, s.Key);
            if (s.Name.Length == 0)
            {
                s.Name = detail is not null && detail.Texts.TryGetValue(lang, out var text) ? text.Name
                    : title?.Name ?? detail?.Texts.Values.FirstOrDefault()?.Name ?? string.Empty;
            }

            var w = s.Weight;
            // Eine Ablehnung gilt selten den Darstellern oder dem Jahrzehnt, meist der Art des Films.
            var side = w < 0f ? 0.3f : 1f;
            var strongPositive = s.Strong && w > 0f;

            var genres = detail?.Genres ?? title?.Genres ?? (s.Lib is not null ? GenreMap.FromNames(s.Lib.Genres, s.Key.Tv) : Array.Empty<int>());
            GenreMap.Canonical(genres, canonical);
            if (canonical.Count > 0)
            {
                var share = 1f / MathF.Sqrt(canonical.Count);
                foreach (var g in canonical)
                {
                    genreRaw[g.Id] = genreRaw.GetValueOrDefault(g.Id) + (w * g.Part * share);
                }
            }

            if (detail is not null)
            {
                foreach (var k in detail.Keywords)
                {
                    if (!DullKeywords.Contains(k))
                    {
                        keywordRaw[k] = keywordRaw.GetValueOrDefault(k) + (w * side);
                    }
                }

                foreach (var id in detail.Directors)
                {
                    p.AddPerson(catalog.Person(id), w * side, true, s.Key.Tv, strongPositive);
                }

                foreach (var id in detail.Cast)
                {
                    p.AddPerson(catalog.Person(id), w * side * 0.6f, false, s.Key.Tv, strongPositive);
                }
            }
            else if (s.Lib is not null)
            {
                foreach (var name in s.Lib.Directors)
                {
                    p.AddPerson(name, w * side, true, s.Key.Tv, strongPositive);
                }

                foreach (var name in s.Lib.Cast)
                {
                    p.AddPerson(name, w * side * 0.6f, false, s.Key.Tv, strongPositive);
                }
            }

            var year = detail?.Year > 0 ? detail.Year : title?.Year > 0 ? title.Year : s.Lib?.Year ?? 0;
            if (year > 1900)
            {
                var decade = year / 10 * 10;
                decadeRaw[decade] = decadeRaw.GetValueOrDefault(decade) + (w * side);
            }

            var language = detail?.Lang ?? title?.Lang;
            if (!string.IsNullOrEmpty(language) && w > 0f)
            {
                p.Langs[language] = p.Langs.GetValueOrDefault(language) + w;
            }
        }

        // Gewählte Welten: eine ausdrückliche Vorliebe für das Genre, etwa so stark wie zwei gemochte Titel daraus. Für
        // die Sicherheit über den Geschmack zählen sie gedämpft (alle zusammen höchstens wie vier Signale): Sie sagen, was
        // jemand mag, belegen es aber noch nicht.
        var chosen = 0;
        var chosenWorlds = new HashSet<int>();
        foreach (var world in worlds ?? Array.Empty<int>())
        {
            // Auch eine stummgeschaltete Welt bleibt in der Liste der Genres stehen, sonst ließe sie sich dort nicht wieder
            // einschalten; mitgezählt wird sie nicht.
            chosenWorlds.Add(world);
            genreRaw.TryAdd(world, 0f);
            if (!muted.Contains(Worlds.Feature(world)))
            {
                chosen += Worlds.SignalsEach;
            }
        }

        chosen = Math.Min(chosen, Worlds.SignalsMax);
        p.StrongCount += chosen;

        // Nach oben begrenzt: Auch bei sehr vielen Signalen behält die Qualität für sich knapp ein Drittel des Gewichts.
        p.N = positive + (0.5f * rejected) + (chosen * Worlds.SignalWeight);
        p.Alpha = Math.Min(tune.AlphaCap, p.N / (p.N + tune.HalfConfidence));

        // Genres: häufige Genres abwerten, dann auf das stärkste normieren.
        var genrePref = new Dictionary<int, float>();
        var max = 0f;
        foreach (var g in genreRaw)
        {
            // Eine gewählte Welt zählt für jedes Genre gleich: Bei einer ausdrücklichen Wahl spielt keine Rolle, wie häufig
            // das Genre ist (aus dem Geschauten abgeleitet sagt „Drama“ dagegen wenig).
            var bonus = chosenWorlds.Contains(g.Key) ? Worlds.GenreWeight : 0f;
            var value = (g.Value * GenreMap.Weight(g.Key)) + bonus;
            genrePref[g.Key] = value;
            max = Math.Max(max, value);
            if (!muted.Contains("genre:" + g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            {
                p.GenreRaw[g.Key] = g.Value + bonus;
            }
        }

        var scale = max > 0f ? max : Math.Max(0.0001f, genrePref.Count > 0 ? genrePref.Values.Max(v => Math.Abs(v)) : 1f);
        foreach (var g in genrePref.OrderByDescending(g => g.Value))
        {
            var value = Math.Clamp(g.Value / scale, -1f, 1f);
            var feature = "genre:" + g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var isMuted = muted.Contains(feature);
            if (value > 0.02f && p.ViewGenres.Count < 10)
            {
                p.ViewGenres.Add(new FeatureView(feature, GenreMap.Name(g.Key, lang), value, isMuted));
            }

            if (isMuted)
            {
                continue;
            }

            p.Genres[g.Key] = value;
            if (value > 0.05f)
            {
                p.TopGenres.Add(g.Key);
            }
        }

        var sum = 0f;
        foreach (var v in p.Genres.Values)
        {
            sum += v * v;
        }

        p.GenreNorm = MathF.Sqrt(sum);

        // Personen: erst ab mehr als einem einfachen „gesehen“ zählen, sonst wäre jede Besetzung schon ein Geschmack.
        var personMax = Math.Max(4f, p.People.Count > 0 ? p.People.Values.Max(x => x.Raw) : 0f);
        foreach (var person in p.People.OrderByDescending(x => x.Value.Raw).ToList())
        {
            var feature = "person:" + person.Key;
            if (person.Value.Raw < 1.5f)
            {
                p.People.Remove(person.Key);
                continue;
            }

            person.Value.Weight = Math.Min(1f, person.Value.Raw / personMax);
            var isMuted = muted.Contains(feature);
            if (p.ViewPeople.Count < 10)
            {
                p.ViewPeople.Add(new FeatureView(feature, person.Value.Name, person.Value.Weight, isMuted, person.Value.Directs, person.Value.Tv));
            }

            if (isMuted)
            {
                p.People.Remove(person.Key);
            }
        }

        var keywordMax = Math.Max(4f, keywordRaw.Count > 0 ? keywordRaw.Values.Max() : 0f);
        foreach (var k in keywordRaw.OrderByDescending(x => x.Value))
        {
            if (k.Value < 2f)
            {
                break;
            }

            var feature = "kw:" + k.Key.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var weight = Math.Min(1f, k.Value / keywordMax);
            var isMuted = muted.Contains(feature);
            if (p.ViewKeywords.Count < 12)
            {
                p.ViewKeywords.Add(new FeatureView(feature, catalog.Keyword(k.Key) ?? feature, weight, isMuted));
            }

            if (!isMuted && p.Keywords.Count < 60)
            {
                p.Keywords[k.Key] = weight;
            }
        }

        var decadeMax = decadeRaw.Count > 0 ? decadeRaw.Values.Max() : 0f;
        if (decadeMax > 0f)
        {
            foreach (var d in decadeRaw.OrderByDescending(x => x.Value))
            {
                var weight = d.Value / decadeMax;
                if (weight <= 0.02f)
                {
                    continue;
                }

                var feature = "decade:" + d.Key.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var isMuted = muted.Contains(feature);
                if (p.ViewDecades.Count < 6)
                {
                    p.ViewDecades.Add(new FeatureView(feature, Texts.Decade(d.Key, lang), weight, isMuted));
                }

                if (!isMuted)
                {
                    p.Decades[d.Key] = weight;
                }
            }
        }

        return p;
    }

    public Signal? SignalFor(MediaKey key)
    {
        foreach (var s in Signals)
        {
            if (s.Key == key)
            {
                return s;
            }
        }

        return null;
    }

    private void AddPerson(string? name, float weight, bool directs, bool tv, bool strongPositive)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var feature = name.Trim().ToLowerInvariant();
        if (!People.TryGetValue(feature, out var person))
        {
            person = new PersonPref { Name = name.Trim(), Tv = tv };
            People[feature] = person;
        }

        person.Raw += weight;
        if (directs)
        {
            person.DirectRaw += weight;
        }

        if (strongPositive)
        {
            person.StrongHits++;
        }
    }
}
