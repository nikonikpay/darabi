using Mazesta.Core.Inventory; using Microsoft.Extensions.Logging;
namespace Mazesta.Hardware.Wmi;
public sealed class WmiInventoryProvider(IWmiQuery query, ILogger<WmiInventoryProvider> logger) : IInventoryProvider
{
    private const string Cimv2 = @"root\cimv2", Storage = @"root\Microsoft\Windows\Storage";
    public Task<HardwareInventory> ReadAsync(CancellationToken ct) => Task.Run(() =>
    {
        var errors = new List<string>();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Q(string scope, string wql, string section)
        {
            ct.ThrowIfCancellationRequested();
            try { return query.Query(scope, wql); }
            catch (Exception ex) { logger.LogWarning(ex, "WMI {Section} failed", section); errors.Add($"{section}: {ex.Message}"); return []; }
        }
        return new HardwareInventory(
            WmiInventoryParser.Cpu(Q(Cimv2, "SELECT Name,Manufacturer,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,SocketDesignation FROM Win32_Processor", "cpu")),
            WmiInventoryParser.Gpus(Q(Cimv2, "SELECT Name,DriverVersion,AdapterRAM,PNPDeviceID FROM Win32_VideoController", "gpu")),
            WmiInventoryParser.Memory(Q(Cimv2, "SELECT DeviceLocator,Capacity,Manufacturer,PartNumber,ConfiguredClockSpeed,Speed FROM Win32_PhysicalMemory", "memory")),
            WmiInventoryParser.TotalMemory(Q(Cimv2, "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", "computer")),
            WmiInventoryParser.Board(Q(Cimv2, "SELECT Manufacturer,Product,Version,SerialNumber FROM Win32_BaseBoard", "board")),
            WmiInventoryParser.Bios(Q(Cimv2, "SELECT Manufacturer,SMBIOSBIOSVersion,ReleaseDate,SMBIOSMajorVersion,SMBIOSMinorVersion FROM Win32_BIOS", "bios")),
            WmiInventoryParser.Disks(Q(Storage, "SELECT FriendlyName,SerialNumber,MediaType,BusType,Size,FirmwareVersion,HealthStatus FROM MSFT_PhysicalDisk", "disks")),
            WmiInventoryParser.Adapters(Q(Cimv2, "SELECT Name,MACAddress,Speed,NetEnabled,InterfaceIndex FROM Win32_NetworkAdapter WHERE PhysicalAdapter=TRUE", "adapters"),
                                        Q(Cimv2, "SELECT InterfaceIndex,IPAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=TRUE", "ipconfig")),
            WmiInventoryParser.Os(Q(Cimv2, "SELECT Caption,Version,BuildNumber,OSArchitecture FROM Win32_OperatingSystem", "os")),
            errors);
    }, ct);
}
