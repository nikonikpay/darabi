using System.Collections.ObjectModel; using System.Globalization; using CommunityToolkit.Mvvm.ComponentModel;
using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization; using Mazesta.Monitoring;
namespace Mazesta.Desktop.ViewModels;

/// <summary>One line of the overlay: what it is and its current value. The value is the not-available dash when the sensor has no good reading.</summary>
public sealed partial class OverlayRow(string label, Unit unit, IReadOnlyList<SensorId> sensors) : ObservableObject
{
    public string Label { get; } = label;
    internal Unit Unit { get; } = unit;
    /// <summary>Usually one sensor; the network rows add up every adapter's.</summary>
    internal IReadOnlyList<SensorId> Sensors { get; } = sensors;
    [ObservableProperty] private string _value = OverlayViewModel.Missing;
}

/// <summary>A block of the overlay (GPU, CPU, RAM, NET): its rows, and for GPU and CPU a short trend of load and temperature.</summary>
public sealed partial class OverlaySection(string title, string accentKey) : ObservableObject
{
    public string Title { get; } = title;
    public string AccentKey { get; } = accentKey;
    public ObservableCollection<OverlayRow> Rows { get; } = [];
    internal SensorId? LoadSensor, TempSensor;
    public bool HasTrend => LoadSensor is not null || TempSensor is not null;
    /// <summary>The last <see cref="OverlayViewModel.TrendLength"/> polls, oldest first; NaN where there was no reading.</summary>
    [ObservableProperty] private double[] _loadTrend = [];
    [ObservableProperty] private double[] _tempTrend = [];
}

/// <summary>
/// The always-on-top overlay: the numbers a technician watches while a game or a stress test runs full screen (borderless), in the corner of the
/// screen, with a one-minute trend of GPU and CPU load and temperature. It reads the monitor's own snapshots - no extra polling - and only
/// while it is shown. Every value is a measured reading; a sensor the machine lacks is left out, one without a current reading shows a dash.
/// Frame rate is not shown: the app does not measure it (that needs PresentMon/ETW frame tracing).
/// </summary>
public sealed class OverlayViewModel : IDisposable
{
    public const string Missing = "—";
    public const int TrendLength = 60;
    private readonly PollingEngine _engine; private readonly Func<Action, object> _dispatch;
    private readonly Dictionary<SensorId, double?> _latest = [];
    private readonly Dictionary<OverlaySection, (Queue<double> Load, Queue<double> Temp)> _trends = [];
    private bool _active;

    public IReadOnlyList<OverlaySection> Sections { get; }

    public OverlayViewModel(PollingEngine engine, Func<Action, object> dispatch)
    {
        _engine = engine; _dispatch = dispatch;
        Sections = Build(engine.Hardware);
        foreach (var s in Sections) _trends[s] = (new(), new());
        engine.SnapshotPublished += OnSnapshot;
    }

