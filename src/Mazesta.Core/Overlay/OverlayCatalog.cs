using Mazesta.Core.Hardware;
namespace Mazesta.Core.Overlay;

/// <summary>The overlay's blocks. Each is a part of the machine and wears that part's colour; Gaming is the frame rate. Blocks are drawn in the
/// order the technician put their items in.</summary>
public enum OverlayPart { Gaming, Gpu, Cpu, Memory, Storage, Network }

/// <summary>How an item made of several sensors becomes one number: the first sensor found, the highest (hottest core, hottest drive), or the
/// sum (traffic over every adapter or drive).</summary>
public enum OverlayAggregate { First, Max, Sum }

/// <summary>One thing the overlay can show. <paramref name="Roles"/> are alternatives, tried in order for <see cref="OverlayAggregate.First"/>;
/// an item with no roles is measured by the frame-rate monitor, not by a sensor. <paramref name="FixedMax"/> is the chart's fixed top (100 for a
/// percentage or a temperature); null scales the chart to what it shows.</summary>
public sealed record OverlayItem(string Id, OverlayPart Part, string LabelKey, SensorRole[] Roles, OverlayAggregate Aggregate = OverlayAggregate.First, double? FixedMax = null)
{
    public bool IsFrameItem => Roles.Length == 0;
    /// <summary>The one device an item is pinned to (a drive's own read rate), by hardware id; null reads the whole part.</summary>
    public string? Device { get; init; }
    /// <summary>The catalog item a pinned item was made from ("storage.read" for "storage.read@storage/…").</summary>
    public string BaseId => Device is null ? Id : Id[..Id.IndexOf('@', StringComparison.Ordinal)];
}

/// <summary>The item the overlay settings refer to by id, with its chart on or off.</summary>
public sealed record OverlayChoice(string Id, bool Chart);

