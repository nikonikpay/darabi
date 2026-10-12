using Mazesta.Core.Hardware;
namespace Mazesta.Core.Health;

/// <summary>Reduces one poll to the few numbers the health rules use, by sensor <i>role</i> (never by name). Readings that are not trustworthy are ignored, so a stale or invalid value cannot raise an alert.</summary>
public static class HealthSampler
{
    public static HealthSample From(IEnumerable<HardwareNode> nodes, IReadOnlyList<SensorReading> readings)
    {
        var byId = readings.Where(r => r.Value is not null && r.Quality == DataQuality.Ok).ToDictionary(r => r.Id, r => r.Value!.Value);
        // An APU's graphics read the shared die, the CPU's heat: with a card beside them, the card's temperature is the GPU's, or a hot CPU raises a GPU alert.
        var all = nodes.ToList();
        var igpu = all.Where(n => n.Kind == HardwareKind.Gpu && GpuKind.Integrated(n.Name, false)).Select(n => n.Id).ToHashSet();
        bool card = all.Any(n => n.Kind == HardwareKind.Gpu && n.ParentId is null && !igpu.Contains(n.Id));
        var sensors = all.Where(n => !card || !(igpu.Contains(n.Id) || n.ParentId is { } p && igpu.Contains(p))).SelectMany(n => n.Sensors).ToList();
        IEnumerable<double> Values(params SensorRole[] roles) => sensors.Where(s => roles.Contains(s.Role) && byId.ContainsKey(s.Id)).Select(s => byId[s.Id]);
        double? Max(params SensorRole[] roles) => Values(roles) is var v && v.Any() ? v.Max() : null;
        double? First(params SensorRole[] roles) { foreach (var role in roles) if (Values(role) is var v && v.Any()) return v.First(); return null; }

        return new(Max(SensorRole.CpuPackageTemp, SensorRole.CpuTctlTdie, SensorRole.CpuCoreTemp), Max(SensorRole.GpuCoreTemp),
                   First(SensorRole.CpuTotalLoad), First(SensorRole.CpuEffectiveClockAverage, SensorRole.CpuCoreClockAverage), Max(SensorRole.GpuHotSpotTemp),
                   Max(SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D, SensorRole.GpuLoadCompute));
    }
}
