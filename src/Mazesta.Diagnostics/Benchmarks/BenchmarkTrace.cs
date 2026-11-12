using Mazesta.Core.Hardware; using Mazesta.Monitoring;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>One measured quantity over a run, a value per second (null where the monitor had no reading), for the part named by <see cref="Part"/> (Gpu, Cpu, Ram, Storage, Net).</summary>
public sealed record TraceSeries(string Key, string Part, string Unit, IReadOnlyList<double?> Values);

/// <summary>What the monitor read second by second while a run was under way, kept with the run so its chart can be drawn later. Each kind of benchmark
/// keeps the readings that matter to the part it measures (a drive's temperature for a storage run, the memory modules' for a memory run), plus the
/// run's own per-second figures (<see cref="BenchmarkResult.Series"/>, e.g. a drive's write speed). Only sensors the machine reports appear; nothing is filled in.</summary>
public static class BenchmarkTrace
{
    public const int MaxSeconds = 600;
    /// <summary>Several sensors of one role (the memory modules, the network adapters) are read as their hottest, or their sum, at each second.</summary>
    private enum How { First, Max, Sum }
    private sealed record Want(string Key, string Part, string Unit, HardwareKind Kind, SensorRole[] Roles, How How = How.First, double Scale = 1);

    private static readonly Want GpuLoad = new("Trace_Gpu_Load", "Gpu", "%", HardwareKind.Gpu, [SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D]);
    private static readonly Want CpuLoad = new("Trace_Cpu_Load", "Cpu", "%", HardwareKind.Cpu, [SensorRole.CpuTotalLoad]);

    private static Want[] For(HardwareKind component) => component switch
    {
        HardwareKind.Gpu =>
        [
            GpuLoad, new("Trace_Gpu_Clock", "Gpu", "MHz", HardwareKind.Gpu, [SensorRole.GpuCoreClock]), new("Trace_Gpu_Temp", "Gpu", "°C", HardwareKind.Gpu, [SensorRole.GpuCoreTemp]),
            new("Trace_Gpu_HotSpot", "Gpu", "°C", HardwareKind.Gpu, [SensorRole.GpuHotSpotTemp]), new("Trace_Gpu_VramTemp", "Gpu", "°C", HardwareKind.Gpu, [SensorRole.GpuVramTemp]),
            new("Trace_Gpu_Power", "Gpu", "W", HardwareKind.Gpu, [SensorRole.GpuPower]), CpuLoad,
        ],
        HardwareKind.Cpu =>
        [
            CpuLoad, new("Trace_Cpu_Clock", "Cpu", "MHz", HardwareKind.Cpu, [SensorRole.CpuEffectiveClockAverage, SensorRole.CpuCoreClockAverage]),
            new("Trace_Cpu_Temp", "Cpu", "°C", HardwareKind.Cpu, [SensorRole.CpuPackageTemp, SensorRole.CpuTctlTdie, SensorRole.CpuCoreTemp]),
            new("Trace_Cpu_Power", "Cpu", "W", HardwareKind.Cpu, [SensorRole.CpuPackagePower]), new("Trace_Cpu_Voltage", "Cpu", "V", HardwareKind.Cpu, [SensorRole.CpuVcore]),
        ],
        HardwareKind.Memory =>
        [
            new("Trace_Ram_Load", "Ram", "%", HardwareKind.Memory, [SensorRole.RamLoad]), new("Trace_Ram_Temp", "Ram", "°C", HardwareKind.Memory, [SensorRole.DimmTemp], How.Max), CpuLoad,
        ],
        HardwareKind.Storage => [new("Trace_Storage_Temp", "Storage", "°C", HardwareKind.Storage, [SensorRole.StorageTemp]), CpuLoad],
        HardwareKind.Network =>
        [
            new("Trace_Net_Down", "Net", "MB/s", HardwareKind.Network, [SensorRole.NetDownload], How.Sum, 1e-6), new("Trace_Net_Up", "Net", "MB/s", HardwareKind.Network, [SensorRole.NetUpload], How.Sum, 1e-6),
            new("Trace_Net_Util", "Net", "%", HardwareKind.Network, [SensorRole.NetUtilization], How.Max),
        ],
        _ => [GpuLoad, CpuLoad],
    };

    /// <summary>The series for the window <paramref name="from"/>..<paramref name="to"/> of a benchmark measuring <paramref name="component"/>, followed by the run's own
    /// <paramref name="own"/> series, or null when there is none. <paramref name="node"/> narrows the measured part's sensors to the device the run used
    /// (the drive it wrote to, the card it drew on); the sensors of other kinds are not narrowed. The run must have lasted two seconds for the monitor's readings to count.</summary>
    public static IReadOnlyList<TraceSeries>? Capture(PollingEngine? engine, DateTimeOffset from, DateTimeOffset to, HardwareKind component = HardwareKind.Gpu,
        Func<HardwareNode, bool>? node = null, IReadOnlyList<TraceSeries>? own = null)
    {
        var list = new List<TraceSeries>();
        if (engine is not null)
        {
            int start = engine.History.SecondsSinceEpoch(from), end = Math.Min(engine.History.SecondsSinceEpoch(to), start + MaxSeconds - 1);
            if (end - start >= 1)
                foreach (var w in For(component))
                    foreach (var role in w.Roles)
                    {
                        var sensors = engine.Hardware.Where(n => n.Kind == w.Kind && (w.Kind != component || node is null || node(n)) && (w.Kind != HardwareKind.Memory || w.Roles[0] == SensorRole.DimmTemp || n.ParentId is null))
                            .SelectMany(n => n.Sensors).Where(s => s.Role == role).ToList();
                        var values = new double?[end - start + 1]; bool any = false;
                        foreach (var s in sensors)
                        {
                            var raw = engine.History.GetRaw(s.Id);
                            for (int i = 0; i < raw.Seconds.Length; i++)
                            {
                                if (raw.Seconds[i] < start || raw.Seconds[i] > end || float.IsNaN(raw.Values[i])) continue;
                                int at = raw.Seconds[i] - start; double v = raw.Values[i] * w.Scale;
                                values[at] = w.How switch { How.Max => Math.Max(values[at] ?? double.MinValue, v), How.Sum => (values[at] ?? 0) + v, _ => v };
                                any = true;
                            }
                            if (any && w.How == How.First) break;   // the first sensor of the role that has readings (the first card)
                        }
                        if (!any) continue;
                        for (int i = 0; i < values.Length; i++) if (values[i] is { } x) values[i] = Math.Round(x, w.Scale < 1 ? 2 : 1);
                        list.Add(new TraceSeries(w.Key, w.Part, w.Unit, values)); break;
                    }
        }
        if (own is not null) list.AddRange(own);
        return list.Count == 0 ? null : list;
    }
}
