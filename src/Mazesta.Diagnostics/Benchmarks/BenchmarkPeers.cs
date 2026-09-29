using System.Security.Cryptography; using System.Text; using System.Text.Json; using System.Text.Json.Serialization; using System.Text.RegularExpressions;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>
/// One ranked benchmark run as the shop keeps it for the comparison lists: which benchmark (and its workload version and settings), the part it
/// measured (a CPU, GPU or drive model), the machine, and the headline number. <see cref="System"/> is an opaque hash of the machine and its
/// parts, so the published lists can count distinct systems without naming them; <see cref="Machine"/> and <see cref="Spec"/> stay in the shop's
/// own files and never go into the lists.
/// </summary>
public sealed record BenchmarkRun(string Id, DateTimeOffset At, string Benchmark, int Version, string Settings, string Part, string System, string Machine, string Spec,
    double Value, string Unit, string? App = null, IReadOnlyList<BenchmarkMetric>? Metrics = null)
{
    [JsonIgnore] public string Table => BenchmarkPeers.TableKey(Benchmark, Version, Settings);
}

/// <summary>One part model in a comparison list: the median of its systems' best runs (each system counts once, however often it ran), the
/// fastest of them, how many systems and runs it stands for, and the latest run.</summary>
public sealed record PeerEntry(string Part, double Median, double Best, int Systems, int Runs, DateTimeOffset Last);

/// <summary>The comparison list of one benchmark, workload version and settings: one entry per part model, best first. The published database is
/// one such file per list, so a client fetches only the lists that changed, and a list grows with the number of models, not of runs.</summary>
public sealed record PeerTable(string Key, string Benchmark, int Version, string Settings, string Unit, bool HigherIsBetter, DateTimeOffset Built, IReadOnlyList<PeerEntry> Entries);

/// <summary>A list entry against this system's result. <see cref="DiffPercent"/> is how much this result is above (+) or below (−) the entry,
/// signed so that positive is always better; <see cref="Local"/> marks a model known only from this copy's own runs, not yet published.</summary>
public sealed record PeerRow(PeerEntry Entry, double DiffPercent, bool Local, bool Same);

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
    public static IReadOnlyList<PeerTable> Aggregate(IEnumerable<BenchmarkRun> runs, DateTimeOffset built)
    {
        var tables = new List<PeerTable>();
        foreach (var list in runs.Where(r => r.Part.Length > 0 && r.Value > 0 && double.IsFinite(r.Value)).GroupBy(r => r.Table, StringComparer.Ordinal))
        {
            var first = list.First();
            bool higher = BenchmarkRecords.Headline(first.Benchmark)?.HigherIsBetter ?? true;
            string unit = list.GroupBy(r => r.Unit).OrderByDescending(g => g.Count()).First().Key;
            var entries = list.Where(r => r.Unit == unit).GroupBy(r => PartName(r.Part), StringComparer.OrdinalIgnoreCase)
                .Select(part => Entry(part.Key, part, higher)).ToList();
            tables.Add(new PeerTable(list.Key, first.Benchmark, first.Version, first.Settings, unit, higher, built, Sort(entries, higher)));
        }
        return tables;
    }

    private static PeerEntry Entry(string part, IEnumerable<BenchmarkRun> runs, bool higher)
    {
        var all = runs.ToList();
        var perSystem = all.GroupBy(r => r.System).Select(s => higher ? s.Max(r => r.Value) : s.Min(r => r.Value)).Order().ToList();
        double median = perSystem.Count % 2 == 1 ? perSystem[perSystem.Count / 2] : (perSystem[perSystem.Count / 2 - 1] + perSystem[perSystem.Count / 2]) / 2;
        return new PeerEntry(part, median, higher ? perSystem[^1] : perSystem[0], perSystem.Count, all.Count, all.Max(r => r.At));
    }

    private static List<PeerEntry> Sort(IEnumerable<PeerEntry> entries, bool higher)
        => [.. higher ? entries.OrderByDescending(e => e.Median).ThenBy(e => e.Part, StringComparer.OrdinalIgnoreCase) : entries.OrderBy(e => e.Median).ThenBy(e => e.Part, StringComparer.OrdinalIgnoreCase)];

    /// <summary>This result against a list: the published entries, plus the models only this copy has measured (marked local; a model in both is
    /// taken from the published list, so a run is never counted twice). The difference is taken against each entry's median.</summary>
    public static PeerRanking Rank(PeerTable? table, IReadOnlyList<PeerEntry> local, double mine, string? myPart, bool higherIsBetter)
    {
        var published = table?.Entries ?? [];
        var names = new HashSet<string>(published.Select(e => e.Part), StringComparer.OrdinalIgnoreCase);
        var merged = published.Select(e => (e, false)).Concat(local.Where(e => !names.Contains(e.Part)).Select(e => (e, true)));
        string me = PartName(myPart);
        var rows = Sort(merged.Select(x => x.e), higherIsBetter).Select(e => new PeerRow(e, Diff(mine, e.Median, higherIsBetter), !names.Contains(e.Part),
            me.Length > 0 && string.Equals(e.Part, me, StringComparison.OrdinalIgnoreCase))).ToList();
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

    /// <summary>This copy's runs of one list, newest first.</summary>
    public IReadOnlyList<BenchmarkRun> Of(string tableKey) { lock (_lock) return [.. Load().Where(r => r.Table == tableKey).OrderByDescending(r => r.At)]; }

    /// <summary>This copy's runs of one list as list entries (the same rule as the published lists).</summary>
    public IReadOnlyList<PeerEntry> Entries(string tableKey) => BenchmarkPeers.Aggregate(Of(tableKey), DateTimeOffset.UtcNow).FirstOrDefault()?.Entries ?? [];

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
