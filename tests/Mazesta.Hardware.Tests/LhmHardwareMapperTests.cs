using LibreHardwareMonitor.Hardware; using Mazesta.Core.Hardware; using Mazesta.Hardware.Lhm; using Mazesta.Hardware.Tests.Fakes;
using Xunit;
namespace Mazesta.Hardware.Tests;
public class LhmHardwareMapperTests
{
    // The identifier tokens below are the ones LibreHardwareMonitor 0.9.6 actually produced on the
    // dev box ("/gpu-nvidia/0", "/gpu-intel/0", "/intelcpu/0", "/lpc/nct6687d/0", "/nvme/0", "/ram",
    // "/vram"); they are recorded in docs/HARDWARE-MATRIX.md. Earlier fakes used invented spellings
    // such as "/nvidiagpu/0", which no live tree ever emits.
    private static LhmHardwareMapper Mapper(string? serial = null) => new(_ => serial);
    [Fact] public void Nvidia_gpu_maps_kind_vendor_id_and_roles()
    {
        var gpu = new FakeHardware(HardwareType.GpuNvidia, "/gpu-nvidia/0", "NVIDIA GeForce RTX 4090");
        gpu.Add("GPU Core", SensorType.Temperature, 0, 41); gpu.Add("GPU Hot Spot", SensorType.Temperature, 1, 52);
        var node = Assert.Single(Mapper().Map([gpu]));
        Assert.Equal((HardwareKind.Gpu, HardwareVendor.Nvidia, "gpu/gpu-nvidia-0", true), (node.Node.Kind, node.Node.Vendor, node.Node.Id.Value, node.Node.IdIsStable));
        Assert.Contains(node.Sensors, s => s.Definition.Role == SensorRole.GpuHotSpotTemp && s.Definition.Id.Value == "gpu/gpu-nvidia-0#temperature/1");
        Assert.Equal(Unit.Celsius, node.Sensors[0].Definition.Unit);
    }
    [Fact] public void Gpu_without_hot_spot_yields_no_hot_spot_role()
    {
        var gpu = new FakeHardware(HardwareType.GpuIntel, "/gpu-intel/0", "Intel(R) UHD Graphics 770");
        gpu.Add("GPU Core", SensorType.Temperature, 0, 40);
        Assert.DoesNotContain(Mapper().Map([gpu])[0].Sensors, s => s.Definition.Role == SensorRole.GpuHotSpotTemp);
    }
    [Fact] public void Cpu_vendor_comes_from_identifier_not_name()
    {
        var cpu = new FakeHardware(HardwareType.Cpu, "/amdcpu/0", "Some GPU-looking name");
        Assert.Equal(HardwareVendor.Amd, Mapper().Map([cpu])[0].Node.Vendor);
        Assert.Equal(HardwareVendor.Intel, Mapper().Map([new FakeHardware(HardwareType.Cpu, "/intelcpu/0", "x")])[0].Node.Vendor);
    }
    [Fact] public void Storage_uses_serial_when_available_and_provider_path_otherwise()
    {
        var disk = new FakeHardware(HardwareType.Storage, "/nvme/0", "Samsung SSD 990 PRO 2TB");
        Assert.Equal(("storage/S7KXNJ0X", true), (Mapper("S7KXNJ0X").Map([disk])[0].Node.Id.Value, Mapper("S7KXNJ0X").Map([disk])[0].Node.IdIsStable));
        Assert.Equal(("storage/nvme-0", false), (Mapper(null).Map([disk])[0].Node.Id.Value, Mapper(null).Map([disk])[0].Node.IdIsStable));
    }
    [Fact] public void SubHardware_is_flattened_with_parent_id()
    {
        var board = new FakeHardware(HardwareType.Motherboard, "/motherboard", "MSI Z790");
        var sio = new FakeHardware(HardwareType.SuperIO, "/lpc/nct6687d/0", "Nuvoton NCT6687D", board); sio.Add("CPU Fan", SensorType.Fan, 0, 900);
        board.SubHardware = [sio];
        var nodes = Mapper().Map([board]);
        Assert.Equal(2, nodes.Count);
        Assert.Equal(nodes[0].Node.Id, nodes[1].Node.ParentId);
        Assert.Equal(HardwareKind.Motherboard, nodes[1].Node.Kind);
    }
    [Fact] public void Hidden_sensors_are_skipped()
    {
        var mem = new FakeHardware(HardwareType.Memory, "/memory/dimm/0", "DIMM"); mem.Add("Thermal Sensor Low Limit", SensorType.Temperature, 2, 0, hidden: true); mem.Add("DIMM #1", SensorType.Temperature, 0, 38);
        Assert.Single(Mapper().Map([mem])[0].Sensors);
    }

    // Regression: an NVIDIA driver reported two loads with the same identifier; the duplicate key crashed the whole Monitoring page.
    [Fact] public void A_duplicated_sensor_identifier_is_mapped_once()
    {
        var gpu = new FakeHardware(HardwareType.GpuNvidia, "/gpu-nvidia/0", "NVIDIA GeForce RTX 3090"); gpu.Add("GPU Video Engine", SensorType.Load, 3, 1); gpu.Add("GPU Other Engine", SensorType.Load, 3, 2);
        var sensors = Mapper().Map([gpu])[0].Sensors;
        Assert.Single(sensors); Assert.Equal("GPU Video Engine", sensors[0].Definition.Name);
    }
}
