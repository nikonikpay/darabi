using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Core.Providers; using Mazesta.Desktop.Composition; using Mazesta.Desktop.ViewModels; using Microsoft.Extensions.Logging.Abstractions; using Xunit;
namespace Mazesta.Desktop.Tests;

public class SystemInfoViewModelTests
{
    private sealed class Inv(HardwareInventory i) : IInventoryProvider { public Task<HardwareInventory> ReadAsync(CancellationToken ct) => Task.FromResult(i); }
    private static InventoryCache Cache(HardwareInventory i) => new(new Inv(i), NullLogger<InventoryCache>.Instance);
    private static readonly string Na = Mazesta.Desktop.Localization.Loc.Get("Value_NotAvailable");

    [Fact]
    public void Every_missing_top_level_field_renders_as_not_available_not_a_crash()
    {
        var sections = SystemInfoViewModel.Describe(HardwareInventory.Empty).ToList();
        Assert.All(sections, s => Assert.All(s.Rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Value))));
        Assert.Contains(sections, s => s.Rows.Any(r => r.Value == Na));
    }

    [Fact]
    public void Cpu_section_lists_every_field()
    {
        var inv = HardwareInventory.Empty with { Cpu = new CpuInfo("i9-14900K", HardwareVendor.Intel, 24, 32, 3200, "LGA1700") };
        var cpu = SystemInfoViewModel.Describe(inv).First(s => s.Title == Mazesta.Desktop.Localization.Loc.Get("Dashboard_Cpu"));
        Assert.Equal("i9-14900K", cpu.Rows.First(r => r.Label == Mazesta.Desktop.Localization.Loc.Get("SystemInfo_Name")).Value);
        Assert.Equal("3200 MHz", cpu.Rows.First(r => r.Label == Mazesta.Desktop.Localization.Loc.Get("SystemInfo_MaxClock")).Value);
        Assert.Equal("Intel", cpu.Rows.First(r => r.Label == Mazesta.Desktop.Localization.Loc.Get("SystemInfo_Vendor")).Value);
    }

    [Fact]
    public void No_gpu_storage_or_network_yields_one_not_available_row_each_not_zero_sections()
    {
        var sections = SystemInfoViewModel.Describe(HardwareInventory.Empty).ToList();
        var gpu = sections.Single(s => s.Title == Mazesta.Desktop.Localization.Loc.Get("Dashboard_Gpu"));
        var storage = sections.Single(s => s.Title == Mazesta.Desktop.Localization.Loc.Get("SystemInfo_Storage"));
        var network = sections.Single(s => s.Title == Mazesta.Desktop.Localization.Loc.Get("SystemInfo_Network"));
        Assert.Equal(Na, gpu.Rows[0].Value); Assert.Equal(Na, storage.Rows[0].Value); Assert.Equal(Na, network.Rows[0].Value);
    }

    [Fact]
    public void Multiple_gpus_get_numbered_section_titles()
    {
        var inv = HardwareInventory.Empty with { Gpus = [new GpuInfo("RTX 4090", "555.99", 24L * 1024 * 1024 * 1024, "PCI\\VEN_10DE"), new GpuInfo("RTX 4060", "555.99", 8L * 1024 * 1024 * 1024, "PCI\\VEN_10DE")] };
        var titles = SystemInfoViewModel.Describe(inv).Select(s => s.Title).ToList();
        Assert.Contains(Mazesta.Desktop.Localization.Loc.Format("SystemInfo_GpuN", 1), titles);
        Assert.Contains(Mazesta.Desktop.Localization.Loc.Format("SystemInfo_GpuN", 2), titles);
    }

    [Fact]
    public void Network_adapter_status_reflects_is_up()
    {
        var inv = HardwareInventory.Empty with { NetworkAdapters = [new NetworkAdapterInfo("Realtek", "AA:BB", ["10.0.0.5"], 1_000_000_000, true)] };
        var net = SystemInfoViewModel.Describe(inv).Single(s => s.Title == Mazesta.Desktop.Localization.Loc.Get("SystemInfo_Network"));
        Assert.Equal(Mazesta.Desktop.Localization.Loc.Get("Value_Connected"), net.Rows.First(r => r.Label == Mazesta.Desktop.Localization.Loc.Get("SystemInfo_Status")).Value);
        Assert.Equal("1000 Mbps", net.Rows.First(r => r.Label == Mazesta.Desktop.Localization.Loc.Get("SystemInfo_LinkSpeed")).Value);
    }

    [Fact]
    public async Task Loaded_populates_sections_and_surfaces_errors_from_the_cache()
    {
        var inv = HardwareInventory.Empty with { Cpu = new CpuInfo("i9", HardwareVendor.Intel, 8, 16, 3000, null), Errors = ["gpu: access denied"] };
        var vm = new SystemInfoViewModel(Cache(inv), a => { a(); return null!; });
        await vm.Loaded;
        Assert.NotEmpty(vm.Sections);
        Assert.Contains("gpu", vm.Status);
    }
}
