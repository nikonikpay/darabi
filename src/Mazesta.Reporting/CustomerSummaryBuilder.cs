using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Core.Providers; using Mazesta.Monitoring;
namespace Mazesta.Reporting;

/// <summary>Collects the customer summary's measured parts from the monitor and Windows' drive health, kept out of the UI so it is testable.</summary>
public static class CustomerSummaryBuilder
{
    /// <summary>A reading older than this is not "now": the monitor paused, or the sensor stopped reporting.</summary>
    public static readonly TimeSpan CurrentWindow = TimeSpan.FromSeconds(15);

    /// <summary>CPU package, each GPU's core and hot spot: the newest recorded reading (when recent) and the highest since the monitor started. A part
    /// whose sensor the machine does not have is left out.</summary>
    public static IReadOnlyList<TemperatureSummary> Temperatures(PollingEngine engine, DateTimeOffset now)
    {
        var list = new List<TemperatureSummary>(); int now0 = engine.History.SecondsSinceEpoch(now);
        var gpus = engine.Hardware.Where(n => n.ParentId is null && n.Kind == HardwareKind.Gpu).ToList();
        foreach (var node in engine.Hardware.Where(n => n.ParentId is null))
        {
            string gpu = gpus.Count > 1 ? $"GPU {gpus.IndexOf(node) + 1}" : "GPU";
            if (node.Kind == HardwareKind.Cpu) Add("CPU", node, SensorRole.CpuPackageTemp, SensorRole.CpuTctlTdie);
            else if (node.Kind == HardwareKind.Gpu) { Add(gpu, node, SensorRole.GpuCoreTemp); Add(gpu + " Hot Spot", node, SensorRole.GpuHotSpotTemp); }
        }
        return list;

        void Add(string part, HardwareNode node, params SensorRole[] roles)
        {
            if (roles.Select(r => node.Sensors.FirstOrDefault(s => s.Role == r)).FirstOrDefault(s => s is not null) is not { } sensor) return;
            var raw = engine.History.GetRaw(sensor.Id);
            double? current = raw.Seconds.Length > 0 && now0 - raw.Seconds[^1] <= CurrentWindow.TotalSeconds ? raw.Values[^1] : null;
            var stats = engine.Statistics.Get(sensor.Id);
            list.Add(new(part, current, stats.Max, stats.Since));
        }
    }

    /// <summary>Each drive Windows reports health for, with its type and size from the inventory (matched by serial number). Drives without a
    /// health record still appear, with the inventory's own verdict.</summary>
    public static IReadOnlyList<DriveSummary> Drives(IReadOnlyList<DriveHealth> health, IReadOnlyList<StorageDeviceInfo> storage)
    {
        static string Key(string? serial) => (serial ?? "").Trim().Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        var bySerial = storage.Where(s => Key(s.SerialNumber).Length > 0).GroupBy(s => Key(s.SerialNumber)).ToDictionary(g => g.Key, g => g.First());
        var list = new List<DriveSummary>(); var matched = new HashSet<StorageDeviceInfo>();
        foreach (var h in health)
        {
            var info = bySerial.GetValueOrDefault(Key(h.Serial)) ?? storage.FirstOrDefault(s => string.Equals(s.FriendlyName?.Trim(), h.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (info is not null) matched.Add(info);
            list.Add(new(h.Name, info?.MediaType, info?.BusType, info?.SizeBytes, h.Status, h.WearPercent, h.TemperatureC, h.PowerOnHours));
        }
        foreach (var s in storage.Where(s => !matched.Contains(s)))
            list.Add(new(s.FriendlyName ?? "?", s.MediaType, s.BusType, s.SizeBytes, s.HealthStatus, null, null, null));
        return list;
    }
}
