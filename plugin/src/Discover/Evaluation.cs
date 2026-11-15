using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>
/// Die Stellschrauben der Rangfolge. Im Betrieb gelten immer die üblichen Werte; ein Messlauf kann sie überschreiben,
/// um zu sehen, ob eine Änderung die Empfehlungen besser oder schlechter macht.
/// </summary>
internal sealed record Tuning
{
    public static readonly Tuning Default = new();

    public float Favorite { get; init; } = Profile.Favorite;

    public float Like { get; init; } = Profile.Like;

    public float Completed { get; init; } = Profile.Completed;

    public float Seen { get; init; } = Profile.Seen;

    public float PartialMin { get; init; } = Profile.PartialMin;

    public float Requested { get; init; } = Profile.Requested;

    public float Unseen { get; init; } = Profile.Unseen;

    public float Abandoned { get; init; } = Profile.Abandoned;

    public float Hidden { get; init; } = Profile.Hidden;

    public float Dislike { get; init; } = Profile.Dislike;

    public float HalfConfidence { get; init; } = Profile.HalfConfidence;

    public float AlphaCap { get; init; } = Profile.AlphaCap;

    public float TasteFloor { get; init; } = Scorer.TasteFloor;

    public float TvRatingOffset { get; init; } = Scorer.TvRatingOffset;

    public float ForeignFactor { get; init; } = Scorer.ForeignFactor;

    public float FormatPenalty { get; init; } = Scorer.FormatPenalty;

    /// <summary>Tägliche kleine Verschiebung; im Messlauf aus, damit er wiederholbar ist.</summary>
    public bool Jitter { get; init; } = true;

    /// <summary>Abzug für oft Gezeigtes; im Messlauf aus, weil er vom Zeigen abhängt und nicht vom Geschmack.</summary>
    public bool Shown { get; init; } = true;

