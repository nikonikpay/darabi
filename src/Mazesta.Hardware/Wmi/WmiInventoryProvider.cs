using Mazesta.Core.Inventory; using Microsoft.Extensions.Logging;
namespace Mazesta.Hardware.Wmi;
public sealed class WmiInventoryProvider(IWmiQuery query, ILogger<WmiInventoryProvider> logger) : IInventoryProvider
{
    private const string Cimv2 = @"root\cimv2", Storage = @"root\Microsoft\Windows\Storage";
    public Task<HardwareInventory> ReadAsync(CancellationToken ct) => Task.Run(() =>
    {
        var errors = new List<string>();
        T Section<T>(string name, Func<T> compute, T fallback)
        {
            ct.ThrowIfCancellationRequested();
            try { return compute(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "WMI {Section} failed", name); errors.Add($"{name}: {ex.Message}"); return fallback; }
        }
        return new HardwareInventory(
            Section<CpuInfo?>("cpu", () => WmiInventoryParser.Cpu(query.Query(Cimv2, "SELECT Name,Manufacturer,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,SocketDesignation FROM Win32_Processor")), null),
            Section<IReadOnlyList<GpuInfo>>("gpu", () => WmiInventoryParser.Gpus(query.Query(Cimv2, "SELECT Name,DriverVersion,AdapterRAM,PNPDeviceID FROM Win32_VideoController")), []),
            Section<IReadOnlyList<MemoryModuleInfo>>("memory", () => WmiInventoryParser.Memory(query.Query(Cimv2, "SELECT DeviceLocator,Capacity,Manufacturer,PartNumber,ConfiguredClockSpeed,Speed FROM Win32_PhysicalMemory")), []),
            Section<long?>("computer", () => WmiInventoryParser.TotalMemory(query.Query(Cimv2, "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem")), null),
            Section<MotherboardInfo?>("board", () => WmiInventoryParser.Board(query.Query(Cimv2, "SELECT Manufacturer,Product,Version,SerialNumber FROM Win32_BaseBoard")), null),
            Section<BiosInfo?>("bios", () => WmiInventoryParser.Bios(query.Query(Cimv2, "SELECT Manufacturer,SMBIOSBIOSVersion,ReleaseDate,SMBIOSMajorVersion,SMBIOSMinorVersion FROM Win32_BIOS")), null),
            Section<IReadOnlyList<StorageDeviceInfo>>("disks", () => WmiInventoryParser.Disks(query.Query(Storage, "SELECT FriendlyName,SerialNumber,MediaType,BusType,Size,FirmwareVersion,HealthStatus FROM MSFT_PhysicalDisk")), []),
            Section<IReadOnlyList<NetworkAdapterInfo>>("adapters", () => WmiInventoryParser.Adapters(
                query.Query(Cimv2, "SELECT Name,MACAddress,Speed,NetEnabled,InterfaceIndex FROM Win32_NetworkAdapter WHERE PhysicalAdapter=TRUE"),
                query.Query(Cimv2, "SELECT InterfaceIndex,IPAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=TRUE")), []),
            Section<OsInfo?>("os", () => WmiInventoryParser.Os(query.Query(Cimv2, "SELECT Caption,Version,BuildNumber,OSArchitecture FROM Win32_OperatingSystem")), null),
            errors);
    }, ct);
}
