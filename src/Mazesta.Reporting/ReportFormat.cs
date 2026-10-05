using System.Globalization; using Mazesta.Core.Hardware; using Mazesta.Core.Inventory;
namespace Mazesta.Reporting;

/// <summary>What the HTML and the plain-text report both print: times, durations, measured values and the machine specification rows.</summary>
internal static class ReportFormat
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static string Stamp(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", Inv);
    public static string Duration(double seconds) => seconds >= 60 ? $"{(int)(seconds / 60)}m {seconds % 60:F0}s" : $"{seconds:F0}s";
    public static string Value(double v, SensorSummary s) => Units.FormatMeasured(v, s.Unit, 1);

    /// <summary>The maker and model the machine is sold under ("LENOVO 82JU"); null when Windows gave neither or only a placeholder a board maker leaves in.</summary>
    public static string? DeviceName(HardwareInventory m)
    {
        if (m.Computer is not { } c) return null;
        static bool Filler(string? s) => string.IsNullOrWhiteSpace(s) || s.Contains("O.E.M.", StringComparison.OrdinalIgnoreCase) || s.Contains("To be filled", StringComparison.OrdinalIgnoreCase) || s.Contains("System Product Name", StringComparison.OrdinalIgnoreCase) || s.Contains("Default string", StringComparison.OrdinalIgnoreCase);
        string text = $"{(Filler(c.Manufacturer) ? "" : c.Manufacturer)} {(Filler(c.Model) ? "" : c.Model)}".Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>One row per part that was detected; a part that was not is left out, not shown as empty.</summary>
    public static IEnumerable<(string Label, string Value)> MachineRows(HardwareInventory m, ReportText w)
    {
        IEnumerable<(string, string?)> Rows()
        {
            if (DeviceName(m) is { } device) yield return (w.Device, device + (m.Computer?.IsPortable == true ? $" ({w.Laptop})" : ""));
            if (m.Cpu is { } c) yield return (w.Cpu, $"{c.Name} · {c.PhysicalCores}C/{c.LogicalProcessors}T" + (c.MaxClockMhz is { } mhz ? $" · {mhz / 1000.0:F2} GHz" : ""));
            foreach (var g in m.Gpus) yield return (w.Gpu, $"{g.Name}" + (g.DriverVersion is { } d ? $" · driver {d}" : ""));
            if (m.TotalPhysicalMemoryBytes is { } ram) yield return (w.Ram, $"{ram / 1073741824.0:F0} GB · {m.MemoryModules.Count} module(s)");
            foreach (var d in m.MemoryModules) yield return ("", $"{d.Slot}: {(d.CapacityBytes ?? 0) / 1073741824} GB {d.Manufacturer} {d.PartNumber} {(d.ConfiguredSpeedMts ?? d.SpeedMts)} MT/s".Trim());
            if (m.Motherboard is { } mb) yield return (w.Board, $"{mb.Manufacturer} {mb.Product}");
            if (m.Bios is { } bios) yield return (w.Bios, $"{bios.Vendor} {bios.Version}" + (bios.ReleaseDate is { } rd ? $" ({rd:yyyy-MM-dd})" : ""));
            foreach (var s in m.Storage) yield return (w.Storage, $"{s.FriendlyName} · {s.MediaType} {s.BusType} · {(s.SizeBytes ?? 0) / 1_000_000_000} GB · {s.HealthStatus}");
            foreach (var n in m.NetworkAdapters.Where(n => n.IsUp)) yield return (w.Network, $"{n.Name}" + (n.LinkSpeedBps is { } bps ? $" · {bps / 1_000_000} Mbps" : ""));
            if (m.Os is { } os) yield return (w.Os, $"{os.Caption} {os.Version} ({os.Architecture})");
        }
        return Rows().Where(r => !string.IsNullOrWhiteSpace(r.Item2)).Select(r => (r.Item1, r.Item2!));
    }
}
