using System.Globalization; using System.Security.Cryptography; using System.Text; using System.Text.Json; using System.Text.Json.Serialization; using System.Text.RegularExpressions;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>
/// One ranked benchmark run as the shop keeps it for the comparison lists: which benchmark (and its workload version and settings), the part it
/// measured (a CPU, GPU or drive model), the machine, and the headline number. <see cref="System"/> is an opaque hash of the machine and its
/// parts, so the published lists can count distinct systems without naming them; <see cref="Machine"/> and <see cref="Spec"/> stay in the shop's
/// own files and never go into the lists.
/// </summary>
public sealed record BenchmarkRun(string Id, DateTimeOffset At, string Benchmark, int Version, string Settings, string Part, string System, string Machine, string Spec,
    double Value, string Unit, string? App = null, IReadOnlyList<BenchmarkMetric>? Metrics = null, bool Overclocked = false, IReadOnlyList<SpecItem>? Details = null)
{
    [JsonIgnore] public string Table => BenchmarkPeers.TableKey(Benchmark, Version, Settings);
}

/// <summary>The shop's own say about a logged run, kept apart from the append-only run log: <see cref="Featured"/> puts it among the reference
/// results every copy shows; <see cref="Overclocked"/>, when set, overrides what was said when it ran. Only the shop's copies' marks are published.</summary>
public sealed record BenchmarkMark(bool Featured, bool? Overclocked, string? Note, DateTimeOffset At);

/// <summary>One run as the lists show it beside an entry: its number, whether the part was overclocked, what was measured during it (clocks,
/// temperatures) and the part's and system's specifications. No machine name and no system id: a list names models, not customers.</summary>
public sealed record RunSample(double Value, bool Overclocked, DateTimeOffset At, IReadOnlyList<BenchmarkMetric>? Metrics, IReadOnlyList<SpecItem>? Details);

/// <summary>A run the shop chose to show as a reference result, with the same facts as a <see cref="RunSample"/>.</summary>
public sealed record FeaturedRun(string Id, string Part, double Value, bool Overclocked, string? Note, DateTimeOffset At, IReadOnlyList<BenchmarkMetric>? Metrics, IReadOnlyList<SpecItem>? Details);

/// <summary>One part model in a comparison list: the median of its systems' best runs (each system counts once, however often it ran), the
/// fastest of them, how many systems and runs it stands for, and the latest run. An overclocked part is an entry of its own, so it neither lifts
/// the model's usual figure nor hides among it. <see cref="Sample"/> is the run nearest the median, with its conditions and specifications.</summary>
public sealed record PeerEntry(string Part, double Median, double Best, int Systems, int Runs, DateTimeOffset Last, bool Overclocked = false, RunSample? Sample = null)
{
    [JsonIgnore] internal string Key => Overclocked ? Part + "\u0001oc" : Part;
}

/// <summary>The comparison list of one benchmark, workload version and settings: one entry per part model, best first, and the shop's featured
/// runs. The published database is one such file per list, so a client fetches only the lists that changed, and a list grows with the number of
/// models, not of runs.</summary>
public sealed record PeerTable(string Key, string Benchmark, int Version, string Settings, string Unit, bool HigherIsBetter, DateTimeOffset Built, IReadOnlyList<PeerEntry> Entries,
    IReadOnlyList<FeaturedRun>? Featured = null);

/// <summary>How far apart two results are, read the way people say it: <see cref="Ratio"/> is the faster one over the slower one (so never below 1),
/// and <see cref="TheyLead"/> says which side is faster. 26 against 6 GFLOPS is 4.3 either way; it is "4.3× faster" on the faster row.</summary>
public readonly record struct PeerGap(double Ratio, bool TheyLead)
{
    /// <summary>Up to double as a percentage ("25%", "4.5%"), from double as a multiple ("3.2×", "12×"), "≈" within half a percent.</summary>
    public string Text
    {
        get
        {
            double pct = (Ratio - 1) * 100;
            if (pct < 0.5) return "≈";
            if (Ratio >= 1.995) return (Ratio < 9.95 ? Ratio.ToString("0.0", CultureInfo.InvariantCulture) : Ratio.ToString("0", CultureInfo.InvariantCulture)) + "×";
            return (pct < 9.95 ? pct.ToString("0.0", CultureInfo.InvariantCulture) : pct.ToString("0", CultureInfo.InvariantCulture)) + "%";
        }
    }
    public bool Equal => (Ratio - 1) * 100 < 0.5;
}

/// <summary>A list entry against this system's result. <see cref="DiffPercent"/> is how much this result is above (+) or below (−) the entry,
/// signed so that positive is always better; <see cref="Gap"/> is the same distance as a ratio, null when there is no result to compare.
/// <see cref="Local"/> marks a model known only from this copy's own runs, not yet published.</summary>
public sealed record PeerRow(PeerEntry Entry, double DiffPercent, bool Local, bool Same, PeerGap? Gap = null);

