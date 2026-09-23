using System.Collections.ObjectModel; using System.Globalization; using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization;
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

    private static string Show(object? value, string? format = null) => value switch
    {
        null => Loc.Get("Value_NotAvailable"),
        string s when s.Trim().Length == 0 => Loc.Get("Value_NotAvailable"),
        IFormattable f when format is not null => f.ToString(format, CultureInfo.CurrentCulture),
        _ => value.ToString() is { Length: > 0 } t ? t : Loc.Get("Value_NotAvailable")
    };
    private static string ShowBytes(long? bytes, double divisor, string unit) => bytes is { } b ? (b / divisor).ToString("F0", CultureInfo.InvariantCulture) + " " + unit : Loc.Get("Value_NotAvailable");
    private static string ShowSuffixed(object? value, string suffix) => value is null ? Loc.Get("Value_NotAvailable") : $"{value} {suffix}";

    internal static IEnumerable<InfoSection> Describe(HardwareInventory inv)
    {
        yield return new(Loc.Get("Dashboard_Cpu"), CpuRows(inv.Cpu));
        yield return new(Loc.Get("SystemInfo_Motherboard"), BoardRows(inv.Motherboard));
        yield return new(Loc.Get("SystemInfo_Bios"), BiosRows(inv.Bios));
        yield return new(Loc.Get("Dashboard_Ram"), MemoryRows(inv));
        yield return new(Loc.Get("SystemInfo_Os"), OsRows(inv.Os));

        if (inv.Gpus.Count == 0) yield return new(Loc.Get("Dashboard_Gpu"), [new(Loc.Get("SystemInfo_Name"), Loc.Get("Value_NotAvailable"))]);
        for (int i = 0; i < inv.Gpus.Count; i++)
            yield return new(inv.Gpus.Count == 1 ? Loc.Get("Dashboard_Gpu") : Loc.Format("SystemInfo_GpuN", i + 1), GpuRows(inv.Gpus[i]));

        if (inv.Storage.Count == 0) yield return new(Loc.Get("SystemInfo_Storage"), [new(Loc.Get("SystemInfo_Name"), Loc.Get("Value_NotAvailable"))]);
        for (int i = 0; i < inv.Storage.Count; i++)
            yield return new(inv.Storage.Count == 1 ? Loc.Get("SystemInfo_Storage") : Loc.Format("SystemInfo_StorageN", i + 1), StorageRows(inv.Storage[i]));

        if (inv.NetworkAdapters.Count == 0) yield return new(Loc.Get("SystemInfo_Network"), [new(Loc.Get("SystemInfo_Name"), Loc.Get("Value_NotAvailable"))]);
        for (int i = 0; i < inv.NetworkAdapters.Count; i++)
            yield return new(inv.NetworkAdapters.Count == 1 ? Loc.Get("SystemInfo_Network") : Loc.Format("SystemInfo_NetworkN", i + 1), NetworkRows(inv.NetworkAdapters[i]));
    }

    private static IReadOnlyList<InfoRow> CpuRows(CpuInfo? c) => c is null ? [new(Loc.Get("SystemInfo_Name"), Loc.Get("Value_NotAvailable"))] :
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
        new(Loc.Get("SystemInfo_Vram"), ShowBytes(g.AdapterRamBytes, 1024.0 * 1024 * 1024, "GB")),
        new(Loc.Get("SystemInfo_DeviceId"), Show(g.PnpDeviceId)),
    ];

    private static IReadOnlyList<InfoRow> BoardRows(MotherboardInfo? m) => m is null ? [new(Loc.Get("SystemInfo_Manufacturer"), Loc.Get("Value_NotAvailable"))] :
    [
        new(Loc.Get("SystemInfo_Manufacturer"), Show(m.Manufacturer)),
        new(Loc.Get("SystemInfo_Product"), Show(m.Product)),
        new(Loc.Get("SystemInfo_Version"), Show(m.Version)),
        new(Loc.Get("SystemInfo_Serial"), Show(m.SerialNumber)),
    ];

    private static IReadOnlyList<InfoRow> BiosRows(BiosInfo? b) => b is null ? [new(Loc.Get("SystemInfo_Vendor"), Loc.Get("Value_NotAvailable"))] :
    [
        new(Loc.Get("SystemInfo_Vendor"), Show(b.Vendor)),
        new(Loc.Get("SystemInfo_Version"), Show(b.Version)),
        new(Loc.Get("SystemInfo_ReleaseDate"), Show(b.ReleaseDate, "yyyy-MM-dd")),
        new(Loc.Get("SystemInfo_Smbios"), Show(b.SmbiosVersion)),
    ];

    private static IReadOnlyList<InfoRow> MemoryRows(HardwareInventory inv)
    {
        List<InfoRow> rows = [new(Loc.Get("Dashboard_Ram_Total"), ShowBytes(inv.TotalPhysicalMemoryBytes, 1024.0 * 1024 * 1024, "GB"))];
        if (inv.MemoryModules.Count == 0) { rows.Add(new(Loc.Get("Dashboard_Ram_Modules"), Loc.Get("Value_NotAvailable"))); return rows; }
        foreach (var m in inv.MemoryModules)
        {
            var speed = m.ConfiguredSpeedMts ?? m.SpeedMts;
            rows.Add(new(Show(m.Slot), $"{ShowBytes(m.CapacityBytes, 1024.0 * 1024 * 1024, "GB")} · {Show(m.Manufacturer)} · {Show(m.PartNumber)} · {ShowSuffixed(speed, "MT/s")}"));
        }
        return rows;
    }

    private static IReadOnlyList<InfoRow> OsRows(OsInfo? o) => o is null ? [new(Loc.Get("SystemInfo_Name"), Loc.Get("Value_NotAvailable"))] :
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
        new(Loc.Get("SystemInfo_Size"), ShowBytes(d.SizeBytes, 1e9, "GB")),
        new(Loc.Get("SystemInfo_Firmware"), Show(d.FirmwareVersion)),
        new(Loc.Get("SystemInfo_Serial"), Show(d.SerialNumber)),
        new(Loc.Get("SystemInfo_Health"), Show(d.HealthStatus)),
    ];

    private static IReadOnlyList<InfoRow> NetworkRows(NetworkAdapterInfo n) =>
    [
        new(Loc.Get("SystemInfo_Name"), Show(n.Name)),
        new(Loc.Get("SystemInfo_Status"), Loc.Get(n.IsUp ? "Value_Connected" : "Value_Disconnected")),
        new(Loc.Get("SystemInfo_Mac"), Show(n.MacAddress)),
        new(Loc.Get("SystemInfo_IpAddresses"), n.IpAddresses.Count == 0 ? Loc.Get("Value_NotAvailable") : string.Join(", ", n.IpAddresses)),
        new(Loc.Get("SystemInfo_LinkSpeed"), n.LinkSpeedBps is { } bps ? (bps / 1_000_000.0).ToString("F0", CultureInfo.InvariantCulture) + " Mbps" : Loc.Get("Value_NotAvailable")),
    ];
}
