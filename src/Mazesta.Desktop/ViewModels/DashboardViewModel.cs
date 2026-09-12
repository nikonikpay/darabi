using System.Collections.ObjectModel; using System.Diagnostics; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input; using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly PollingEngine _engine; private readonly Func<Action, object> _dispatch; private readonly Dictionary<SensorId, List<CardLine>> _lines = [];
    public SensorCardViewModel Cpu { get; } = new("Dashboard_Cpu", HardwareKind.Cpu); public SensorCardViewModel HotSpot { get; } = new("Dashboard_HotSpot", HardwareKind.Gpu); public SensorCardViewModel Ram { get; } = new("Dashboard_Ram", HardwareKind.Memory);
    public ObservableCollection<SensorCardViewModel> Gpus { get; } = []; public ObservableCollection<(string Label, string Value)> Inventory { get; } = [];
    [ObservableProperty] private string _inventoryStatus = ""; public string ShopName { get; }
    public string ProductName => "سیستم رندر معماری و شبیه‌سازی AM9"; public string ProductDescription => "Ryzen 9 9900X · RTX 5060 Ti 16GB · 48GB DDR5";
    public string ProductUrl => "https://www.dfmrendering.com/shop/systems/am9-architectural-rendering-ryzen-9900x-rtx5060ti/"; public string SiteUrl => "https://www.dfmrendering.com/"; public string ContactUrl => "https://www.dfmrendering.com/contactus/"; public string Phones => "09197588700 · 09197588701";
    public Task InventoryLoaded { get; }
    public DashboardViewModel(PollingEngine engine, InventoryCache inventory, AppConfig config, Func<Action, object> dispatch)
    {
        _engine = engine; _dispatch = dispatch; ShopName = config.ShopName;
        foreach (var node in engine.Hardware.Where(n => n.ParentId is null))
            switch (node.Kind)
            {
                case HardwareKind.Cpu: Line(Cpu, "Package", node, SensorRole.CpuPackageTemp, SensorRole.CpuTctlTdie); Line(Cpu, "Clock", node, SensorRole.CpuEffectiveClockAverage, SensorRole.CpuCoreClockAverage, SensorRole.CpuCoreClock); Line(Cpu, "Load", node, SensorRole.CpuTotalLoad); Line(Cpu, "Power", node, SensorRole.CpuPackagePower); break;
                case HardwareKind.Gpu:
                    var card = new SensorCardViewModel("Dashboard_Gpu", HardwareKind.Gpu, node.Name); Gpus.Add(card);
                    Line(card, "Core", node, SensorRole.GpuCoreTemp); Line(card, "Load", node, SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D); Line(card, "Clock", node, SensorRole.GpuCoreClock); Line(card, "Power", node, SensorRole.GpuPower); Line(card, "VRAM used", node, SensorRole.GpuVramUsed); Line(card, "VRAM total", node, SensorRole.GpuVramTotal);
                    Line(HotSpot, node.Name, node, SensorRole.GpuHotSpotTemp); break;
                case HardwareKind.Memory when node.Id.Value == "memory/ram": Line(Ram, "Used", node, SensorRole.RamUsed); Line(Ram, "Free", node, SensorRole.RamFree); Line(Ram, "Load", node, SensorRole.RamLoad); break;
            }
        if (HotSpot.Lines.Count == 0) HotSpot.Lines.Add(new CardLine("GPU", null, Unit.Celsius));
        engine.SnapshotPublished += OnSnapshot;
        InventoryLoaded = LoadInventoryAsync(inventory);
    }
    private void Line(SensorCardViewModel card, string label, HardwareNode node, params SensorRole[] roles)
    {
        var def = Pick(node, roles); var line = new CardLine(label, def?.Id, def?.Unit ?? Unit.None); card.Lines.Add(line);
        if (def is not null) { if (!_lines.TryGetValue(def.Id, out var list)) _lines[def.Id] = list = []; list.Add(line); }
    }
    internal static SensorDefinition? Pick(HardwareNode node, params SensorRole[] preference) => preference.Select(r => node.Sensors.FirstOrDefault(s => s.Role == r)).FirstOrDefault(s => s is not null);
    private void OnSnapshot(SensorSnapshot s) => _dispatch(() => ApplySnapshot(s));
    internal void ApplySnapshot(SensorSnapshot s)
    {
        foreach (var r in s.Readings) if (_lines.TryGetValue(r.Id, out var lines)) foreach (var l in lines) l.Value = r.Quality == DataQuality.Ok && r.Value is { } v ? Units.FormatWithSymbol(v, l.Unit) : Loc.Get("Value_NotAvailable");
    }
    /// <summary>Renders any nullable inventory field as the localized not-available text rather
    /// than as a blank, a bare "C/T" or a misleading "0 GB" (spec §4: every null field is shown as
    /// «دریافت نشد» / "Not available").</summary>
    internal static string Show(object? value, string? format = null) => value switch
    {
        null => Loc.Get("Value_NotAvailable"),
        string s when s.Trim().Length == 0 => Loc.Get("Value_NotAvailable"),
        IFormattable f when format is not null => f.ToString(format, System.Globalization.CultureInfo.CurrentCulture),
        _ => value.ToString() is { Length: > 0 } t ? t : Loc.Get("Value_NotAvailable")
    };
    private static string ShowBytes(long? bytes, double divisor) => bytes is { } b ? (b / divisor).ToString("F0", System.Globalization.CultureInfo.CurrentCulture) + " GB" : Loc.Get("Value_NotAvailable");

    internal static IEnumerable<(string Label, string Value)> DescribeInventory(HardwareInventory inv)
    {
        yield return ("CPU", inv.Cpu is { } c ? $"{Show(c.Name)} ({Show(c.PhysicalCores)}C/{Show(c.LogicalProcessors)}T)" : Loc.Get("Value_NotAvailable"));
        if (inv.Gpus.Count == 0) yield return ("GPU", Loc.Get("Value_NotAvailable"));
        foreach (var g in inv.Gpus) yield return ("GPU", $"{Show(g.Name)} · driver {Show(g.DriverVersion)}");
        yield return ("RAM", $"{ShowBytes(inv.TotalPhysicalMemoryBytes, 1024.0 * 1024 * 1024)} · {inv.MemoryModules.Count} modules");
        yield return ("Motherboard", inv.Motherboard is { } m ? $"{Show(m.Manufacturer)} {Show(m.Product)}" : Loc.Get("Value_NotAvailable"));
        yield return ("BIOS", inv.Bios is { } b ? $"{Show(b.Version)} ({Show(b.ReleaseDate, "yyyy-MM-dd")})" : Loc.Get("Value_NotAvailable"));
        if (inv.Storage.Count == 0) yield return ("Disk", Loc.Get("Value_NotAvailable"));
        foreach (var d in inv.Storage) yield return ("Disk", $"{Show(d.FriendlyName)} · {Show(d.BusType)} · {ShowBytes(d.SizeBytes, 1e9)} · {Show(d.HealthStatus)}");
        yield return ("OS", inv.Os is { } o ? $"{Show(o.Caption)} {Show(o.Version)}" : Loc.Get("Value_NotAvailable"));
    }

    private async Task LoadInventoryAsync(InventoryCache cache)
    {
        var inv = await cache.GetAsync().ConfigureAwait(false);
        _dispatch(() =>
        {
            foreach (var row in DescribeInventory(inv)) Inventory.Add(row);
            ApplyMemoryInventory(inv);
            InventoryStatus = inv.Errors.Count == 0 ? "" : string.Join("; ", inv.Errors);
            App.LogStartup("Inventory shown");
        });
    }

    /// <summary>Adds the RAM card's inventory lines: the installed total, and the modules (one line
    /// per module up to four, otherwise a count plus the first module's part number).</summary>
    internal void ApplyMemoryInventory(HardwareInventory inv)
    {
        Ram.Lines.Add(new CardLine(Loc.Get("Dashboard_Ram_Total"), null, Unit.None) { Value = ShowBytes(inv.TotalPhysicalMemoryBytes, 1024.0 * 1024 * 1024) });
        var modules = inv.MemoryModules;
        if (modules.Count == 0) { Ram.Lines.Add(new CardLine(Loc.Get("Dashboard_Ram_Modules"), null, Unit.None) { Value = Loc.Get("Value_NotAvailable") }); return; }
        if (modules.Count <= 4)
            foreach (var m in modules)
                Ram.Lines.Add(new CardLine(Show(m.Slot), null, Unit.None) { Value = $"{ShowBytes(m.CapacityBytes, 1024.0 * 1024 * 1024)} · {Show(m.PartNumber)}" });
        else
            Ram.Lines.Add(new CardLine(Loc.Get("Dashboard_Ram_Modules"), null, Unit.None) { Value = $"{modules.Count} × {Show(modules[0].PartNumber)}" });
    }
    [RelayCommand] private void OpenUrl(string? url) { if (url is not null) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    public void Dispose() => _engine.SnapshotPublished -= OnSnapshot;
}