    /// <summary>Snapshots are only read while the overlay is on screen; turning it on starts the trend afresh.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (!active) return;
        foreach (var (s, t) in _trends) { t.Load.Clear(); t.Temp.Clear(); s.LoadTrend = []; s.TempTrend = []; }
    }

    internal static IReadOnlyList<OverlaySection> Build(IReadOnlyList<HardwareNode> hardware)
    {
        static SensorDefinition? Pick(HardwareNode? node, params SensorRole[] roles) => node is null ? null : roles.Select(r => node.Sensors.FirstOrDefault(s => s.Role == r)).FirstOrDefault(s => s is not null);
        var sections = new List<OverlaySection>();
        void Row(OverlaySection s, string key, SensorDefinition? def) { if (def is not null) s.Rows.Add(new(Loc.Get(key), def.Unit, [def.Id])); }

        var top = hardware.Where(n => n.ParentId is null).ToList();
        var gpu = top.FirstOrDefault(n => n.Kind == HardwareKind.Gpu && n.Sensors.Any(s => s.Role == SensorRole.GpuCoreTemp)) ?? top.FirstOrDefault(n => n.Kind == HardwareKind.Gpu);
        if (gpu is not null)
        {
            var s = new OverlaySection("GPU", "Brush.Series.Gpu");
            var temp = Pick(gpu, SensorRole.GpuCoreTemp); var load = Pick(gpu, SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D);
            Row(s, "Overlay_Temp", temp); Row(s, "Overlay_Load", load); Row(s, "Overlay_Clock", Pick(gpu, SensorRole.GpuCoreClock)); Row(s, "Overlay_Power", Pick(gpu, SensorRole.GpuPower));
            Row(s, "Overlay_Voltage", Pick(gpu, SensorRole.GpuVoltage)); Row(s, "Overlay_HotSpot", Pick(gpu, SensorRole.GpuHotSpotTemp)); Row(s, "Overlay_Fan", Pick(gpu, SensorRole.GpuFanPercent));
            Row(s, "Overlay_FanRpm", Pick(gpu, SensorRole.GpuFanRpm)); Row(s, "Overlay_VramClock", Pick(gpu, SensorRole.GpuMemoryClock)); Row(s, "Overlay_VramTemp", Pick(gpu, SensorRole.GpuVramTemp));
            Row(s, "Overlay_Vram", Pick(gpu, SensorRole.GpuVramUsed));
            s.LoadSensor = load?.Id; s.TempSensor = temp?.Id;
            if (s.Rows.Count > 0) sections.Add(s);
        }
        var cpu = top.FirstOrDefault(n => n.Kind == HardwareKind.Cpu);
        if (cpu is not null)
        {
            var s = new OverlaySection("CPU", "Brush.Series.Cpu");
            var temp = Pick(cpu, SensorRole.CpuPackageTemp, SensorRole.CpuTctlTdie); var load = Pick(cpu, SensorRole.CpuTotalLoad);
            Row(s, "Overlay_Temp", temp); Row(s, "Overlay_Load", load); Row(s, "Overlay_Power", Pick(cpu, SensorRole.CpuPackagePower));
            Row(s, "Overlay_Clock", Pick(cpu, SensorRole.CpuEffectiveClockAverage, SensorRole.CpuCoreClockAverage, SensorRole.CpuCoreClock));
            s.LoadSensor = load?.Id; s.TempSensor = temp?.Id;
            if (s.Rows.Count > 0) sections.Add(s);
        }
        var ram = top.FirstOrDefault(n => n.Kind == HardwareKind.Memory && n.Sensors.Any(x => x.Role == SensorRole.RamUsed));
        if (ram is not null)
        {
            var s = new OverlaySection("RAM", "Brush.Series.Memory");
            Row(s, "Overlay_Used", Pick(ram, SensorRole.RamUsed)); Row(s, "Overlay_Load", Pick(ram, SensorRole.RamLoad));
            sections.Add(s);
        }
        // Traffic is the sum over the adapters; a Hyper-V virtual switch (vEthernet) forwards the physical adapter's traffic and would count it twice.
        var nets = top.Where(n => n.Kind == HardwareKind.Network && !NetworkAdapterFilter.IsVirtualBinding(n.Name) && !n.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase)).ToList();
        var down = nets.Select(n => Pick(n, SensorRole.NetDownload)).OfType<SensorDefinition>().Select(d => d.Id).ToList();
        var up = nets.Select(n => Pick(n, SensorRole.NetUpload)).OfType<SensorDefinition>().Select(d => d.Id).ToList();
        if (down.Count + up.Count > 0)
        {
            var s = new OverlaySection("NET", "Brush.Series.Network");
            if (down.Count > 0) s.Rows.Add(new(Loc.Get("Overlay_Down"), Unit.BytesPerSecond, down));
            if (up.Count > 0) s.Rows.Add(new(Loc.Get("Overlay_Up"), Unit.BytesPerSecond, up));
            sections.Add(s);
        }
        return sections;
    }

    private void OnSnapshot(SensorSnapshot snapshot) { if (_active) _dispatch(() => Apply(snapshot)); }

    internal void Apply(SensorSnapshot snapshot)
    {
        foreach (var r in snapshot.Readings) _latest[r.Id] = r.Quality == DataQuality.Ok ? r.Value : null;
        foreach (var section in Sections)
        {
            foreach (var row in section.Rows)
            {
                var values = row.Sensors.Select(id => _latest.GetValueOrDefault(id)).OfType<double>().ToList();
                row.Value = values.Count == 0 ? Missing : Format(values.Sum(), row.Unit);   // a sum only of adapters that did report
            }
            if (!section.HasTrend) continue;
            var (load, temp) = _trends[section];
            section.LoadTrend = Push(load, section.LoadSensor is { } l ? _latest.GetValueOrDefault(l) : null);
            section.TempTrend = Push(temp, section.TempSensor is { } t ? _latest.GetValueOrDefault(t) : null);
        }
    }

    private static double[] Push(Queue<double> queue, double? value)
    {
        queue.Enqueue(value ?? double.NaN);
        while (queue.Count > TrendLength) queue.Dequeue();
        return [.. queue];
    }

    /// <summary>Short forms for a glance: whole numbers except voltage; memory in GB once it passes a gigabyte.</summary>
    internal static string Format(double v, Unit unit)
    {
        string F(string format) => v.ToString(format, CultureInfo.InvariantCulture);
        return unit switch
        {
            Unit.Celsius => F("F0") + " °C", Unit.Percent => F("F0") + " %", Unit.MegaHertz => F("F0") + " MHz", Unit.Watt => F("F0") + " W", Unit.Volt => F("F3") + " V",
            Unit.Rpm => F("F0") + " RPM", Unit.Gigabyte => F("F1") + " GB",
            Unit.Megabyte => v >= 1024 ? (v / 1024).ToString("F1", CultureInfo.InvariantCulture) + " GB" : F("F0") + " MB",
            _ => Units.FormatWithSymbol(v, unit)
        };
    }

    public void Dispose() => _engine.SnapshotPublished -= OnSnapshot;
}
