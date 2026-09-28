using System.Collections.ObjectModel; using System.Globalization; using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization; using static Mazesta.Desktop.ViewModels.DashboardViewModel;
namespace Mazesta.Desktop.ViewModels;

public sealed record InfoRow(string Label, string Value);
public sealed record InfoSection(string Title, IReadOnlyList<InfoRow> Rows);

/// <summary>Read-only machine inventory: the same InventoryCache the Dashboard reads, laid out in
/// full (every field, one row each) instead of the Dashboard's condensed summary line.</summary>
public sealed partial class SystemInfoViewModel : ObservableObject
{
    public ObservableCollection<InfoSection> Sections { get; } = [];
    [ObservableProperty] private string _status = "";
    public Task Loaded { get; }

    public SystemInfoViewModel(InventoryCache cache, Func<Action, object> dispatch) => Loaded = LoadAsync(cache, dispatch);

    private async Task LoadAsync(InventoryCache cache, Func<Action, object> dispatch)
    {
        var inv = await cache.GetAsync().ConfigureAwait(false);
        dispatch(() =>
        {
            foreach (var s in Describe(inv)) Sections.Add(s);
            Status = inv.Errors.Count == 0 ? "" : string.Join("; ", inv.Errors);
        });
    }

    private static string ShowSuffixed(object? value, string suffix) => value is null ? Loc.Get("Value_NotAvailable") : $"{value} {suffix}";

    internal static IEnumerable<InfoSection> Describe(HardwareInventory inv)
    {
        foreach (var section in Component(inv, HardwareKind.Cpu)) yield return section;
        yield return new(Loc.Get("SystemInfo_Motherboard"), BoardRows(inv.Motherboard));
        yield return new(Loc.Get("SystemInfo_Bios"), BiosRows(inv.Bios));
        yield return new(Loc.Get("Dashboard_Ram"), MemoryRows(inv));
        yield return new(Loc.Get("SystemInfo_Os"), OsRows(inv.Os));
        foreach (var kind in new[] { HardwareKind.Gpu, HardwareKind.Storage, HardwareKind.Network }) foreach (var section in Component(inv, kind)) yield return section;
    }

    /// <summary>The inventory of one component, as the CPU, GPU, Storage and Network pages show it.</summary>
    internal static IEnumerable<InfoSection> Component(HardwareInventory inv, HardwareKind kind) => kind switch
    {
        HardwareKind.Cpu => [new(Loc.Get("Dashboard_Cpu"), CpuRows(inv.Cpu))],
        HardwareKind.Gpu => Numbered(inv.Gpus, "Dashboard_Gpu", "SystemInfo_GpuN", GpuRows),
        HardwareKind.Storage => Numbered(inv.Storage, "SystemInfo_Storage", "SystemInfo_StorageN", StorageRows),
        HardwareKind.Network => Numbered(inv.NetworkAdapters, "SystemInfo_Network", "SystemInfo_NetworkN", NetworkRows),
        _ => []
    };

    /// <summary>One section per device, titled with just the category when there is one and numbered when there are several; a category with none is a single not-available row, never silently absent.</summary>
    private static IEnumerable<InfoSection> Numbered<T>(IReadOnlyList<T> items, string titleKey, string numberedKey, Func<T, IReadOnlyList<InfoRow>> rows)
    {
        if (items.Count == 0) yield return new(Loc.Get(titleKey), NotAvailableRows());
        for (int i = 0; i < items.Count; i++) yield return new(items.Count == 1 ? Loc.Get(titleKey) : Loc.Format(numberedKey, i + 1), rows(items[i]));
    }

    private static IReadOnlyList<InfoRow> NotAvailableRows(string labelKey = "SystemInfo_Name") => [new(Loc.Get(labelKey), Loc.Get("Value_NotAvailable"))];

    private static IReadOnlyList<InfoRow> CpuRows(CpuInfo? c) => c is null ? NotAvailableRows() :
    [
        new(Loc.Get("SystemInfo_Name"), Show(c.Name)),
        new(Loc.Get("SystemInfo_Vendor"), c.Vendor == HardwareVendor.Unknown ? Loc.Get("Value_NotAvailable") : c.Vendor.ToString()),
        new(Loc.Get("SystemInfo_CoresPhysical"), Show(c.PhysicalCores)),
        new(Loc.Get("SystemInfo_CoresLogical"), Show(c.LogicalProcessors)),
        new(Loc.Get("SystemInfo_MaxClock"), ShowSuffixed(c.MaxClockMhz, "MHz")),
        new(Loc.Get("SystemInfo_Socket"), Show(c.Socket)),
    ];

    private static IReadOnlyList<InfoRow> GpuRows(GpuInfo g) =>
    [
        new(Loc.Get("SystemInfo_Name"), Show(g.Name)),
        new(Loc.Get("SystemInfo_Driver"), Show(g.DriverVersion)),
        new(Loc.Get("SystemInfo_Vram"), ShowBytes(g.AdapterRamBytes, 1024.0 * 1024 * 1024)),
        new(Loc.Get("SystemInfo_DeviceId"), Show(g.PnpDeviceId)),
    ];

