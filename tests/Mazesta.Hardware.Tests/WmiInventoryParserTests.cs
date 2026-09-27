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
    [Fact] public void Out_of_range_integer_becomes_null()
    {
        Assert.Null(WmiInventoryParser.Cpu([Row(("MaxClockSpeed", 4294967295u))])!.MaxClockMhz);
    }
    [Fact] public void Adapters_exclude_virtual_bindings()
    {
        var a = WmiInventoryParser.Adapters(
            [Row(("Name", "Intel Wi-Fi")), Row(("Name", "Wi-Fi-QoS Packet Scheduler-0000")), Row(("Name", "Ethernet 5-WFP Native MAC Layer LightWeight Filter-0000"))],
            []);
        Assert.Single(a); Assert.Equal("Intel Wi-Fi", a[0].Name);
    }
    [Fact] public void A_saturated_adapter_ram_is_unknown_unless_the_driver_says_the_size()
    {
        var rows = new[] { Row(("Name", "NVIDIA GeForce RTX 3090"), ("AdapterRAM", 4293918720u), ("PNPDeviceID", @"PCI\VEN_10DE&DEV_2204&SUBSYS_136A196E&REV_A1\4&2AE1B128&0&0019")) };
        Assert.Null(WmiInventoryParser.Gpus(rows)[0].AdapterRamBytes);
        long? Driver(string? pnp) => GpuDriverMemory.Match([(@"pci\ven_10de&dev_2204", 24L << 30), (@"pci\ven_10de&dev_2204&subsys_136a196e", 25L << 30)], pnp);
        Assert.Equal(25L << 30, WmiInventoryParser.Gpus(rows, Driver)[0].AdapterRamBytes);   // the most specific match
        Assert.Equal(2L << 30, WmiInventoryParser.Gpus([Row(("AdapterRAM", 2147483648u))])[0].AdapterRamBytes);
    }
    [Fact] public void A_disconnected_adapter_has_no_link_speed()
        => Assert.Null(WmiInventoryParser.Adapters([Row(("Name", "Realtek PCIe GbE"), ("Speed", 9223372036854775807ul), ("NetEnabled", false), ("InterfaceIndex", 3u))], [])[0].LinkSpeedBps);
}
