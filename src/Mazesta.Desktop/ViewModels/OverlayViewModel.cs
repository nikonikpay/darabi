using System.Collections.ObjectModel; using System.Globalization; using CommunityToolkit.Mvvm.ComponentModel;
using Mazesta.Core.Hardware; using Mazesta.Core.Overlay; using Mazesta.Desktop.Localization; using Mazesta.Monitoring;
namespace Mazesta.Desktop.ViewModels;

/// <summary>One line of the overlay: what it is, its value now, and (when its chart is on) its last minute as a line in its part's colour, drawn
/// right under its own label so a chart is never anonymous. The value is the not-available dash when there is no good reading.</summary>
public sealed partial class OverlayRow : ObservableObject
{
    internal OverlayRow(OverlayItem item, string label, Unit unit, IReadOnlyList<SensorId> sensors, bool chart, string hue)
    {
        Item = item; Label = label; Unit = unit; Sensors = sensors; HasChart = chart; Hue = hue; _trendMax = item.FixedMax ?? 1;
    }
    internal OverlayItem Item { get; }
    public string Id => Item.Id;
    public string Label { get; }
    internal Unit Unit { get; }
    /// <summary>Usually one sensor; Max and Sum items read several (every core, drive or adapter).</summary>
    internal IReadOnlyList<SensorId> Sensors { get; }
    public bool HasChart { get; }
    public string Hue { get; }
    internal readonly Queue<double> History = new();
    [ObservableProperty] private string _value = OverlayViewModel.Missing;
    [ObservableProperty] private double[] _trend = [];
    /// <summary>The chart's top: fixed for percentages and temperatures (0-100), otherwise a little above the highest point shown.</summary>
    [ObservableProperty] private double _trendMax;
    /// <summary>The chart's top written out ("max 71 °C"), so its scale can be read.</summary>
    [ObservableProperty] private string _trendCaption = "";
}

/// <summary>A block of the overlay (GAME, GPU, CPU, RAM, DISK, NET) in its part's colour. The game block names the program being measured.</summary>
public sealed partial class OverlaySection(OverlayPart part, string title, string hue) : ObservableObject
{
    public OverlayPart Part { get; } = part;
    public string Title { get; } = title;
    public string Hue { get; } = hue;
    public ObservableCollection<OverlayRow> Rows { get; } = [];
    [ObservableProperty] private string _subtitle = "";
}

/// <summary>
/// The always-on-top overlay: the items the technician chose (or a preset: game, render, troubleshooting), grouped by part in each part's colour,
/// each optionally with a labelled one-minute chart. It reads the monitor's own snapshots (no extra polling) and only while it is shown; the frame
/// rate comes from <see cref="IFrameRateSource"/>, which runs only while a frame item is shown. Every value is measured: an item the machine has
/// no sensor for is left out, one without a current reading shows a dash, and a frame rate that cannot be measured (Vulkan, OpenGL, nothing in
/// front) shows a dash too, never a guess.
/// </summary>
public sealed partial class OverlayViewModel : ObservableObject, IDisposable
{
    public const string Missing = "—";
    public const int TrendLength = 60;
    private static readonly Dictionary<OverlayPart, (string Title, string Hue)> Look = new()
    {
        // The same hues as the web edition's parts (css --c-*), so a part is one colour everywhere.
        [OverlayPart.Gaming] = ("GAME", "#FDD400"), [OverlayPart.Gpu] = ("GPU", "#B38BFF"), [OverlayPart.Cpu] = ("CPU", "#5BA8FF"),
        [OverlayPart.Memory] = ("RAM", "#35D0E0"), [OverlayPart.Storage] = ("DISK", "#FF9A4D"), [OverlayPart.Network] = ("NET", "#FF78B9"),
    };
    private readonly PollingEngine _engine; private readonly Func<Action, object> _dispatch; private readonly IFrameRateSource? _frames;
    private readonly Dictionary<SensorId, double?> _latest = [];
    private bool _active;

    public IReadOnlyList<OverlaySection> Sections { get; }
    public double Opacity { get; }
    public double Scale { get; }
    public bool NeedsFrames { get; }
    /// <summary>The last frame reading while a frame item is shown (the web page's preview shows it too).</summary>
    public FrameRateReading? Frames { get; private set; }
    public event Action? Updated;