/// <summary>Where this result stands in a list: <see cref="Rows"/> best first, this result sitting before <see cref="MineIndex"/>;
/// <see cref="Beaten"/> is how many entries it is ahead of.</summary>
public sealed record PeerRanking(int Total, int Beaten, int MineIndex, IReadOnlyList<PeerRow> Rows);

public static partial class BenchmarkPeers
{
    /// <summary>Options that choose the device (which GPU, which drive) rather than the work: they name the part, and are not part of the list's key.</summary>
    public static readonly IReadOnlySet<string> DeviceOptions = new HashSet<string>(StringComparer.Ordinal) { "gpu", "drive" };
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>The options that change the work (a file size, a resolution), in a fixed order; empty when there are none.</summary>
    public static string Settings(IReadOnlyDictionary<string, string>? options)
        => options is null ? "" : string.Join("|", options.Where(o => !DeviceOptions.Contains(o.Key)).OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => $"{o.Key}={o.Value}"));

    public static string TableKey(string benchmark, int version, string settings) => $"{benchmark}@{version}" + (settings.Length > 0 ? "|" + settings : "");

    /// <summary>The list's file name on the site and on disk: plain characters only, the settings folded into a short hash.</summary>
    public static string FileName(string tableKey)
    {
        int bar = tableKey.IndexOf('|');
        if (bar < 0) return tableKey.Replace("@", "-v", StringComparison.Ordinal) + ".json";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tableKey[(bar + 1)..])))[..10];
        return $"{tableKey[..bar].Replace("@", "-v", StringComparison.Ordinal)}-{hash}.json";
    }

    /// <summary>A part's name as the lists group it: the vendors' ® and ™ marks, "CPU @ 3.20GHz" and "8-Core Processor" tails and doubled spaces
    /// removed, so one model read by two machines is one entry. Nothing is added or guessed.</summary>
    public static string PartName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = Marks().Replace(raw, " ");
        s = CpuTail().Replace(s, "");
        return Spaces().Replace(s, " ").Trim();
    }

    /// <summary>The comparison lists from every run: runs of one list are grouped by part; per part, each system counts once, with its best run,
    /// and the entry's value is the median of those. Runs in a unit other than the list's usual one are left out (they cannot be compared).</summary>
    /// <remarks>An overclocked run (as it was logged, or as the shop marked it later) makes an entry of its own. Runs the shop marked as
    /// featured are also listed one by one, best first.</remarks>
    public static IReadOnlyList<PeerTable> Aggregate(IEnumerable<BenchmarkRun> runs, DateTimeOffset built, IReadOnlyDictionary<string, BenchmarkMark>? marks = null)
    {
        var tables = new List<PeerTable>();
        foreach (var list in runs.Where(r => r.Part.Length > 0 && r.Value > 0 && double.IsFinite(r.Value)).Select(r => Marked(r, marks)).GroupBy(r => r.Table, StringComparer.Ordinal))
        {
            var first = list.First();
            bool higher = BenchmarkRecords.Headline(first.Benchmark)?.HigherIsBetter ?? true;
            string unit = list.GroupBy(r => r.Unit).OrderByDescending(g => g.Count()).First().Key;
            var usable = list.Where(r => r.Unit == unit).ToList();
            var entries = usable.GroupBy(r => (Part: PartName(r.Part).ToUpperInvariant(), r.Overclocked))
                .Select(part => Entry(PartName(part.First().Part), part, higher)).ToList();
            var featured = usable.Where(r => marks?.GetValueOrDefault(r.Id) is { Featured: true })
                .Select(r => new FeaturedRun(r.Id, PartName(r.Part), r.Value, r.Overclocked, marks![r.Id].Note, r.At, r.Metrics, r.Details));
            featured = higher ? featured.OrderByDescending(f => f.Value) : featured.OrderBy(f => f.Value);
            tables.Add(new PeerTable(list.Key, first.Benchmark, first.Version, first.Settings, unit, higher, built, Sort(entries, higher), [.. featured]));
        }
        return tables;
    }

    /// <summary>A run with the shop's later word on whether it was overclocked.</summary>
    public static BenchmarkRun Marked(BenchmarkRun run, IReadOnlyDictionary<string, BenchmarkMark>? marks)
        => marks?.GetValueOrDefault(run.Id)?.Overclocked is { } oc && oc != run.Overclocked ? run with { Overclocked = oc } : run;

    private static PeerEntry Entry(string part, IEnumerable<BenchmarkRun> runs, bool higher)
    {
        var all = runs.ToList();
        var perSystem = all.GroupBy(r => r.System).Select(s => higher ? s.MaxBy(r => r.Value)! : s.MinBy(r => r.Value)!).OrderBy(r => r.Value).ToList();
        double median = perSystem.Count % 2 == 1 ? perSystem[perSystem.Count / 2].Value : (perSystem[perSystem.Count / 2 - 1].Value + perSystem[perSystem.Count / 2].Value) / 2;
        var sample = perSystem.MinBy(r => Math.Abs(r.Value - median))!;
        return new PeerEntry(part, median, higher ? perSystem[^1].Value : perSystem[0].Value, perSystem.Count, all.Count, all.Max(r => r.At), all[0].Overclocked,
            new RunSample(sample.Value, sample.Overclocked, sample.At, sample.Metrics, sample.Details));
    }

    /// <summary>How far apart this result and another are, or null when either is missing.</summary>
    public static PeerGap? Gap(double mine, double theirs, bool higherIsBetter)
    {
        if (!(mine > 0) || !(theirs > 0) || !double.IsFinite(mine) || !double.IsFinite(theirs)) return null;
        double r = higherIsBetter ? theirs / mine : mine / theirs;   // above 1: theirs is the faster
        return r > 1 ? new PeerGap(r, true) : new PeerGap(1 / r, false);
    }

    private static List<PeerEntry> Sort(IEnumerable<PeerEntry> entries, bool higher)
        => [.. higher ? entries.OrderByDescending(e => e.Median).ThenBy(e => e.Part, StringComparer.OrdinalIgnoreCase) : entries.OrderBy(e => e.Median).ThenBy(e => e.Part, StringComparer.OrdinalIgnoreCase)];

    /// <summary>This result against a list: the published entries, plus the models only this copy has measured (marked local; a model in both is
    /// taken from the published list, so a run is never counted twice). The difference is taken against each entry's median.</summary>
    public static PeerRanking Rank(PeerTable? table, IReadOnlyList<PeerEntry> local, double mine, string? myPart, bool higherIsBetter)
    {
        var published = table?.Entries ?? [];
        var names = new HashSet<string>(published.Select(e => e.Key), StringComparer.OrdinalIgnoreCase);
        var merged = published.Concat(local.Where(e => !names.Contains(e.Key)));
        string me = PartName(myPart);
        var rows = Sort(merged, higherIsBetter).Select(e => new PeerRow(e, Diff(mine, e.Median, higherIsBetter), !names.Contains(e.Key),
            me.Length > 0 && string.Equals(e.Part, me, StringComparison.OrdinalIgnoreCase), Gap(mine, e.Median, higherIsBetter))).ToList();
        int beaten = rows.Count(r => r.DiffPercent > 0);
        return new PeerRanking(rows.Count, beaten, rows.Count(r => r.DiffPercent < 0), rows);
    }

    internal static double Diff(double mine, double theirs, bool higherIsBetter) => (mine - theirs) / theirs * 100 * (higherIsBetter ? 1 : -1);

    public static string Write(PeerTable table) => JsonSerializer.Serialize(table, Json);
    public static PeerTable? Read(string json) => JsonSerializer.Deserialize<PeerTable>(json, Json);

    [GeneratedRegex(@"\((R|TM|C)\)|®|™", RegexOptions.IgnoreCase)] private static partial Regex Marks();
    [GeneratedRegex(@"(\s+CPU)?\s+@\s+[\d.]+\s*GHz$|\s+\d+-Core\s+Processor$|\s+Processor$", RegexOptions.IgnoreCase)] private static partial Regex CpuTail();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}