/// <summary>
/// Every item the overlay can show, and the three ready-made sets (game, render, troubleshooting). The catalog names roles, never sensors: which
/// sensors an item reads on a machine is decided by <see cref="Resolve"/>, and an item the machine has no sensor for is simply not available
/// there (it is never shown as zero).
/// </summary>
public static class OverlayCatalog
{
    private const double Percent = 100;
    public static readonly IReadOnlyList<OverlayItem> All =
    [
        new("fps", OverlayPart.Gaming, "Overlay_Fps", []), new("low1", OverlayPart.Gaming, "Overlay_Low1", []), new("frametime", OverlayPart.Gaming, "Overlay_FrameTime", []),

        new("gpu.temp", OverlayPart.Gpu, "Overlay_Temp", [SensorRole.GpuCoreTemp], FixedMax: Percent),
        new("gpu.hotspot", OverlayPart.Gpu, "Overlay_HotSpot", [SensorRole.GpuHotSpotTemp], FixedMax: Percent),
        new("gpu.vramtemp", OverlayPart.Gpu, "Overlay_VramTemp", [SensorRole.GpuVramTemp], FixedMax: Percent),
        new("gpu.load", OverlayPart.Gpu, "Overlay_Load", [SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D], FixedMax: Percent),
        new("gpu.clock", OverlayPart.Gpu, "Overlay_Clock", [SensorRole.GpuCoreClock]),
        new("gpu.memclock", OverlayPart.Gpu, "Overlay_VramClock", [SensorRole.GpuMemoryClock]),
        new("gpu.power", OverlayPart.Gpu, "Overlay_Power", [SensorRole.GpuPower]),
        new("gpu.voltage", OverlayPart.Gpu, "Overlay_Voltage", [SensorRole.GpuVoltage]),
        new("gpu.fan", OverlayPart.Gpu, "Overlay_Fan", [SensorRole.GpuFanPercent], FixedMax: Percent),
        new("gpu.fanrpm", OverlayPart.Gpu, "Overlay_FanRpm", [SensorRole.GpuFanRpm]),
        new("gpu.vram", OverlayPart.Gpu, "Overlay_Vram", [SensorRole.GpuVramUsed]),

        new("cpu.temp", OverlayPart.Cpu, "Overlay_Temp", [SensorRole.CpuPackageTemp, SensorRole.CpuTctlTdie], FixedMax: Percent),
        new("cpu.hotcore", OverlayPart.Cpu, "Overlay_HotCore", [SensorRole.CpuCoreTemp, SensorRole.CpuCcdTemp], OverlayAggregate.Max, Percent),
        new("cpu.load", OverlayPart.Cpu, "Overlay_Load", [SensorRole.CpuTotalLoad], FixedMax: Percent),
        new("cpu.maxthread", OverlayPart.Cpu, "Overlay_MaxThread", [SensorRole.CpuThreadLoad], OverlayAggregate.Max, Percent),
        new("cpu.clock", OverlayPart.Cpu, "Overlay_Clock", [SensorRole.CpuEffectiveClockAverage, SensorRole.CpuCoreClockAverage, SensorRole.CpuCoreClock]),
        new("cpu.maxclock", OverlayPart.Cpu, "Overlay_MaxClock", [SensorRole.CpuEffectiveClock, SensorRole.CpuCoreClock], OverlayAggregate.Max),
        new("cpu.power", OverlayPart.Cpu, "Overlay_Power", [SensorRole.CpuPackagePower]),
        new("cpu.voltage", OverlayPart.Cpu, "Overlay_Voltage", [SensorRole.CpuVcore]),
        new("cpu.fan", OverlayPart.Cpu, "Overlay_FanRpm", [SensorRole.CpuFan]),

        new("ram.used", OverlayPart.Memory, "Overlay_Used", [SensorRole.RamUsed]),
        new("ram.load", OverlayPart.Memory, "Overlay_Load", [SensorRole.RamLoad], FixedMax: Percent),
        new("ram.temp", OverlayPart.Memory, "Overlay_Temp", [SensorRole.DimmTemp], OverlayAggregate.Max, Percent),

        new("storage.temp", OverlayPart.Storage, "Overlay_HotDrive", [SensorRole.StorageTemp], OverlayAggregate.Max, Percent),
        new("storage.read", OverlayPart.Storage, "Overlay_Read", [SensorRole.StorageReadRate], OverlayAggregate.Sum),
        new("storage.write", OverlayPart.Storage, "Overlay_Write", [SensorRole.StorageWriteRate], OverlayAggregate.Sum),
        new("storage.activity", OverlayPart.Storage, "Overlay_Activity", [SensorRole.StorageTotalActivity], OverlayAggregate.Max, Percent),

        new("net.down", OverlayPart.Network, "Overlay_Down", [SensorRole.NetDownload], OverlayAggregate.Sum),
        new("net.up", OverlayPart.Network, "Overlay_Up", [SensorRole.NetUpload], OverlayAggregate.Sum),
    ];

    /// <summary>The items that can also be pinned to one drive, so each drive can show its own traffic, temperature and activity.</summary>
    public static readonly IReadOnlyList<string> PerDrive = ["storage.read", "storage.write", "storage.temp", "storage.activity"];

    public static string Pinned(string baseId, string device) => $"{baseId}@{device}";

    /// <summary>A catalog item by id; "base@device" is that item pinned to one device (read on that device only).</summary>
    public static OverlayItem? Find(string id)
    {
        int at = id.IndexOf('@', StringComparison.Ordinal);
        if (at < 0) return All.FirstOrDefault(i => i.Id == id);
        var item = All.FirstOrDefault(i => i.Id == id[..at]);
        return item is null || !PerDrive.Contains(item.Id) || at == id.Length - 1 ? null : item with { Id = id, Aggregate = OverlayAggregate.First, Device = id[(at + 1)..] };
    }

