using Mazesta.Core.Hardware; using Mazesta.Core.Health.Checkup; using Mazesta.Monitoring;
namespace Mazesta.Desktop.Services;

/// <summary>
/// Turns what the monitor recorded during a run into the checkup's series: each second of the run's own window, one value per quantity (the
/// mean or the highest of several sensors, as the quantity needs). A sensor with no reading in a second adds nothing for that second.
/// </summary>
internal static class CheckupTraces
{
    private const string DistanceSuffix = " Distance to TjMax";

    public static CpuRunTrace Cpu(PollingEngine engine, DateTimeOffset from, DateTimeOffset to, bool allThreads, int? baseClockMhz, bool? onBattery, CpuSpec? spec)
    {
        var (a, b) = (engine.History.SecondsSinceEpoch(from), engine.History.SecondsSinceEpoch(to));
        var cpu = engine.Hardware.Where(n => n.Kind == HardwareKind.Cpu).SelectMany(n => n.Sensors).ToList();
        List<SensorDefinition> Role(SensorRole r) => [.. cpu.Where(s => s.Role == r)];

        Series? temp = null;
        foreach (var role in new[] { SensorRole.CpuPackageTemp, SensorRole.CpuTctlTdie, SensorRole.CpuCoreTemp })
            if (Combine(engine, Role(role), a, b, v => v.Max()) is { Count: > 0 } s) { temp = s; break; }
        // Per-core clocks: AMD's effective clocks where there are any (they count only the time a core ran), else the cores' clocks. A hybrid CPU's
        // all-core clock is its P-cores', which is what its base clock is given for.
        // A one-thread run reads the cores' own clocks instead: Windows moves the thread between cores, so no core's effective clock over a second
        // shows the whole of it, while the fastest core's clock shows the boost it reached.
        var cores = allThreads && Role(SensorRole.CpuEffectiveClock) is { Count: > 0 } effective ? effective : Role(SensorRole.CpuCoreClock) is { Count: > 0 } own ? own : Role(SensorRole.CpuEffectiveClock);
        if (allThreads && cores.Any(s => s.Name.StartsWith("P-Core", StringComparison.Ordinal))) cores = [.. cores.Where(s => s.Name.StartsWith("P-Core", StringComparison.Ordinal))];
        var clock = Combine(engine, cores, a, b, allThreads ? v => v.Average() : v => v.Max());
        var power = Combine(engine, Role(SensorRole.CpuPackagePower).Take(1), a, b, v => v.First());
        var load = Combine(engine, Role(SensorRole.CpuTotalLoad).Take(1), a, b, v => v.First());
        return new(allThreads, (to - from).TotalSeconds, temp, clock, power, load, TjMax(engine, cpu, b), baseClockMhz, onBattery, spec);
    }

    /// <summary>The limit an Intel CPU reports: a core's temperature plus its distance to the limit, read in the same poll. Every sample so far is
    /// used (the limit does not change); a distance of 0 reads as invalid, which only leaves those seconds out.</summary>
    internal static double? TjMax(PollingEngine engine, IReadOnlyList<SensorDefinition> cpu, int until)
    {
        var sums = new List<double>();
        foreach (var d in cpu.Where(s => s.Role == SensorRole.CpuTjMaxDistance && s.Name.EndsWith(DistanceSuffix, StringComparison.Ordinal)))
        {
            string coreName = d.Name[..^DistanceSuffix.Length];
            if (cpu.FirstOrDefault(s => s.Role == SensorRole.CpuCoreTemp && s.Hardware == d.Hardware && s.Name == coreName) is not { } core) continue;
            var dist = Values(engine, d, 0, until); var temp = Values(engine, core, 0, until);
            foreach (var (sec, v) in dist) if (temp.TryGetValue(sec, out var t)) sums.Add(t + v);
        }
        if (sums.Count < 3) return null;
        sums.Sort();
        return Math.Round(sums[sums.Count / 2]);
    }

    public static GpuRunTrace Gpu(PollingEngine engine, DateTimeOffset from, DateTimeOffset to, Func<HardwareNode, bool> node, string? name, GpuThrottleCounts? throttle, GpuLink? link)
    {
        var (a, b) = (engine.History.SecondsSinceEpoch(from), engine.History.SecondsSinceEpoch(to));
        var nodes = engine.Hardware.Where(n => n.Kind == HardwareKind.Gpu && node(n)).ToList();
        var sensors = nodes.SelectMany(n => n.Sensors).ToList();
        Series? First(params SensorRole[] roles)
        {
            foreach (var r in roles) if (Combine(engine, sensors.Where(s => s.Role == r).Take(1), a, b, v => v.First()) is { Count: > 0 } s) return s;
            return null;
        }
        var vendor = nodes.FirstOrDefault(n => n.ParentId is null)?.Vendor ?? nodes.FirstOrDefault()?.Vendor ?? HardwareVendor.Unknown;
        return new(name, vendor, First(SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D), First(SensorRole.GpuCoreTemp), First(SensorRole.GpuHotSpotTemp), First(SensorRole.GpuPower), throttle, link);
    }

    private static Series? Combine(PollingEngine engine, IEnumerable<SensorDefinition> sensors, int from, int to, Func<List<double>, double> merge)
    {
        var bySecond = new SortedDictionary<int, List<double>>();
        foreach (var s in sensors)
            foreach (var (sec, v) in Values(engine, s, from, to))
                (bySecond.TryGetValue(sec, out var list) ? list : bySecond[sec] = []).Add(v);
        return bySecond.Count == 0 ? null : Series.Of(bySecond.Select(kv => ((double)(kv.Key - from), merge(kv.Value))));
    }

    private static Dictionary<int, double> Values(PollingEngine engine, SensorDefinition s, int from, int to)
    {
        var raw = engine.History.GetRaw(s.Id); var values = new Dictionary<int, double>();
        for (int i = 0; i < raw.Seconds.Length; i++)
            if (raw.Seconds[i] >= from && raw.Seconds[i] <= to && !float.IsNaN(raw.Values[i])) values[raw.Seconds[i]] = raw.Values[i];
        return values;
    }
}
