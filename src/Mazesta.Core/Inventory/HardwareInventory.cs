using Mazesta.Core.Hardware;
namespace Mazesta.Core.Inventory;

public sealed record CpuInfo(string? Name, HardwareVendor Vendor, int? PhysicalCores, int? LogicalProcessors, int? MaxClockMhz, string? Socket);
public sealed record GpuInfo(string? Name, string? DriverVersion, long? AdapterRamBytes, string? PnpDeviceId);
/// <summary>A module as SMBIOS describes it. <see cref="Ranks"/> is SMBIOS' "attributes"; <see cref="SmbiosType"/> 26 is DDR4, 34 DDR5; a total width
/// wider than the data width means ECC bits.</summary>
public sealed record MemoryModuleInfo(string? Slot, long? CapacityBytes, string? Manufacturer, string? PartNumber, int? ConfiguredSpeedMts, int? SpeedMts,
    string? Bank = null, int? Ranks = null, int? ConfiguredVoltageMv = null, int? SmbiosType = null, int? DataWidth = null, int? TotalWidth = null)
{
    public string? TypeName => SmbiosType switch { 20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 34 => "DDR5", 35 => "LPDDR5", 30 => "LPDDR4", _ => null };
    public bool? Ecc => DataWidth is { } d && TotalWidth is { } t && d > 0 ? t > d : null;
}
public sealed record MotherboardInfo(string? Manufacturer, string? Product, string? Version, string? SerialNumber);
public sealed record BiosInfo(string? Vendor, string? Version, DateTime? ReleaseDate, string? SmbiosVersion);
/// <summary><see cref="WearPercent"/> is the drive's own wear counter (life used), null where it has none (an HDD).</summary>
public sealed record StorageDeviceInfo(string? FriendlyName, string? SerialNumber, string? MediaType, string? BusType, long? SizeBytes, string? FirmwareVersion, string? HealthStatus,
    int? WearPercent = null);
public sealed record NetworkAdapterInfo(string? Name, string? MacAddress, IReadOnlyList<string> IpAddresses, long? LinkSpeedBps, bool IsUp);
public sealed record OsInfo(string? Caption, string? Version, string? BuildNumber, string? Architecture);
public sealed record HardwareInventory(
    CpuInfo? Cpu, IReadOnlyList<GpuInfo> Gpus, IReadOnlyList<MemoryModuleInfo> MemoryModules, long? TotalPhysicalMemoryBytes,
    MotherboardInfo? Motherboard, BiosInfo? Bios, IReadOnlyList<StorageDeviceInfo> Storage, IReadOnlyList<NetworkAdapterInfo> NetworkAdapters,
    OsInfo? Os, IReadOnlyList<string> Errors)
{
    public static readonly HardwareInventory Empty = new(null, [], [], null, null, null, [], [], null, []);
}