/// <summary>
/// Every ranked run made with this copy of the app, whatever machine it ran on, one JSON line each in <c>Data/benchmarks/runs/yyyy-MM.jsonl</c>
/// (a month per file keeps each small, and a line is only ever appended, so a crash loses at most the line being written). These files are what the
/// shop gathers from its copies to build the published comparison lists; they never leave the machine on their own.
/// </summary>
public sealed class BenchmarkRunLog(string dataRoot)
{
    private readonly string _dir = Path.Combine(dataRoot, "benchmarks", "runs");
    private readonly object _lock = new();
    private List<BenchmarkRun>? _runs;

    public string Folder => _dir;
    /// <summary>The shop's marks on these runs (featured, overclocked), in <c>marks.json</c> beside the run files.</summary>
    public BenchmarkMarks Marks { get; } = new(Path.Combine(dataRoot, "benchmarks", "runs"));

    public void Append(BenchmarkRun run)
    {
        lock (_lock)
        {
            Load().Add(run);
            try
            {
                Directory.CreateDirectory(_dir);
                File.AppendAllText(Path.Combine(_dir, $"{run.At.UtcDateTime:yyyy-MM}.jsonl"), JsonSerializer.Serialize(run, BenchmarkPeers.Json) + "\n");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // kept for this session only
        }
    }

    /// <summary>This copy's runs of one list, newest first, with the shop's marks applied.</summary>
    public IReadOnlyList<BenchmarkRun> Of(string tableKey)
    {
        var marks = Marks.All();
        lock (_lock) return [.. Load().Where(r => r.Table == tableKey).OrderByDescending(r => r.At).Select(r => BenchmarkPeers.Marked(r, marks))];
    }

    /// <summary>The newest runs of one machine (its system hash), of every list.</summary>
    public IReadOnlyList<BenchmarkRun> Recent(string system, int count) { lock (_lock) return [.. Load().Where(r => r.System == system).OrderByDescending(r => r.At).Take(count)]; }

    public BenchmarkRun? Find(string id) { lock (_lock) return Load().FirstOrDefault(r => r.Id == id); }

    /// <summary>This copy's runs of one list as a list (the same rule as the published lists): its entries and its featured runs.</summary>
    public PeerTable? Table(string tableKey) => BenchmarkPeers.Aggregate(Of(tableKey), DateTimeOffset.UtcNow, Marks.All()).FirstOrDefault();
    public IReadOnlyList<PeerEntry> Entries(string tableKey) => Table(tableKey)?.Entries ?? [];

    private List<BenchmarkRun> Load() => _runs ??= ReadFolder(_dir).ToList();

    /// <summary>Every run in a folder of run files (and its sub-folders); a damaged line is skipped, not the file.</summary>
    public static IEnumerable<BenchmarkRun> ReadFolder(string folder)
    {
        if (!Directory.Exists(folder)) yield break;
        foreach (var file in Directory.EnumerateFiles(folder, "*.jsonl", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string[] lines;
            try { lines = File.ReadAllLines(file); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var line in lines)
            {
                BenchmarkRun? run = null;
                if (line.Length > 0) try { run = JsonSerializer.Deserialize<BenchmarkRun>(line, BenchmarkPeers.Json); } catch (JsonException) { }
                if (run is { Id.Length: > 0, Benchmark.Length: > 0 }) yield return run;
            }
        }
    }
}

/// <summary>The shop's marks on logged runs, by run id, in one small JSON file (the run log itself is only ever appended to). A mark taken back
/// is kept as "not featured" with its time, so gathering copies' marks together lets the latest word win.</summary>
public sealed class BenchmarkMarks(string folder)
{
    public const string FileName = "marks.json";
    private readonly object _lock = new();
    private Dictionary<string, BenchmarkMark>? _marks;
    private string File_ => Path.Combine(folder, FileName);

    public IReadOnlyDictionary<string, BenchmarkMark> All() { lock (_lock) return new Dictionary<string, BenchmarkMark>(Load()); }

    public void Set(string id, BenchmarkMark mark)
    {
        lock (_lock)
        {
            var marks = Load();
            marks[id] = mark;
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(File_ + ".tmp", JsonSerializer.Serialize(marks, BenchmarkPeers.Json));
                File.Move(File_ + ".tmp", File_, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private Dictionary<string, BenchmarkMark> Load() => _marks ??= Read(File_);

    /// <summary>The marks in a file, or none when it is missing or damaged.</summary>
    public static Dictionary<string, BenchmarkMark> Read(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, BenchmarkMark>>(File.ReadAllText(file), BenchmarkPeers.Json) ?? [] : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    /// <summary>Several copies' marks as one: for a run marked in more than one, the latest mark wins.</summary>
    public static Dictionary<string, BenchmarkMark> Merge(IEnumerable<IReadOnlyDictionary<string, BenchmarkMark>> sets)
    {
        var all = new Dictionary<string, BenchmarkMark>(StringComparer.Ordinal);
        foreach (var set in sets) foreach (var (id, m) in set) if (!all.TryGetValue(id, out var had) || m.At > had.At) all[id] = m;
        return all;
    }
}

/// <summary>The published comparison lists as downloaded into <c>Data/benchdb</c> (checked against the signed manifest before they land there).
/// A list is read from disk the first time it is needed and kept; <see cref="Reload"/> forgets them after a new download.</summary>
public sealed class PeerDatabase(string folder)
{
    private readonly Dictionary<string, PeerTable?> _tables = [];
    private readonly object _lock = new();
    public string Folder => folder;

    public PeerTable? Table(string tableKey)
    {
        lock (_lock)
        {
            if (_tables.TryGetValue(tableKey, out var t)) return t;
            string file = Path.Combine(folder, BenchmarkPeers.FileName(tableKey));
            try { t = File.Exists(file) ? BenchmarkPeers.Read(File.ReadAllText(file)) : null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { t = null; }
            if (t is not null && t.Key != tableKey) t = null;   // a file that is not the list it is named for is not shown
            return _tables[tableKey] = t;
        }
    }

    public void Reload() { lock (_lock) _tables.Clear(); }
}