    public OverlayViewModel(PollingEngine engine, Func<Action, object> dispatch, IReadOnlyList<OverlayChoice>? items = null, IFrameRateSource? frames = null, double opacity = 0.9, double scale = 1)
    {
        _engine = engine; _dispatch = dispatch; _frames = frames;
        Opacity = Math.Clamp(opacity, 0.5, 1); Scale = Math.Clamp(scale, 0.7, 1.5);
        Sections = Build(engine.Hardware, items ?? OverlayCatalog.Presets[OverlayCatalog.DefaultPreset]);
        NeedsFrames = Sections.Any(s => s.Part == OverlayPart.Gaming);
        engine.SnapshotPublished += OnSnapshot;
    }

    /// <summary>Snapshots are only read while the overlay is on screen; turning it on starts the charts afresh (and the frame tracing, if needed).</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (NeedsFrames && _frames is not null) { if (active) _frames.Start(); else _frames.Stop(); }
        if (!active) return;
        foreach (var r in Sections.SelectMany(s => s.Rows)) { r.History.Clear(); r.Trend = []; }
    }

    /// <summary>The chosen items this machine can show, grouped by part in the overlay's fixed part order, each part in the order chosen.</summary>
    internal static IReadOnlyList<OverlaySection> Build(IReadOnlyList<HardwareNode> hardware, IReadOnlyList<OverlayChoice> choices)
    {
        bool Include(HardwareNode n) => n.Kind != HardwareKind.Network || (!NetworkAdapterFilter.IsVirtualBinding(n.Name) && !n.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase));
        var sections = new List<OverlaySection>();
        foreach (var part in Enum.GetValues<OverlayPart>())
        {
            var (title, hue) = Look[part];
            var section = new OverlaySection(part, title, hue);
            foreach (var choice in choices)
            {
                if (OverlayCatalog.Find(choice.Id) is not { } item || item.Part != part || section.Rows.Any(r => r.Id == item.Id)) continue;
                if (item.IsFrameItem) { section.Rows.Add(new(item, Loc.Get(item.LabelKey), Unit.None, [], choice.Chart, hue)); continue; }
                var sensors = OverlayCatalog.Resolve(item, hardware, Include);
                if (sensors.Count > 0) section.Rows.Add(new(item, Loc.Get(item.LabelKey), sensors[0].Unit, [.. sensors.Select(s => s.Id)], choice.Chart, hue));
            }
            if (section.Rows.Count > 0) sections.Add(section);
        }
        return sections;
    }

    private void OnSnapshot(SensorSnapshot snapshot) { if (_active) _dispatch(() => Apply(snapshot)); }

    internal void Apply(SensorSnapshot snapshot)
    {
        foreach (var r in snapshot.Readings) _latest[r.Id] = r.Quality == DataQuality.Ok ? r.Value : null;
        Frames = NeedsFrames ? _frames?.Read() : null;
        foreach (var section in Sections)
        {
            if (section.Part == OverlayPart.Gaming) section.Subtitle = Frames?.App ?? "";
            foreach (var row in section.Rows)
            {
                double? v = row.Item.IsFrameItem ? FrameValue(row.Id, Frames) : OverlayCatalog.Combine(row.Item.Aggregate, row.Sensors.Select(id => _latest.GetValueOrDefault(id)));
                row.Value = v is { } x ? (row.Item.IsFrameItem ? FormatFrame(row.Id, x) : Format(x, row.Unit)) : Missing;
                if (row.HasChart) Chart(row, v);
            }
        }
        Updated?.Invoke();
    }

    private static double? FrameValue(string id, FrameRateReading? f) => f is null ? null : id switch { "fps" => f.Fps, "low1" => f.Low1Fps, "frametime" => f.FrameTimeMs, _ => null };

    private void Chart(OverlayRow row, double? value)
    {
        row.History.Enqueue(value ?? double.NaN);
        while (row.History.Count > TrendLength) row.History.Dequeue();
        double[] points = [.. row.History];
        if (row.Item.FixedMax is { } fixedMax) row.TrendMax = fixedMax;
        else { var seen = points.Where(double.IsFinite).DefaultIfEmpty(0).Max(); row.TrendMax = Math.Max(seen * 1.15, 1e-6); }
        row.TrendCaption = points.Any(double.IsFinite) ? Loc.Format("Overlay_ChartMax", row.Item.IsFrameItem ? FormatFrame(row.Id, points.Where(double.IsFinite).Max()) : Format(points.Where(double.IsFinite).Max(), row.Unit)) : "";
        row.Trend = points;
    }

    internal static string FormatFrame(string id, double v) => id == "frametime" ? v.ToString("F1", CultureInfo.InvariantCulture) + " ms" : v.ToString("F0", CultureInfo.InvariantCulture) + " FPS";

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

    public void Dispose() { _engine.SnapshotPublished -= OnSnapshot; if (_active && NeedsFrames) _frames?.Stop(); }
}