    private static IReadOnlyList<InfoRow> BoardRows(MotherboardInfo? m) => m is null ? NotAvailableRows("SystemInfo_Manufacturer") :
    [
        new(Loc.Get("SystemInfo_Manufacturer"), Show(m.Manufacturer)),
        new(Loc.Get("SystemInfo_Product"), Show(m.Product)),
        new(Loc.Get("SystemInfo_Version"), Show(m.Version)),
        new(Loc.Get("SystemInfo_Serial"), Show(m.SerialNumber)),
    ];

    private static IReadOnlyList<InfoRow> BiosRows(BiosInfo? b) => b is null ? NotAvailableRows("SystemInfo_Vendor") :
    [
        new(Loc.Get("SystemInfo_Vendor"), Show(b.Vendor)),
        new(Loc.Get("SystemInfo_Version"), Show(b.Version)),
        new(Loc.Get("SystemInfo_ReleaseDate"), Show(b.ReleaseDate, "yyyy-MM-dd")),
        new(Loc.Get("SystemInfo_Smbios"), Show(b.SmbiosVersion)),
    ];

    private static IReadOnlyList<InfoRow> MemoryRows(HardwareInventory inv)
    {
        List<InfoRow> rows = [new(Loc.Get("Dashboard_Ram_Total"), ShowBytes(inv.TotalPhysicalMemoryBytes, 1024.0 * 1024 * 1024))];
        if (inv.MemoryModules.Count == 0) { rows.Add(new(Loc.Get("Dashboard_Ram_Modules"), Loc.Get("Value_NotAvailable"))); return rows; }
        foreach (var m in inv.MemoryModules)
        {
            var speed = m.ConfiguredSpeedMts ?? m.SpeedMts;
            rows.Add(new(Show(m.Slot), $"{ShowBytes(m.CapacityBytes, 1024.0 * 1024 * 1024)} · {Show(m.Manufacturer)} · {Show(m.PartNumber)} · {ShowSuffixed(speed, "MT/s")}"));
        }
        return rows;
    }

    private static IReadOnlyList<InfoRow> OsRows(OsInfo? o) => o is null ? NotAvailableRows() :
    [
        new(Loc.Get("SystemInfo_Name"), Show(o.Caption)),
        new(Loc.Get("SystemInfo_Version"), Show(o.Version)),
        new(Loc.Get("SystemInfo_Build"), Show(o.BuildNumber)),
        new(Loc.Get("SystemInfo_Architecture"), Show(o.Architecture)),
    ];

    private static IReadOnlyList<InfoRow> StorageRows(StorageDeviceInfo d) =>
    [
        new(Loc.Get("SystemInfo_Name"), Show(d.FriendlyName)),
        new(Loc.Get("SystemInfo_MediaType"), Show(d.MediaType)),
        new(Loc.Get("SystemInfo_BusType"), Show(d.BusType)),
        new(Loc.Get("SystemInfo_Size"), ShowBytes(d.SizeBytes, 1e9)),
        new(Loc.Get("SystemInfo_Firmware"), Show(d.FirmwareVersion)),
        new(Loc.Get("SystemInfo_Serial"), Show(d.SerialNumber)),
        new(Loc.Get("SystemInfo_Health"), DriveHealth(d.HealthStatus, d.WearPercent)),
    ];

    /// <summary>Windows' verdict in words, with the life left in brackets when the drive has a wear counter: «سالم (98%)».</summary>
    public static string DriveHealth(string? status, int? wearPercent)
    {
        if (status is null) return Loc.Get("Value_NotAvailable");
        string name = Loc.Get(status is "Healthy" or "Warning" or "Unhealthy" ? "Drive_Health_" + status : "Drive_Health_Unknown");
        return Core.Health.DriveAttention.HealthPercent(wearPercent) is { } p ? Loc.Format("Drive_Health_Percent", name, p) : name;
    }

    private static IReadOnlyList<InfoRow> NetworkRows(NetworkAdapterInfo n) =>
    [
        new(Loc.Get("SystemInfo_Name"), Show(n.Name)),
        new(Loc.Get("SystemInfo_Status"), Loc.Get(n.IsUp ? "Value_Connected" : "Value_Disconnected")),
        new(Loc.Get("SystemInfo_Mac"), Show(n.MacAddress)),
        new(Loc.Get("SystemInfo_IpAddresses"), n.IpAddresses.Count == 0 ? Loc.Get("Value_NotAvailable") : string.Join(", ", n.IpAddresses)),
        new(Loc.Get("SystemInfo_LinkSpeed"), n.LinkSpeedBps is { } bps ? (bps / 1_000_000.0).ToString("F0", CultureInfo.InvariantCulture) + " Mbps" : Loc.Get("Value_NotAvailable")),
    ];
}
