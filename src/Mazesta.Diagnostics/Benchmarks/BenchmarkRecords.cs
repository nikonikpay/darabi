using System.Text.Json; using System.Text.Json.Serialization;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>A kept benchmark result: when it was measured, its headline number, and every number it measured.</summary>
public sealed record BenchmarkRecord(DateTimeOffset At, string Key, double Value, string Unit, IReadOnlyList<BenchmarkMetric> Metrics);

/// <summary>What a new run is against the best kept one on this system. <see cref="Previous"/> is null the first time; <see cref="ChangePercent"/>
/// is signed so that positive is always better (a latency that fell is positive). <see cref="Saved"/> is true only when the run became the record.</summary>
public sealed record BenchmarkComparison(BenchmarkRecord Current, BenchmarkRecord? Previous, double? ChangePercent, bool Saved);

/// <summary>The part a benchmark's result belongs to when it is compared with other systems: a CPU benchmark is compared CPU model with CPU
/// model, a storage one drive model with drive model. <see cref="None"/> is never compared with others (an internet speed measures the line).</summary>
public enum PeerPart { None, Cpu, Memory, Gpu, Drive }

/// <summary>The one number a benchmark is ranked by, and which way is better. Everything else it measures is kept alongside but not ranked.
/// <see cref="Version"/> is the benchmark's workload version: results of different versions are never compared, so it is raised whenever a
/// benchmark's work changes (another matrix size, another scene) and the shared comparison lists start over for it.</summary>
public sealed record HeadlineMetric(string Key, bool HigherIsBetter, PeerPart Part = PeerPart.None, int Version = 1);

/// <summary>
/// The best result of each benchmark on each system, and nothing else: a run is kept only when it beats (or first sets) the record, so a slower
/// run never replaces a faster one, whether it happens now or after the app restarts. A benchmark is ranked by its headline number only; a run
/// that is not complete, or did not measure its headline, is never compared and never kept. Results of different options (another drive,
/// another GPU) are different records. Nothing leaves the machine: the file is <c>Data/benchmarks/records.json</c>.
/// </summary>
public sealed class BenchmarkRecords
{
    private static readonly Dictionary<string, HeadlineMetric> Headlines = new()
    {
        ["bench.cpu.single"] = new("Bench_Cpu_Gflops", true, PeerPart.Cpu), ["bench.cpu.multi"] = new("Bench_Cpu_Gflops", true, PeerPart.Cpu),
        ["bench.memory"] = new("Bench_Mem_Read", true, PeerPart.Memory, Version: 3), ["bench.storage"] = new("Bench_Storage_SeqRead", true, PeerPart.Drive),
        ["bench.gpu.d3d"] = new("Bench_Gpu_Fps", true, PeerPart.Gpu), ["bench.gpu.rt"] = new("Bench_Gpu_Rt_Fps", true, PeerPart.Gpu), ["bench.gpu.ai"] = new("Bench_Gpu_Ai_Fp32", true, PeerPart.Gpu),
        ["bench.gpu.scene.d3d"] = new("Bench_Gpu_Scene_Fps", true, PeerPart.Gpu, 2), ["bench.gpu.scene.rt"] = new("Bench_Gpu_Scene_Fps", true, PeerPart.Gpu, 2),   // v2: the courtyard V4 scene; RT at 4 rays a pixel
        ["bench.network.internet"] = new("Bench_Net_Download", true),
    };
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly string _file; private readonly object _lock = new();
    private Dictionary<string, SystemRecords> _systems;

    public BenchmarkRecords(string dataRoot)
    {
        _file = Path.Combine(dataRoot, "benchmarks", "records.json");
        _systems = Load(_file);
    }

    public static HeadlineMetric? Headline(string benchmarkId) => Headlines.GetValueOrDefault(benchmarkId);

    /// <summary>The record key: the benchmark and the options it ran with, in a fixed order.</summary>
    public static string RecordKey(string benchmarkId, IReadOnlyDictionary<string, string>? options)
        => options is null || options.Count == 0 ? benchmarkId : benchmarkId + "|" + string.Join("|", options.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => $"{o.Key}={o.Value}"));

    public BenchmarkRecord? Best(string system, string recordKey)
    {
        lock (_lock) return _systems.TryGetValue(system, out var s) ? s.Best.GetValueOrDefault(recordKey) : null;
    }

    /// <summary>Versions up to 0.6 named a system before its hardware was read, so their records sit under "<c>machine | </c>" with no CPU or GPU.
    /// They are moved to the machine's full key once it is known, where that key has no record of the same benchmark yet.</summary>
    public void AdoptUnnamed(string system, string machine)
    {
        string old = machine + " | ";
        lock (_lock)
        {
            if (old == system || !_systems.Remove(old, out var legacy)) return;
            var s = _systems.TryGetValue(system, out var found) ? found : _systems[system] = new SystemRecords(legacy.Name, []);
            foreach (var (key, record) in legacy.Best) s.Best.TryAdd(key, record);
            Save();
        }
    }

    /// <summary>Compares a finished run with the record and keeps it if it is better. Null when the run cannot be ranked.</summary>
    public BenchmarkComparison? Offer(string system, string systemName, string recordKey, BenchmarkResult result)
    {
        if (Rank(result) is not { } current) return null;
        var headline = Headlines[result.Id.Value];
        lock (_lock)
        {
            var previous = _systems.TryGetValue(system, out var s) ? s.Best.GetValueOrDefault(recordKey) : null;
            var comparison = Compare(current, previous, headline.HigherIsBetter);
            if (comparison.Saved)
            {
                s ??= _systems[system] = new SystemRecords(systemName, []);
                s.Best[recordKey] = current;
                Save();
            }
            return comparison;
        }
    }

    /// <summary>The run as a record, or null when it is not complete or lacks its headline number (unranked runs are never kept).</summary>
    internal static BenchmarkRecord? Rank(BenchmarkResult result)
    {
        if (result.Status != BenchmarkStatus.Completed || Headline(result.Id.Value) is not { } headline) return null;
        var m = result.Metrics.FirstOrDefault(x => x.Key == headline.Key);
        return m is null || double.IsNaN(m.Value) || double.IsInfinity(m.Value) || m.Value <= 0 ? null : new BenchmarkRecord(result.FinishedAt, m.Key, m.Value, m.Unit, result.Metrics);
    }

    /// <summary>Pure: the comparison of a ranked run with the kept record. An equal result does not replace the older record.</summary>
    internal static BenchmarkComparison Compare(BenchmarkRecord current, BenchmarkRecord? previous, bool higherIsBetter)
    {
        if (previous is null || previous.Key != current.Key || previous.Value <= 0) return new(current, null, null, true);
        double change = (current.Value - previous.Value) / previous.Value * 100 * (higherIsBetter ? 1 : -1);
        return new(current, previous, change, change > 0);
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            string tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_systems, Json));
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // a record that cannot be written is only lost, never half-written
    }

    private static Dictionary<string, SystemRecords> Load(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, SystemRecords>>(File.ReadAllText(file), Json) ?? [] : []; }
        catch (JsonException)
        {
            // A damaged file is set aside, not overwritten by the next record, so the old records can still be recovered by hand.
            try { File.Move(file, file + $".damaged-{DateTime.Now:yyyyMMdd-HHmmss}"); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    private sealed record SystemRecords(string Name, Dictionary<string, BenchmarkRecord> Best);
}