    /// <summary>Every per-drive item this machine has: for each drive, <see cref="PerDrive"/> pinned to it.</summary>
    public static IReadOnlyList<OverlayItem> ForDrives(IReadOnlyList<HardwareNode> hardware)
        => [.. hardware.Where(n => n.ParentId is null && n.Kind == HardwareKind.Storage).SelectMany(n => PerDrive.Select(b => Find(Pinned(b, n.Id.Value))!))];

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<OverlayChoice>> Presets = new Dictionary<string, IReadOnlyList<OverlayChoice>>
    {
        // Playing: the frame rate first and charted, then what limits it.
        ["game"] = Choices("fps:c low1 frametime:c gpu.temp gpu.load gpu.clock gpu.vram gpu.power cpu.temp cpu.load cpu.maxthread ram.used"),
        // Rendering: how busy and how hot the processors stay over a long job, memory, and the drive being written.
        ["render"] = Choices("cpu.load:c cpu.temp:c cpu.clock cpu.power gpu.load:c gpu.temp gpu.power gpu.vram ram.used:c ram.load storage.write"),
        // Troubleshooting: every temperature, clock, voltage and fan that tells a throttling or failing part.
        ["troubleshoot"] = Choices("cpu.temp:c cpu.hotcore cpu.clock cpu.maxclock cpu.power cpu.voltage cpu.fan gpu.temp:c gpu.hotspot gpu.vramtemp gpu.clock gpu.power gpu.voltage gpu.fanrpm ram.load storage.temp"),
    };
    public const string DefaultPreset = "game";

    private static OverlayChoice[] Choices(string spec) => [.. spec.Split(' ').Select(s => s.EndsWith(":c", StringComparison.Ordinal) ? new OverlayChoice(s[..^2], true) : new OverlayChoice(s, false))];

    /// <summary>The sensors an item reads on this machine; empty when it has none (or it is a frame item). For First, the first role found on
    /// the first device of the part that has any of them (for the GPU, the one with a core temperature first: the discrete card); for Max and
    /// Sum, that role on every device of the part. <paramref name="include"/> leaves devices out (virtual network switches count traffic twice).</summary>
    public static IReadOnlyList<SensorDefinition> Resolve(OverlayItem item, IReadOnlyList<HardwareNode> hardware, Func<HardwareNode, bool>? include = null)
    {
        if (item.IsFrameItem) return [];
        var kind = item.Part switch { OverlayPart.Gpu => HardwareKind.Gpu, OverlayPart.Cpu => HardwareKind.Cpu, OverlayPart.Memory => HardwareKind.Memory, OverlayPart.Storage => HardwareKind.Storage, _ => HardwareKind.Network };
        var nodes = hardware.Where(n => n.ParentId is null && n.Kind == kind && (item.Device is null ? include?.Invoke(n) ?? true : n.Id.Value == item.Device))
            .OrderByDescending(n => kind == HardwareKind.Gpu && n.Sensors.Any(s => s.Role == SensorRole.GpuCoreTemp)).ToList();
        if (item.Aggregate == OverlayAggregate.First)
        {
            foreach (var n in nodes)
                foreach (var role in item.Roles)
                    if (n.Sensors.FirstOrDefault(s => s.Role == role) is { } s) return [s];
            return [];
        }
        // Max and Sum: the first role any device has, on every device (a mix of roles would add unlike numbers).
        foreach (var role in item.Roles)
        {
            var all = nodes.SelectMany(n => n.Sensors.Where(s => s.Role == role)).ToList();
            if (all.Count > 0) return all;
        }
        return [];
    }

    /// <summary>One number from the item's readings; null when none of its sensors has a reading now. A sum adds only what did report.</summary>
    public static double? Combine(OverlayAggregate aggregate, IEnumerable<double?> readings)
    {
        var values = readings.OfType<double>().ToList();
        if (values.Count == 0) return null;
        return aggregate switch { OverlayAggregate.Max => values.Max(), OverlayAggregate.Sum => values.Sum(), _ => values[0] };
    }
}
