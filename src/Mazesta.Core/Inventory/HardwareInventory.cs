using Mazesta.Core.Hardware;
namespace Mazesta.Core.Inventory;

public sealed record CpuInfo(string? Name, HardwareVendor Vendor, int? PhysicalCores, int? LogicalProcessors, int? MaxClockMhz, string? Socket);
public sealed record GpuInfo(string? Name, string? DriverVersion, long? AdapterRamBytes, string? PnpDeviceId);
public sealed record MemoryModuleInfo(string? Slot, long? CapacityBytes, string? Manufacturer, string? PartNumber, int? ConfiguredSpeedMts, int? SpeedMts);
public sealed record MotherboardInfo(string? Manufacturer, string? Product, string? Version, string? SerialNumber);
public sealed record BiosInfo(string? Vendor, string? Version, DateTime? ReleaseDate, string? SmbiosVersion);
public sealed record StorageDeviceInfo(string? FriendlyName, string? SerialNumber, string? MediaType, string? BusType, long? SizeBytes, string? FirmwareVersion, string? HealthStatus);
public sealed record NetworkAdapterInfo(string? Name, string? MacAddress, IReadOnlyList<string> IpAddresses, long? LinkSpeedBps, bool IsUp);
public sealed record OsInfo(string? Caption, string? Version, string? BuildNumber, string? Architecture);
public sealed record HardwareInventory(
    CpuInfo? Cpu, IReadOnlyList<GpuInfo> Gpus, IReadOnlyList<MemoryModuleInfo> MemoryModules, long? TotalPhysicalMemoryBytes,
    MotherboardInfo? Motherboard, BiosInfo? Bios, IReadOnlyList<StorageDeviceInfo> Storage, IReadOnlyList<NetworkAdapterInfo> NetworkAdapters,
    OsInfo? Os, IReadOnlyList<string> Errors)
{
    public static readonly HardwareInventory Empty = new(null, [], [], null, null, null, [], [], null, []);
}
