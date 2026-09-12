using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Desktop.ViewModels; using Mazesta.Core.Providers; using Mazesta.Monitoring; using Mazesta.Monitoring.Tests.Fakes; using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions; using Xunit;
namespace Mazesta.Desktop.Tests;
public class DashboardViewModelTests
{
    private sealed class Inv(HardwareInventory i) : IInventoryProvider { public int Reads; public Task<HardwareInventory> ReadAsync(CancellationToken ct) { Reads++; return Task.FromResult(i); } }
    private static Mazesta.Desktop.Composition.InventoryCache Cache(HardwareInventory i) => new(new Inv(i), NullLogger<Mazesta.Desktop.Composition.InventoryCache>.Instance);
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static HardwareNode Gpu(bool hotSpot)
    {
        var id = new HardwareId("gpu/gpu-nvidia-0"); var sensors = new List<SensorDefinition> { new(SensorId.Create(id, "temperature/0"), id, "GPU Core", SensorKind.Temperature, Unit.Celsius, SensorRole.GpuCoreTemp, 0) };
        if (hotSpot) sensors.Add(new(SensorId.Create(id, "temperature/1"), id, "GPU Hot Spot", SensorKind.Temperature, Unit.Celsius, SensorRole.GpuHotSpotTemp, 1));
        return new HardwareNode(id, HardwareKind.Gpu, HardwareVendor.Nvidia, "RTX 4090", null, true, sensors);
    }
    private static (DashboardViewModel vm, PollingEngine e, FakeSensorProvider p) Build(bool hotSpot)
    {
        var c = new FakeClock(T0); var p = new FakeSensorProvider(); p.Nodes.Add(Gpu(hotSpot));
        var e = new PollingEngine(p, c, new MonitoringOptions(), new BoundedEventLog(c, NullLogger.Instance)); e.PrepareForManualTicks();
        var inv = HardwareInventory.Empty with { Cpu = new CpuInfo("i9", HardwareVendor.Intel, 24, 32, 3200, null), Errors = ["bios: access denied"] };
        return (new DashboardViewModel(e, Cache(inv), new AppConfig { ShopName = "X" }, a => { a(); return null!; }), e, p);
    }
    [Fact] public void Hot_spot_card_reads_not_available_when_gpu_lacks_sensor()
    { var (vm, e, _) = Build(false); vm.ApplySnapshot(e.TickOnce()!); Assert.False(vm.HotSpot.HasAnySensor); Assert.Equal(Mazesta.Desktop.Localization.Loc.Get("Value_NotAvailable"), vm.HotSpot.Lines[0].Value); }
    [Fact] public void Hot_spot_card_shows_value_when_present()
    { var (vm, e, _) = Build(true); vm.ApplySnapshot(e.TickOnce()!); Assert.True(vm.HotSpot.HasAnySensor); Assert.Equal("42.0 °C", vm.HotSpot.Lines[0].Value); }
    [Fact] public void Pick_prefers_first_role_in_order()
    {
        var node = Gpu(true); Assert.Equal(SensorRole.GpuHotSpotTemp, DashboardViewModel.Pick(node, SensorRole.GpuHotSpotTemp, SensorRole.GpuCoreTemp)!.Role);
        Assert.Equal(SensorRole.GpuCoreTemp, DashboardViewModel.Pick(Gpu(false), SensorRole.GpuHotSpotTemp, SensorRole.GpuCoreTemp)!.Role); Assert.Null(DashboardViewModel.Pick(Gpu(false), SensorRole.GpuVramTemp));
    }
    [Fact] public async Task Inventory_lists_available_fields_and_surfaces_errors()
    { var (vm, _, _) = Build(true); await vm.InventoryLoaded; Assert.Contains(vm.Inventory, i => i.Label == "CPU" && i.Value.Contains("i9")); Assert.Contains("bios", vm.InventoryStatus); }
    [Fact] public void Product_card_has_no_price_and_official_links()
    { var (vm, _, _) = Build(true); Assert.DoesNotContain("تومان", vm.ProductDescription); Assert.StartsWith("https://www.dfmrendering.com/", vm.ProductUrl); Assert.Equal("https://www.dfmrendering.com/contactus/", vm.ContactUrl); }
    [Fact] public async Task Cached_inventory_is_read_once_no_matter_how_many_pages_ask()
    {
        var probe = new Inv(HardwareInventory.Empty with { Cpu = new CpuInfo("i9", HardwareVendor.Intel, 24, 32, 3200, null) });
        var cache = new Mazesta.Desktop.Composition.InventoryCache(probe, NullLogger<Mazesta.Desktop.Composition.InventoryCache>.Instance);
        var c = new FakeClock(T0); var p = new FakeSensorProvider(); p.Nodes.Add(Gpu(true));
        var e = new PollingEngine(p, c, new MonitoringOptions(), new BoundedEventLog(c, NullLogger.Instance)); e.PrepareForManualTicks();
        for (int i = 0; i < 3; i++) await new DashboardViewModel(e, cache, new AppConfig { ShopName = "X" }, a => { a(); return null!; }).InventoryLoaded;
        Assert.Equal(1, probe.Reads);
    }
    [Fact] public void Every_null_inventory_field_renders_as_not_available()
    {
        // A fully-null StorageDeviceInfo/CpuInfo/OsInfo: no blanks, no bare "C/T", no "0 GB".
        var inv = HardwareInventory.Empty with
        {
            Cpu = new CpuInfo(null, HardwareVendor.Unknown, null, null, null, null),
            Storage = [new StorageDeviceInfo(null, null, null, null, null, null, null)],
            Motherboard = new MotherboardInfo(null, null, null, null),
            Os = new OsInfo(null, null, null, null),
        };
        var na = Mazesta.Desktop.Localization.Loc.Get("Value_NotAvailable");
        var rows = DashboardViewModel.DescribeInventory(inv).ToList();
        foreach (var (label, value) in rows) { Assert.False(string.IsNullOrWhiteSpace(value), label); Assert.DoesNotContain("  ", value); }
        string Row(string label) => rows.First(r => r.Label == label).Value;
        Assert.Equal($"{na} ({na}C/{na}T)", Row("CPU"));
        Assert.Equal($"{na} · {na} · {na} · {na}", Row("Disk"));
        Assert.Equal($"{na} {na}", Row("OS"));
        Assert.Equal($"{na} {na}", Row("Motherboard"));
        Assert.Equal($"{na} · 0 modules", Row("RAM"));
        Assert.Equal(na, Row("GPU"));
        Assert.Equal(na, Row("BIOS"));
    }
    [Fact] public void Ram_card_shows_installed_total_and_module_lines()
    {
        var (vm, _, _) = Build(true);
        vm.Ram.Lines.Clear();
        vm.ApplyMemoryInventory(HardwareInventory.Empty with
        {
            TotalPhysicalMemoryBytes = 64L * 1024 * 1024 * 1024,
            MemoryModules = [new MemoryModuleInfo("DIMM A1", 32L * 1024 * 1024 * 1024, "G.Skill", "F5-6000J3038F16G", 6000, 6000), new MemoryModuleInfo("DIMM B1", 32L * 1024 * 1024 * 1024, "G.Skill", "F5-6000J3038F16G", 6000, 6000)]
        });
        Assert.Equal("64 GB", vm.Ram.Lines[0].Value);
        Assert.Equal(3, vm.Ram.Lines.Count);
        Assert.Contains("F5-6000J3038F16G", vm.Ram.Lines[1].Value);
    }
    [Fact] public void Ram_card_collapses_more_than_four_modules_into_a_count_line()
    {
        var (vm, _, _) = Build(true); vm.Ram.Lines.Clear();
        var m = new MemoryModuleInfo("DIMM", 16L * 1024 * 1024 * 1024, "Kingston", "KF560C36-16", 6000, 6000);
        vm.ApplyMemoryInventory(HardwareInventory.Empty with { TotalPhysicalMemoryBytes = 96L * 1024 * 1024 * 1024, MemoryModules = [m, m, m, m, m, m] });
        Assert.Equal(2, vm.Ram.Lines.Count);
        Assert.Equal("6 × KF560C36-16", vm.Ram.Lines[1].Value);
    }
    [Fact] public async Task Inventory_renders_not_available_for_null_driver_and_bios_date()
    {
        var c = new FakeClock(T0); var p = new FakeSensorProvider(); p.Nodes.Add(Gpu(true));
        var e = new PollingEngine(p, c, new MonitoringOptions(), new BoundedEventLog(c, NullLogger.Instance)); e.PrepareForManualTicks();
        var inv = HardwareInventory.Empty with { Gpus = [new GpuInfo("RTX 4090", null, null, null)], Bios = new BiosInfo("American Megatrends", "F.10", null, null) };
        var vm = new DashboardViewModel(e, Cache(inv), new AppConfig { ShopName = "X" }, a => { a(); return null!; });
        await vm.InventoryLoaded;
        var na = Mazesta.Desktop.Localization.Loc.Get("Value_NotAvailable");
        var gpuLine = vm.Inventory.First(i => i.Label == "GPU"); var biosLine = vm.Inventory.First(i => i.Label == "BIOS");
        Assert.Contains(na, gpuLine.Value); Assert.Contains(na, biosLine.Value);
        Assert.False(gpuLine.Value.EndsWith("driver ")); Assert.DoesNotContain("()", biosLine.Value);
    }
}
