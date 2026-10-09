using Mazesta.Core.Hardware; using Mazesta.Monitoring;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>One measured quantity over a run, a value per second (null where the monitor had no reading), for the part named by <see cref="Part"/> (Gpu, Cpu, Ram).</summary>
public sealed record TraceSeries(string Key, string Part, string Unit, IReadOnlyList<double?> Values);

/// <summary>What the monitor read second by second while a run was under way, kept with the run so its chart can be drawn later. Only sensors the machine reports
/// appear; nothing is filled in.</summary>
public static class BenchmarkTrace
{
    public const int MaxSeconds = 600;
    private static readonly (string Key, string Part, string Unit, HardwareKind Kind, SensorRole[] Roles)[] Wanted =
    [
        ("Trace_Gpu_Load", "Gpu", "%", HardwareKind.Gpu, [SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D]),
        ("Trace_Gpu_Clock", "Gpu", "MHz", HardwareKind.Gpu, [SensorRole.GpuCoreClock]),
        ("Trace_Gpu_Temp", "Gpu", "°C", HardwareKind.Gpu, [SensorRole.GpuCoreTemp]),
        ("Trace_Gpu_Power", "Gpu", "W", HardwareKind.Gpu, [SensorRole.GpuPower]),
        ("Trace_Cpu_Load", "Cpu", "%", HardwareKind.Cpu, [SensorRole.CpuTotalLoad]),
        ("Trace_Cpu_Clock", "Cpu", "MHz", HardwareKind.Cpu, [SensorRole.CpuEffectiveClockAverage, SensorRole.CpuCoreClockAverage]),
        ("Trace_Cpu_Power", "Cpu", "W", HardwareKind.Cpu, [SensorRole.CpuPackagePower]),
        ("Trace_Ram_Load", "Ram", "%", HardwareKind.Memory, [SensorRole.RamLoad]),
    ];

    /// <summary>The series for the window <paramref name="from"/>..<paramref name="to"/>, or null when the engine has none or the run was shorter than two seconds.</summary>
    public static IReadOnlyList<TraceSeries>? Capture(PollingEngine? engine, DateTimeOffset from, DateTimeOffset to)
    {
        if (engine is null) return null;
        int start = engine.History.SecondsSinceEpoch(from), end = Math.Min(engine.History.SecondsSinceEpoch(to), start + MaxSeconds - 1);
        if (end - start < 1) return null;
        var list = new List<TraceSeries>();
        foreach (var (key, part, unit, kind, roles) in Wanted)
            foreach (var role in roles)
            {
                var values = new double?[end - start + 1]; bool any = false;
                foreach (var s in engine.Hardware.Where(n => n.Kind == kind && (kind == HardwareKind.Memory ? n.ParentId is null : true)).SelectMany(n => n.Sensors).Where(s => s.Role == role))
                {
                    var raw = engine.History.GetRaw(s.Id);
                    for (int i = 0; i < raw.Seconds.Length; i++)
                        if (raw.Seconds[i] >= start && raw.Seconds[i] <= end && !float.IsNaN(raw.Values[i])) { values[raw.Seconds[i] - start] = Math.Round(raw.Values[i], 1); any = true; }
                    if (any) break;   // the first sensor of the role that has readings (the first card)
                }
                if (any) { list.Add(new TraceSeries(key, part, unit, values)); break; }
            }
        return list.Count == 0 ? null : list;
    }
}
