using Mazesta.Core.Hardware; using Mazesta.Hardware.Wmi;
using Xunit;
namespace Mazesta.Hardware.Tests;
public class WmiInventoryParserTests
{
    private static IReadOnlyDictionary<string, object?> Row(params (string k, object? v)[] kv) => kv.ToDictionary(x => x.k, x => x.v);
    [Fact] public void Cpu_vendor_from_manufacturer_field()
    {
        var cpu = WmiInventoryParser.Cpu([Row(("Name", "Intel(R) Core(TM) i9-14900K"), ("Manufacturer", "GenuineIntel"), ("NumberOfCores", 24u), ("NumberOfLogicalProcessors", 32u), ("MaxClockSpeed", 3200u), ("SocketDesignation", "LGA1700"))]);
        Assert.NotNull(cpu); Assert.Equal((HardwareVendor.Intel, 24, 32), (cpu!.Vendor, cpu.PhysicalCores, cpu.LogicalProcessors));
        Assert.Equal(HardwareVendor.Amd, WmiInventoryParser.Cpu([Row(("Manufacturer", "AuthenticAMD"))])!.Vendor);
    }
    [Fact] public void Disk_media_and_bus_codes_are_named()
    {
        var d = WmiInventoryParser.Disks([Row(("FriendlyName", "Samsung SSD 990 PRO 2TB"), ("SerialNumber", " S7KX "), ("MediaType", (ushort)4), ("BusType", (ushort)17), ("Size", 2000398934016ul), ("FirmwareVersion", "4B2QJXD7"), ("HealthStatus", (ushort)0))]);
        Assert.Equal(("S7KX", "SSD", "NVMe", "Healthy"), (d[0].SerialNumber, d[0].MediaType, d[0].BusType, d[0].HealthStatus));
    }
    [Fact] public void Adapters_join_ip_configuration_by_index()
    {
        var a = WmiInventoryParser.Adapters(
            [Row(("Name", "Intel Wi-Fi"), ("MACAddress", "AA:BB"), ("Speed", 866000000ul), ("NetEnabled", true), ("InterfaceIndex", 12u))],
            [Row(("InterfaceIndex", 12u), ("IPAddress", new[] { "192.168.1.5", "fe80::1" }))]);
        Assert.Equal(["192.168.1.5", "fe80::1"], a[0].IpAddresses); Assert.True(a[0].IsUp); Assert.Equal(866000000L, a[0].LinkSpeedBps);
    }
    [Fact] public void Missing_fields_become_null_not_defaults()
    {
        var m = WmiInventoryParser.Memory([Row(("DeviceLocator", "DIMM_A1"))]);
        Assert.Null(m[0].CapacityBytes); Assert.Null(m[0].ConfiguredSpeedMts); Assert.Equal("DIMM_A1", m[0].Slot);
    }
}