    /// <summary>Die Namen, unter denen sich die Werte in <c>set=</c> überschreiben lassen.</summary>
    private static readonly Dictionary<string, Func<Tuning, float, Tuning>> Setters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["heart"] = (t, v) => t with { Favorite = v },
        ["favorite"] = (t, v) => t with { Favorite = v },
        ["like"] = (t, v) => t with { Like = v },
        ["completed"] = (t, v) => t with { Completed = v },
        ["seen"] = (t, v) => t with { Seen = v },
        ["partial"] = (t, v) => t with { PartialMin = v },
        ["requested"] = (t, v) => t with { Requested = v },
        ["unseen"] = (t, v) => t with { Unseen = v },
        ["abandoned"] = (t, v) => t with { Abandoned = v },
        ["dislike"] = (t, v) => t with { Dislike = v },
        ["halfConfidence"] = (t, v) => t with { HalfConfidence = v },
        ["alphaMax"] = (t, v) => t with { AlphaCap = v },
        ["tasteFloor"] = (t, v) => t with { TasteFloor = v },
        ["tvOffset"] = (t, v) => t with { TvRatingOffset = v },
        ["foreign"] = (t, v) => t with { ForeignFactor = v },
        ["format"] = (t, v) => t with { FormatPenalty = v },
    };

    public static IEnumerable<string> Names => Setters.Keys;

    /// <summary>Liest <c>alphaMax:0.5,heart:2</c>; unbekannte Namen oder Zahlen ergeben einen Fehler statt stiller Übergehung.</summary>
    public static Tuning? Parse(string? set, Tuning start, out string? error)
    {
        error = null;
        var t = start;
        foreach (var part in (set ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cut = part.IndexOfAny(new[] { ':', '=' });
            if (cut <= 0
                || !Setters.TryGetValue(part[..cut].Trim(), out var apply)
                || !float.TryParse(part[(cut + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !float.IsFinite(value) || Math.Abs(value) > 100f)
            {
                error = "unbekannt: " + part + " (möglich: " + string.Join(", ", Setters.Keys) + ")";
                return null;
            }

            t = apply(t, value);
        }

        return t;
    }
}

/// <summary>Was ein Messlauf dem Builder vorgibt: die Bibliothek ohne die zurückgehaltenen Titel, der Zustand ohne ihre Rückmeldungen, die Gewichte.</summary>
internal sealed class Replay
{
    public LibrarySnapshot Lib { get; init; } = LibrarySnapshot.Empty;

    public StateView State { get; init; } = StateView.Empty;

    public IReadOnlyDictionary<MediaKey, List<(Guid User, long At)>> Theme { get; init; } = new Dictionary<MediaKey, List<(Guid User, long At)>>();

    public Func<RequestsInfo, RequestsInfo> Requests { get; init; } = r => r;

    public Tuning? Tuning { get; init; }
}

/// <summary>
/// Misst, wie gut die Empfehlungen sind: Die jüngsten positiven Signale eines Kontos werden zurückgehalten, als hätte es
/// sie nie gegeben; mit dem übrigen Verlauf entstehen Vorrat und Rangfolge wie im Betrieb, und gezählt wird, wie weit vorn
/// die zurückgehaltenen Titel landen. Zum Vergleich dieselbe Messung auf demselben Vorrat, nur nach Qualität und nur
/// nach Beliebtheit geordnet. Verändert nichts am Konto: kein Zustand, keine Impressionen, keine Dateien.
/// </summary>
internal sealed class Evaluator
{
    private const int MaxResults = 6;

    private static readonly SignalKind[] Positive = { SignalKind.Seen, SignalKind.Favorite, SignalKind.Like, SignalKind.Rewatched };

    private readonly Builder _builder;
    private readonly LibraryReader _library;
    private readonly StateStore _store;
    private readonly Catalog _catalog;
    private readonly CancellationToken _stop;
    private readonly ConcurrentDictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    private int _running;

    public Evaluator(Builder builder, LibraryReader library, StateStore store, Catalog catalog, CancellationToken stop)
    {
        _builder = builder;
        _library = library;
        _store = store;
        _catalog = catalog;
        _stop = stop;
    }

    /// <summary>
    /// Stand oder Ergebnis eines Messlaufs. Der erste Abruf startet ihn im Hintergrund (wie eine Berechnung der Seite);
    /// weitere Abrufe mit denselben Angaben liefern den Fortschritt und am Ende das Ergebnis. Es läuft immer nur einer.
    /// </summary>
    /// <param name="watched">Nur Geschautes zurückhalten (gesehen, Herz, zu Ende), keine „Gefällt mir“ aus dem Geschmacks-Check:
    /// Dort werden bewusst sehr bekannte Titel gefragt, was die Messung zugunsten der Bekanntheit verzerrt.</param>
    public object Get(User user, string lang, int holdout, int folds, Tuning tuning, string set, bool sweep, bool restart, bool watched)
    {
        var id = string.Join('|', user.Id.ToString("N", CultureInfo.InvariantCulture), lang, holdout, folds, set, sweep, watched);
        if (_jobs.TryGetValue(id, out var job) && !(restart && job.Done))
        {
            return job.View();
        }

        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return new { status = "busy", hint = "Ein anderer Messlauf läuft gerade; danach erneut abrufen." };
        }

        job = new Job { Started = DateTime.UtcNow };
        _jobs[id] = job;
        foreach (var old in _jobs.Where(j => j.Value.Done).OrderBy(j => j.Value.Started).Take(Math.Max(0, _jobs.Count - MaxResults)).Select(j => j.Key).ToList())
        {
            _jobs.TryRemove(old, out _);
        }

        _ = Task.Run(
            async () =>
            {
                try
                {
                    job.Result = await RunAsync(user, lang, holdout, folds, tuning, sweep, watched, job).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    job.Error = "abgebrochen";
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    job.Error = ex.GetType().Name + ": " + ex.Message;
                }
                finally
                {
                    job.Done = true;
                    Interlocked.Exchange(ref _running, 0);
                }
            },
            CancellationToken.None);
        return job.View();
    }

    private async Task<object> RunAsync(User user, string lang, int holdout, int folds, Tuning tuning, bool sweep, bool watched, Job job)
    {
        var watch = Stopwatch.StartNew();
        var memory = new Memory();
        var lib = await Task.Run(() => _library.Read(user), _stop).ConfigureAwait(false);
        var state = _store.View(user.Id);
        var now = DateTime.UtcNow;

        // Die positiven Signale nach Zeitpunkt, jüngste zuerst. Ohne Zeitpunkt (Herz an Ungesehenem) lässt sich nicht
        // sagen, ob sie „später“ kamen; sie bleiben immer im Verlauf.
        var positives = Profile.BuildSignals(lib, state, new Dictionary<MediaKey, DateTime>(), now)
            .Where(s => s.Strong && s.Weight > 0f && s.When.HasValue && Array.IndexOf(Positive, s.Kind) >= 0 && !(watched && s.Kind == SignalKind.Like))
            .OrderByDescending(s => s.When!.Value).ThenBy(s => s.Key.Id)
            .ToList();
        var variants = sweep ? Variants() : new List<(string Name, Tuning Tuning)> { ("eingestellt", tuning) };
        var baseTuning = sweep ? Tuning.Default with { Jitter = false, Shown = false } : tuning;

        var methods = new[] { "plugin", "quality", "popularity" };
        var perVariant = variants.Select(_ => new List<Outcome>()).ToList();
        var baselines = new Dictionary<string, List<Outcome>> { ["quality"] = new(), ["popularity"] = new(), ["votes"] = new() };
        var foldInfo = new List<object>();
        for (var fold = 0; fold < folds; fold++)
        {
            var window = positives.Skip(fold * holdout).Take(holdout).ToList();
            if (window.Count < Math.Max(1, holdout / 2))
            {
                break;
            }

            // Zurückgehalten wird das Fenster und alles, was danach kam: Für diesen Zeitpunkt ist es Zukunft.
            var removed = positives.Take((fold * holdout) + window.Count).Select(s => s.Key).ToHashSet();
            var replay = Replay(user, lib, state, removed, baseTuning);
            var foldWatch = Stopwatch.StartNew();
            var f = fold;
            var built = await _builder.RunAsync(user, lang, null, "eval", null, p => job.Progress = (f + (0.95f * p)) / folds, _stop, replay).ConfigureAwait(false);
            if (built.Error is not null)
            {
                throw new InvalidOperationException("Seerr: " + built.Error);
            }

            memory.Sample();
            for (var v = 0; v < variants.Count; v++)
            {
                var snap = Scorer.Rank(new RankInput
                {
                    User = user.Id,
                    Lang = lang,
                    Pool = built.Pool,
                    Lib = built.Lib,
                    Requests = built.Requests,
                    Theme = replay.Theme,
                    State = replay.State,
                    Catalog = _catalog,
                    BuiltAt = DateTime.UtcNow,
                    Info = built.Info,
                    Tuning = variants[v].Tuning,
                });
                perVariant[v].AddRange(Measure(window, built.Pool, snap, s => s.Score, fold));
                if (v == 0)
                {
                    // Vergleich auf demselben Vorrat: nur Qualität (wie α = 0), nur Beliebtheit (TMDb, eher „gerade gefragt“)
                    // und nur Zahl der Stimmen (eher „allgemein bekannt“).
                    baselines["quality"].AddRange(Measure(window, built.Pool, snap, s => s.Quality, fold));
                    baselines["popularity"].AddRange(Measure(window, built.Pool, snap, s => s.Title.Popularity + (s.Title.Votes / 1e6f), fold));
                    baselines["votes"].AddRange(Measure(window, built.Pool, snap, s => s.Title.Votes, fold));
                    foldInfo.Add(new
                    {
                        fold = fold + 1,
                        heldOut = window.Count,
                        removedTotal = removed.Count,
                        from = window[^1].When!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        to = window[0].When!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        pool = built.Pool.Items.Count,
                        ranked = snap.Ranked.Length,
                        signals = snap.Profile.StrongCount,
                        alpha = Math.Round(snap.Profile.Alpha, 2),
                        seconds = Math.Round(foldWatch.Elapsed.TotalSeconds, 1),
                        seerrRequests = built.Info.SeerrRequests,
                        cacheHits = built.Info.CacheHits,
                    });
                }

                memory.Sample();
            }
        }

        job.Progress = 1f;
        var tuned = Describe(tuning);
        if (sweep)
        {
            var baseline = Metrics(perVariant[0]);
            return new
            {
                status = "ready",
                kind = "sweep",
                holdoutPerFold = holdout,
                folds = foldInfo,
                eligibleSignals = positives.Count,
                note = "Alle Varianten auf demselben Vorrat je Durchgang (mit den üblichen Gewichten gesucht); verglichen wird die Rangfolge.",
                seconds = Math.Round(watch.Elapsed.TotalSeconds, 1),
                memory = memory.View(),
                variants = variants.Select((v, i) =>
                {
                    var m = Metrics(perVariant[i]);
                    // Gepaart gezählt: bei wie vielen Titeln die Variante den Rang verbessert bzw. verschlechtert. Bei wenigen
                    // Zurückgehaltenen sagt das mehr als ein kleiner Unterschied im Mittel.
                    int better = 0, worse = 0;
                    for (var k = 0; k < perVariant[i].Count; k++)
                    {
                        var a = perVariant[i][k].Rank ?? int.MaxValue;
                        var o = perVariant[0][k].Rank ?? int.MaxValue;
                        better += a < o ? 1 : 0;
                        worse += a > o ? 1 : 0;
                    }

                    return new { name = v.Name, set = Describe(v.Tuning), metrics = m, mrrChange = Math.Round(m.Mrr - baseline.Mrr, 4), better, worse };
                }),
                quality = Metrics(baselines["quality"]),
                popularity = Metrics(baselines["popularity"]),
                votes = Metrics(baselines["votes"]),
            };
        }

        var plugin = perVariant[0];
        return new
        {
            status = "ready",
            kind = "eval",
            holdoutPerFold = holdout,
            set = tuned,
            eligibleSignals = positives.Count,
            folds = foldInfo,
            seconds = Math.Round(watch.Elapsed.TotalSeconds, 1),
            memory = memory.View(),
            metrics = new Dictionary<string, MetricSet>
            {
                ["plugin"] = Metrics(plugin),
                ["quality"] = Metrics(baselines["quality"]),
                ["popularity"] = Metrics(baselines["popularity"]),
                ["votes"] = Metrics(baselines["votes"]),
            },
            titles = plugin.Select((o, i) => new
            {
                fold = o.Fold + 1,
                key = o.Key.ToString(),
                title = o.Name,
                kind = o.Kind.ToString().ToLowerInvariant(),
                when = o.When.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                inPool = o.InPool,
                why = o.Why,
                rank = new Dictionary<string, int?> { ["plugin"] = o.Rank, ["quality"] = baselines["quality"][i].Rank, ["popularity"] = baselines["popularity"][i].Rank, ["votes"] = baselines["votes"][i].Rank },
                sources = o.Sources,
            }),
        };
    }

    /// <summary>Die Varianten des Sweeps: je eine Stellschraube in beide Richtungen, alles andere wie eingestellt.</summary>
    private static List<(string Name, Tuning Tuning)> Variants()
    {
        var b = Tuning.Default with { Jitter = false, Shown = false };
        return new List<(string, Tuning)>
        {
            ("wie eingestellt", b),
            ("α höchstens 0,5", b with { AlphaCap = 0.5f }),
            ("α höchstens 0,6", b with { AlphaCap = 0.6f }),
            ("α höchstens 0,8", b with { AlphaCap = 0.8f }),
            ("α höchstens 0,9", b with { AlphaCap = 0.9f }),
            ("α wächst schneller (Hälfte bei 6)", b with { HalfConfidence = 6f }),
            ("α wächst langsamer (Hälfte bei 24)", b with { HalfConfidence = 24f }),
            ("Geschmack unabhängig von Qualität", b with { TasteFloor = 1f }),
            ("Geschmack stärker an Qualität (0,2)", b with { TasteFloor = 0.2f }),
            ("Geschmack schwächer an Qualität (0,7)", b with { TasteFloor = 0.7f }),
            ("Gesehen zählt halb", b with { Seen = 0.5f }),
            ("Gesehen zählt doppelt", b with { Seen = 2f }),
            ("Herz zählt 2", b with { Favorite = 2f, Like = 2f }),
            ("Herz zählt 5", b with { Favorite = 5f, Like = 5f }),
            ("Serienabzug 0", b with { TvRatingOffset = 0f }),
            ("Serienabzug 0,25", b with { TvRatingOffset = 0.25f }),
            ("Serienabzug 0,6", b with { TvRatingOffset = 0.6f }),
            ("kein Formatabzug", b with { FormatPenalty = 0f }),
            ("kein Sprachabzug", b with { ForeignFactor = 1f }),
        };
    }

    private Replay Replay(User user, LibrarySnapshot lib, StateView state, HashSet<MediaKey> removed, Tuning tuning)
    {
        var trimmed = new LibrarySnapshot { Fingerprint = lib.Fingerprint, Movies = lib.Movies, Series = lib.Series };
        foreach (var e in lib.Items)
        {
            if (!removed.Contains(e.Key))
            {
                trimmed.Items[e.Key] = e.Value;
            }
        }

        var view = new StateView
        {
            Revision = state.Revision,
            TasteDone = state.TasteDone,
            Feedback = state.Feedback.Where(f => !removed.Contains(f.Key)).ToDictionary(f => f.Key, f => f.Value),
            Muted = state.Muted,
            Worlds = state.Worlds,
            Requests = state.Requests.Where(f => !removed.Contains(f.Key)).ToDictionary(f => f.Key, f => f.Value),
        };
        var theme = _store.ThemeRequests().Where(t => !removed.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value);
        return new Replay
        {
            Lib = trimmed,
            State = view,
            Theme = theme,
            Tuning = tuning,

            // Hat das Konto einen zurückgehaltenen Titel angefragt, wäre auch das ein Hinweis aus der Zukunft.
            Requests = r => new RequestsInfo { At = r.At, Items = r.Items.Where(x => !removed.Contains(x.Key)).ToList() },
        };
    }

    private IEnumerable<Outcome> Measure(List<Signal> window, Pool pool, Snapshot snap, Func<Scored, float> order, int fold)
    {
        // Rang unter den fehlenden Titeln, die der Betrieb überhaupt empfehlen würde (ohne Kommendes); Gleichstände fest nach Nummer.
        var ranked = snap.Ranked.OrderByDescending(order).ThenBy(s => s.Key.Tv).ThenBy(s => s.Key.Id).ToList();
        var index = new Dictionary<MediaKey, int>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            index[ranked[i].Key] = i + 1;
        }

        foreach (var s in window)
        {
            var inPool = pool.Items.TryGetValue(s.Key, out var candidate);
            int? rank = index.TryGetValue(s.Key, out var r) ? r : null;
            var why = rank.HasValue ? null
                : !inPool ? "nicht im Vorrat"
                : snap.ByKey.TryGetValue(s.Key, out var scored) && scored.Upcoming ? "kommt erst"
                : "unter den Untergrenzen oder ohne Plakat";
            var title = _catalog.Get(snap.Lang, s.Key) ?? _catalog.FromDetail(snap.Lang, s.Key);
            yield return new Outcome
            {
                Fold = fold,
                Key = s.Key,
                Name = s.Lib?.Name is { Length: > 0 } n ? n : title?.Name ?? s.Name,
                Kind = s.Kind,
                When = s.When!.Value,
                InPool = inPool,
                Rank = rank,
                Why = why,
                Sources = candidate is null ? null : candidate.Sources.ToString(),
            };
        }
    }

    private static MetricSet Metrics(List<Outcome> list)
    {
        var n = Math.Max(1, list.Count);
        var ranks = list.Where(o => o.Rank.HasValue).Select(o => o.Rank!.Value).OrderBy(r => r).ToList();
        return new MetricSet
        {
            HeldOut = list.Count,
            InPool = Math.Round(list.Count(o => o.InPool) / (double)n, 3),
            Ranked = Math.Round(ranks.Count / (double)n, 3),
            Hits10 = ranks.Count(r => r <= 10),
            Hits50 = ranks.Count(r => r <= 50),
            Hits100 = ranks.Count(r => r <= 100),
            Recall10 = Math.Round(ranks.Count(r => r <= 10) / (double)n, 3),
            Recall50 = Math.Round(ranks.Count(r => r <= 50) / (double)n, 3),
            Recall100 = Math.Round(ranks.Count(r => r <= 100) / (double)n, 3),
            MeanRank = ranks.Count > 0 ? Math.Round(ranks.Average(), 1) : null,
            MedianRank = ranks.Count > 0 ? ranks[ranks.Count / 2] : null,
            Mrr = Math.Round(list.Sum(o => o.Rank.HasValue ? 1.0 / o.Rank.Value : 0.0) / n, 4),
            MrrError = Math.Round(Error(list.Select(o => o.Rank.HasValue ? 1.0 / o.Rank.Value : 0.0).ToList()), 4),
        };
    }

    private static double Error(List<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        var mean = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
        return Math.Sqrt(variance / values.Count);
    }

    private static string Describe(Tuning t)
    {
        var parts = new List<string>();
        var d = Tuning.Default;
        void Add(string name, float value, float standard)
        {
            if (Math.Abs(value - standard) > 1e-4f)
            {
                parts.Add(name + ":" + value.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }

        Add("heart", t.Favorite, d.Favorite);
        Add("like", t.Like, d.Like);
        Add("completed", t.Completed, d.Completed);
        Add("seen", t.Seen, d.Seen);
        Add("partial", t.PartialMin, d.PartialMin);
        Add("requested", t.Requested, d.Requested);
        Add("unseen", t.Unseen, d.Unseen);
        Add("abandoned", t.Abandoned, d.Abandoned);
        Add("dislike", t.Dislike, d.Dislike);
        Add("halfConfidence", t.HalfConfidence, d.HalfConfidence);
        Add("alphaMax", t.AlphaCap, d.AlphaCap);
        Add("tasteFloor", t.TasteFloor, d.TasteFloor);
        Add("tvOffset", t.TvRatingOffset, d.TvRatingOffset);
        Add("foreign", t.ForeignFactor, d.ForeignFactor);
        Add("format", t.FormatPenalty, d.FormatPenalty);
        return parts.Count == 0 ? "üblich" : string.Join(',', parts);
    }

    internal sealed class MetricSet
    {
        public int HeldOut { get; init; }

        /// <summary>Anteil der Zurückgehaltenen, die überhaupt im Kandidatenvorrat landeten.</summary>
        public double InPool { get; init; }

        /// <summary>Anteil, der auch die Untergrenzen bestand und damit einen Rang hat.</summary>
        public double Ranked { get; init; }

        public int Hits10 { get; init; }

        public int Hits50 { get; init; }

        public int Hits100 { get; init; }

        public double Recall10 { get; init; }

        public double Recall50 { get; init; }

        public double Recall100 { get; init; }

        /// <summary>Mittlerer Rang der Titel mit Rang (die übrigen zählen hier nicht, wohl aber bei MRR als 0).</summary>
        public double? MeanRank { get; init; }

        public int? MedianRank { get; init; }

        public double Mrr { get; init; }

        /// <summary>Standardfehler von MRR: Unterschiede unter etwa dem Doppelten sind bei so wenigen Titeln nicht belastbar.</summary>
        public double MrrError { get; init; }
    }

    private sealed class Outcome
    {
        public int Fold { get; init; }

        public MediaKey Key { get; init; }

        public string Name { get; init; } = string.Empty;

        public SignalKind Kind { get; init; }

        public DateTime When { get; init; }

        public bool InPool { get; init; }

        public int? Rank { get; init; }

        public string? Why { get; init; }

        public string? Sources { get; init; }
    }

    /// <summary>Größter beobachteter Speicherbedarf des Servers während des Laufs (verwalteter Speicher und Arbeitsspeicher).</summary>
    private sealed class Memory
    {
        private readonly long _startManaged = GC.GetTotalMemory(false);
        private readonly long _startWorking = Environment.WorkingSet;
        private long _peakManaged;
        private long _peakWorking;

        public Memory() => Sample();

        public void Sample()
        {
            _peakManaged = Math.Max(_peakManaged, GC.GetTotalMemory(false));
            _peakWorking = Math.Max(_peakWorking, Environment.WorkingSet);
        }

        public object View() => new
        {
            managedStartMb = _startManaged / 1048576,
            managedPeakMb = _peakManaged / 1048576,
            workingSetStartMb = _startWorking / 1048576,
            workingSetPeakMb = _peakWorking / 1048576,
        };
    }

    private sealed class Job
    {
        public DateTime Started { get; init; }

        public volatile bool Done;

        public float Progress { get; set; }

        public object? Result { get; set; }

        public string? Error { get; set; }

        public object View() => Done
            ? Result ?? new { status = "empty", error = Error }
            : new { status = "running", progress = Math.Round(Progress, 2), seconds = Math.Round((DateTime.UtcNow - Started).TotalSeconds, 1) };
    }
}
