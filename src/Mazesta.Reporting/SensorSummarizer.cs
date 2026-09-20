using Mazesta.Core.Hardware; using Mazesta.Monitoring;
namespace Mazesta.Reporting;

/// <summary>Reads the sensors that matter for a test session out of the monitor's recorded history for the session window - never a fresh reading, never a guess.</summary>
public static class SensorSummarizer
{
    public const int MaxTracePoints = 120;

    private static readonly SensorRole[] KeyRoles =
    [
        SensorRole.CpuPackageTemp, SensorRole.CpuTctlTdie, SensorRole.CpuTotalLoad, SensorRole.CpuEffectiveClockAverage, SensorRole.CpuCoreClockAverage, SensorRole.CpuPackagePower, SensorRole.CpuVcore,
        SensorRole.GpuCoreTemp, SensorRole.GpuHotSpotTemp, SensorRole.GpuVramTemp, SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D, SensorRole.GpuCoreClock, SensorRole.GpuMemoryClock, SensorRole.GpuPower, SensorRole.GpuFanRpm,
        SensorRole.RamLoad, SensorRole.BoardTemp, SensorRole.ChipsetTemp, SensorRole.StorageTemp, SensorRole.NetUtilization
    ];

    public static IReadOnlyList<SensorSummary> Summarize(PollingEngine engine, DateTimeOffset from, DateTimeOffset to)
    {
        int startSec = engine.History.SecondsSinceEpoch(from), endSec = engine.History.SecondsSinceEpoch(to);
        var result = new List<SensorSummary>();
        foreach (var role in KeyRoles)
            foreach (var node in engine.Hardware)
                foreach (var def in node.Sensors.Where(s => s.Role == role))
                    if (Summarize(def, node.Name, engine.History.GetRaw(def.Id), startSec, endSec) is { } summary) result.Add(summary);
        return result;
    }

    /// <summary>Statistics and a down-sampled trace of the valid samples inside [startSec, endSec]; null when there are none.</summary>
    internal static SensorSummary? Summarize(SensorDefinition def, string hardwareName, RawSeries raw, int startSec, int endSec)
    {
        var points = new List<(int Sec, double Value)>();
        for (int i = 0; i < raw.Seconds.Length; i++)
            if (raw.Seconds[i] >= startSec && raw.Seconds[i] <= endSec && !float.IsNaN(raw.Values[i])) points.Add((raw.Seconds[i], raw.Values[i]));
        if (points.Count == 0) return null;
        int step = (int)Math.Ceiling(points.Count / (double)MaxTracePoints);
        var trace = points.Where((_, i) => i % step == 0).Select(p => new TracePoint(p.Sec - startSec, p.Value)).ToList();
        return new(def.Id.Value, hardwareName, def.Name, def.Kind.ToString(), Units.Symbol(def.Unit), points.Min(p => p.Value), points.Average(p => p.Value), points.Max(p => p.Value), points.Count, trace);
    }
}
