using System.Globalization; using Mazesta.Core.Hardware; using Mazesta.Core.Inventory;
namespace Mazesta.Hardware.Wmi;
internal static class WmiInventoryParser
{
    private static string? S(IReadOnlyDictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v is not null ? Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } s ? s : null : null;
    private static long? L(IReadOnlyDictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v is not null ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : null;
    private static int? I(IReadOnlyDictionary<string, object?> r, string k) => L(r, k) is { } l ? checked((int)l) : null;
    private static bool? B(IReadOnlyDictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v is bool b ? b : null;

    public static CpuInfo? Cpu(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        if (rows.Count == 0) return null; var r = rows[0];
        var vendor = S(r, "Manufacturer") switch { "GenuineIntel" => HardwareVendor.Intel, "AuthenticAMD" => HardwareVendor.Amd, _ => HardwareVendor.Unknown };
        return new CpuInfo(S(r, "Name"), vendor, I(r, "NumberOfCores"), I(r, "NumberOfLogicalProcessors"), I(r, "MaxClockSpeed"), S(r, "SocketDesignation"));
    }
    public static IReadOnlyList<GpuInfo> Gpus(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
        => rows.Select(r => new GpuInfo(S(r, "Name"), S(r, "DriverVersion"), L(r, "AdapterRAM"), S(r, "PNPDeviceID"))).ToList();
    public static IReadOnlyList<MemoryModuleInfo> Memory(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
        => rows.Select(r => new MemoryModuleInfo(S(r, "DeviceLocator"), L(r, "Capacity"), S(r, "Manufacturer"), S(r, "PartNumber"), I(r, "ConfiguredClockSpeed"), I(r, "Speed"))).ToList();
    public static long? TotalMemory(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) => rows.Count == 0 ? null : L(rows[0], "TotalPhysicalMemory");
    public static MotherboardInfo? Board(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
        => rows.Count == 0 ? null : new MotherboardInfo(S(rows[0], "Manufacturer"), S(rows[0], "Product"), S(rows[0], "Version"), S(rows[0], "SerialNumber"));
    public static BiosInfo? Bios(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        if (rows.Count == 0) return null; var r = rows[0];
        DateTime? date = S(r, "ReleaseDate") is { Length: >= 8 } d && DateTime.TryParseExact(d[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null;
        string? smbios = S(r, "SMBIOSMajorVersion") is { } maj && S(r, "SMBIOSMinorVersion") is { } min ? $"{maj}.{min}" : null;
        return new BiosInfo(S(r, "Manufacturer"), S(r, "SMBIOSBIOSVersion"), date, smbios);
    }
    public static IReadOnlyList<StorageDeviceInfo> Disks(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) => rows.Select(r => new StorageDeviceInfo(
        S(r, "FriendlyName"), S(r, "SerialNumber"),
        I(r, "MediaType") switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", null => null, _ => "Unspecified" },
        I(r, "BusType") switch { 1 => "SCSI", 3 => "ATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 11 => "SATA", 17 => "NVMe", null => null, var b => $"Bus {b}" },
        L(r, "Size"), S(r, "FirmwareVersion"),
        I(r, "HealthStatus") switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", null => null, _ => "Unknown" })).ToList();
    public static IReadOnlyList<NetworkAdapterInfo> Adapters(IReadOnlyList<IReadOnlyDictionary<string, object?>> adapters, IReadOnlyList<IReadOnlyDictionary<string, object?>> configs)
    {
        var ips = configs.Where(c => L(c, "InterfaceIndex") is not null).ToDictionary(c => L(c, "InterfaceIndex")!.Value, c => c.TryGetValue("IPAddress", out var v) && v is string[] a ? a : []);
        return adapters.Select(a => new NetworkAdapterInfo(S(a, "Name"), S(a, "MACAddress"), L(a, "InterfaceIndex") is { } ix && ips.TryGetValue(ix, out var list) ? list : [], L(a, "Speed"), B(a, "NetEnabled") == true)).ToList();
    }
    public static OsInfo? Os(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
        => rows.Count == 0 ? null : new OsInfo(S(rows[0], "Caption"), S(rows[0], "Version"), S(rows[0], "BuildNumber"), S(rows[0], "OSArchitecture"));
}
