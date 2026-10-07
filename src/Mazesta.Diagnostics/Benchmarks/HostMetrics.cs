using System.Globalization; using System.Text.RegularExpressions; using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Evidence; using Mazesta.Monitoring;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>
/// What the processor and the RAM did during a run that is mostly the graphics card's, read from the monitor over the run's own time: the clocks
/// (the peak, the average, and apart for each kind of core - performance and efficiency, and each CCD of a Ryzen), the load, the power (its peak is the
/// spike), and the RAM in use, at what speed and CL. So a result says where a system was held back: a card waiting on a slow processor shows in the
/// processor's clock and load. A figure the machine does not report is left out, never zero.
/// </summary>
public static class HostMetrics
{
    private static readonly Regex CoreName = new(@"^(?:P-|E-)?Core #(\d+)$", RegexOptions.Compiled);
    private const int MaxCcds = 16;

    public static void AddCpu(List<BenchmarkMetric> metrics, TestExecutionRequest request, DateTimeOffset from, DateTimeOffset to)
    {
        metrics.AddFirst(request, HardwareKind.Cpu, from, to, "Bench_Cpu_Clock", Unit.MegaHertz, false, null, SensorRole.CpuEffectiveClockAverage, SensorRole.CpuCoreClockAverage);
        metrics.AddFirst(request, HardwareKind.Cpu, from, to, "Bench_Cpu_ClockPeak", Unit.MegaHertz, true, null, SensorRole.CpuEffectiveClock, SensorRole.CpuCoreClock);
        bool IsP(SensorDefinition s) => s.Name.StartsWith("P-Core", StringComparison.Ordinal);
        bool IsE(SensorDefinition s) => s.Name.StartsWith("E-Core", StringComparison.Ordinal);
        metrics.AddSensor(request, HardwareKind.Cpu, SensorRole.CpuCoreClock, from, to, "Bench_Cpu_PClock", Unit.MegaHertz, sensor: IsP);
        metrics.AddSensor(request, HardwareKind.Cpu, SensorRole.CpuCoreClock, from, to, "Bench_Cpu_PClockMax", Unit.MegaHertz, peak: true, sensor: IsP);
        metrics.AddSensor(request, HardwareKind.Cpu, SensorRole.CpuCoreClock, from, to, "Bench_Cpu_EClock", Unit.MegaHertz, sensor: IsE);
        metrics.AddSensor(request, HardwareKind.Cpu, SensorRole.CpuCoreClock, from, to, "Bench_Cpu_EClockMax", Unit.MegaHertz, peak: true, sensor: IsE);
        // A Ryzen's CCDs are told apart by the cores that share a level-3 cache; each one's cores are the monitor's "Core #n" (n counted from 1, in Windows' order).
        var groups = CpuTopology.CacheGroups; var cores = CpuTopology.Cores;
        if (groups.Count is > 1 and <= MaxCcds && metrics.All(m => m.Key != "Bench_Cpu_PClock"))
            for (int g = 0; g < groups.Count; g++)
            {
                var (group, mask) = groups[g];
                var wanted = cores.Where(c => c.Group == group && (c.Mask & mask) != 0).Select(c => c.Index + 1).ToHashSet();
                metrics.AddSensor(request, HardwareKind.Cpu, SensorRole.CpuCoreClock, from, to, $"Bench_Cpu_Ccd{g + 1}Clock", Unit.MegaHertz,
                    sensor: s => CoreName.Match(s.Name) is { Success: true } m && wanted.Contains(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
            }
        metrics.AddSensor(request, HardwareKind.Cpu, SensorRole.CpuTotalLoad, from, to, "Bench_Cpu_Load", Unit.Percent);
        metrics.AddSensor(request, HardwareKind.Cpu, SensorRole.CpuTotalLoad, from, to, "Bench_Cpu_LoadMax", Unit.Percent, peak: true);
        metrics.AddSensor(request, HardwareKind.Cpu, SensorRole.CpuPackagePower, from, to, "Bench_Cpu_Power", Unit.Watt);
        metrics.AddSensor(request, HardwareKind.Cpu, SensorRole.CpuPackagePower, from, to, "Bench_Cpu_PowerMax", Unit.Watt, peak: true);
        metrics.AddSensor(request, HardwareKind.Cpu, SensorRole.CpuVcore, from, to, "Bench_Cpu_Vcore", Unit.Volt);
        if (SensorEvidence.CpuTemperature(request.Engine, from, to) is { } temp)
            metrics.AddRange([new("Bench_Cpu_TempAvg", temp.Average, Units.Symbol(Unit.Celsius)), new("Bench_Cpu_TempMax", temp.Max, Units.Symbol(Unit.Celsius))]);
    }

    /// <summary>The CCD (counted from 1: the cores that share a level-3 cache) of the core a CPU sensor is named after ("Core #7", "Core #7 (SMU)"), or null: for a sensor
    /// of no single core, and for a processor with one cache for all its cores (every Intel one, and a Ryzen of one CCD).</summary>
    public static int? CcdOfCore(string sensorName)
    {
        var groups = CpuTopology.CacheGroups;
        if (groups.Count is < 2 or > MaxCcds || CoreNumber.Match(sensorName) is not { Success: true } m) return null;
        int index = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 1;
        var core = CpuTopology.Cores.FirstOrDefault(c => c.Index == index);
        if (core is null) return null;
        for (int g = 0; g < groups.Count; g++) if (groups[g].Group == core.Group && (groups[g].Mask & core.Mask) != 0) return g + 1;
        return null;
    }
    private static readonly Regex CoreNumber = new(@"^Core #(\d+)", RegexOptions.Compiled);

    /// <summary>The RAM in use over the run (GB, average and peak), its load, and what it runs at; <paramref name="facts"/> may be null.</summary>
    public static void AddRam(List<BenchmarkMetric> metrics, TestExecutionRequest request, DateTimeOffset from, DateTimeOffset to, MemoryFacts? facts)
    {
        metrics.AddSensor(request, HardwareKind.Memory, SensorRole.RamLoad, from, to, "Bench_Ram_Load", Unit.Percent);
        if (facts?.SpeedMts is { } mts) metrics.Add(new("Bench_Ram_Speed", mts, "MT/s"));
        if (facts?.CasLatency is { } cl) metrics.Add(new("Bench_Ram_Cl", cl, "CL"));
    }

    /// <summary>The RAM in use over the window in gigabytes, whichever unit the sensor reports in.</summary>
    public static SensorStat? UsedGb(PollingEngine? engine, DateTimeOffset from, DateTimeOffset to) => Gigabytes(engine, HardwareKind.Memory, SensorRole.RamUsed, null, from, to);

    /// <summary>A memory sensor's readings over the window (the RAM in use, a card's video memory in use) in gigabytes, whichever of MB and GB it reports in.</summary>
    public static SensorStat? Gigabytes(PollingEngine? engine, HardwareKind kind, SensorRole role, Func<HardwareNode, bool>? node, DateTimeOffset from, DateTimeOffset to)
    {
        if (engine is null) return null;
        int start = engine.History.SecondsSinceEpoch(from), end = engine.History.SecondsSinceEpoch(to); var samples = new List<double>();
        foreach (var s in engine.Hardware.Where(n => n.Kind == kind && (kind == HardwareKind.Memory ? n.ParentId is null : node is null || node(n))).SelectMany(n => n.Sensors).Where(s => s.Role == role))
        {
            double factor = s.Unit == Unit.Megabyte ? 1 / 1024.0 : s.Unit == Unit.Gigabyte ? 1 : 0;
            if (factor == 0) continue;
            var raw = engine.History.GetRaw(s.Id);
            for (int i = 0; i < raw.Seconds.Length; i++) if (raw.Seconds[i] >= start && raw.Seconds[i] <= end && !float.IsNaN(raw.Values[i])) samples.Add(raw.Values[i] * factor);
        }
        return samples.Count == 0 ? null : new(samples.Average(), samples.Max(), samples.Count);
    }

    /// <summary>The same facts as lines of a test's evidence (a test has no metric list).</summary>
    public static string[] Evidence(TestExecutionRequest request, DateTimeOffset from, DateTimeOffset to, MemoryFacts? facts)
    {
        var metrics = new List<BenchmarkMetric>();
        AddCpu(metrics, request, from, to); AddRam(metrics, request, from, to, facts);
        string? Get(string key, string label, string format = "F0") => metrics.FirstOrDefault(m => m.Key == key) is { } m ? $"{label} {m.Value.ToString(format, CultureInfo.InvariantCulture)} {m.Unit}" : null;
        string?[] parts =
        [
            Get("Bench_Cpu_ClockPeak", "CPU clock peak"), Get("Bench_Cpu_Clock", "CPU clock avg"), Get("Bench_Cpu_Load", "CPU load avg"), Get("Bench_Cpu_PowerMax", "CPU power peak"),
            Get("Bench_Ram_Speed", "RAM speed"), Get("Bench_Ram_Cl", "RAM CAS latency (profile at that speed)"),
        ];
        return [.. parts.OfType<string>()];
    }
}
