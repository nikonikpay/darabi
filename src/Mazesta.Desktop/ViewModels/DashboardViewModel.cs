using System.Collections.ObjectModel; using System.Diagnostics; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input; using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Desktop.Localization; using Mazesta.Hardware; using Mazesta.Monitoring; using Mazesta.Persistence;
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
    public DashboardViewModel(PollingEngine engine, IInventoryProvider inventory, AppConfig config, Func<Action, object> dispatch)
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
    private async Task LoadInventoryAsync(IInventoryProvider provider)
    {
        var inv = await provider.ReadAsync(CancellationToken.None).ConfigureAwait(false);
        _dispatch(() =>
        {
            void Add(string label, string? value) => Inventory.Add((label, value ?? Loc.Get("Value_NotAvailable")));
            Add("CPU", inv.Cpu is { } c ? $"{c.Name} ({c.PhysicalCores}C/{c.LogicalProcessors}T)" : null);
            foreach (var g in inv.Gpus) Add("GPU", $"{g.Name} · driver {g.DriverVersion}");
            Add("RAM", inv.TotalPhysicalMemoryBytes is { } t ? $"{t / 1024.0 / 1024 / 1024:F0} GB · {inv.MemoryModules.Count} modules" : null);
            Add("Motherboard", inv.Motherboard is { } m ? $"{m.Manufacturer} {m.Product}" : null); Add("BIOS", inv.Bios is { } b ? $"{b.Version} ({b.ReleaseDate:yyyy-MM-dd})" : null);
            foreach (var d in inv.Storage) Add("Disk", $"{d.FriendlyName} · {d.BusType} · {d.SizeBytes / 1e9:F0} GB · {d.HealthStatus}");
            Add("OS", inv.Os is { } o ? $"{o.Caption} {o.Version}" : null);
            InventoryStatus = inv.Errors.Count == 0 ? "" : string.Join("; ", inv.Errors);
            App.LogStartup("Inventory ready");
        });
    }
    [RelayCommand] private void OpenUrl(string? url) { if (url is not null) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    public void Dispose() => _engine.SnapshotPublished -= OnSnapshot;
}
