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
    /// <summary>The value split for drawing: the number big and white, its unit small in the part's colour.</summary>
    [ObservableProperty] private string _number = OverlayViewModel.Missing;
    [ObservableProperty] private string _unitText = "";
    /// <summary>How full the row's bar is (value over its fixed top); a row without a fixed top, or charted, has no bar.</summary>
    [ObservableProperty] private double _fraction;
    public bool HasBar => Item.FixedMax is not null && !HasChart;
    [ObservableProperty] private double[] _trend = [];
    /// <summary>The chart's top: fixed for percentages and temperatures (0-100), otherwise a little above the highest point shown.</summary>
    [ObservableProperty] private double _trendMax;
    /// <summary>The chart's top written out ("max 71 °C"), so its scale can be read.</summary>
    [ObservableProperty] private string _trendCaption = "";
    /// <summary>The chart's top value alone ("71 °C"): the overlay draws it in its own left-to-right run next to the word, because WPF would
    /// reorder "71 °C" inside a right-to-left caption.</summary>
    [ObservableProperty] private string _trendMaxValue = "";
}

/// <summary>A block of the overlay (GAME, GPU, CPU, RAM, DISK, NET) in its part's colour. The game block names the program being measured; a
/// drive's own block names the drive.</summary>
public sealed partial class OverlaySection(OverlayPart part, string title, string hue, string? device = null) : ObservableObject
{
    public OverlayPart Part { get; } = part;
    public string? Device { get; } = device;
    public string Title { get; } = title;
    public string Hue { get; } = hue;
    /// <summary>The block's own box: its edge in the part's colour at a third, its ground washed with a tenth of it.</summary>
    public string HueEdge { get; } = "#59" + hue.TrimStart('#');
    public string HueTint { get; } = "#1A" + hue.TrimStart('#');
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
    private readonly PollingEngine _engine; private readonly Func<Action, object> _dispatch; private readonly IFrameRateSource? _frames; private readonly IPingSource? _ping;
    // Only the sensors the overlay shows are kept from each snapshot (a snapshot holds every sensor of the machine, often several hundred).
    private readonly Dictionary<SensorId, double?> _latest = [];
    private readonly HashSet<SensorId> _wanted;
    private bool _active;

    public IReadOnlyList<OverlaySection> Sections { get; }
    /// <summary>The part cards under the frame-rate card: every block but the game's.</summary>
    public IReadOnlyList<OverlaySection> Blocks { get; }
    /// <summary>The frame-rate block: the frame rate big, the 1 % low and frame time beside it, the last minute as a trace. Each is null when its item is
    /// not chosen, and the card is not drawn when none is.</summary>
    public OverlayRow? HeroFps { get; }
    public OverlayRow? HeroLow { get; }
    public OverlayRow? HeroFrameTime { get; }
    /// <summary>The session's average, lowest and highest frame rate (<see cref="FrameRateSession"/>), each shown when chosen.</summary>
    public OverlayRow? HeroAvg { get; }
    public OverlayRow? HeroMin { get; }
    public OverlayRow? HeroMax { get; }
    public bool HasHero => HeroFps is not null || HeroLow is not null || HeroFrameTime is not null || HasSessionStats;
    public bool HasSessionStats => HeroAvg is not null || HeroMin is not null || HeroMax is not null;
    private readonly FrameRateSession _session = new();
    /// <summary>The session's numbers for the web page's preview; null until the program in front has drawn.</summary>
    public double? SessionAverage => _session.Average;
    public double? SessionMin => _session.Min;
    public double? SessionMax => _session.Max;
    [ObservableProperty] private double[] _heroTrend = [];
    /// <summary>The 1 % low now, drawn as a level across the trace; NaN when it is not shown or not measured.</summary>
    [ObservableProperty] private double _heroLowValue = double.NaN;
    [ObservableProperty] private string _heroApp = "";
    private readonly Queue<double> _heroHistory = new();
    public double Opacity { get; }
    public double Scale { get; }
    public static readonly string[] Layouts = ["list", "columns", "line"];
    /// <summary>"list": one column of boxes; "columns": two boxes side by side; "line": everything in one row along the screen's edge, as a strip.</summary>
    public string Layout { get; }
    public bool TwoColumns => Layout == "columns";
    public bool IsLine => Layout == "line";
    public bool IsStacked => !IsLine;
    public double SectionWidth => TwoColumns ? 186 : 244;
    /// <summary>Each box keeps 4 px on every side and the list gives back 4 px at its edges: two boxes and the 8 px between them fill the panel.
    /// The strip has no fixed width: it is as long as what it shows.</summary>
    public double PanelWidth => IsLine ? double.NaN : TwoColumns ? SectionWidth * 2 + 8 : SectionWidth;
    public bool NeedsFrames { get; }
    /// <summary>A ping, loss or jitter item is shown: the echoes are sent only then, and only while the overlay is on screen.</summary>
    public bool NeedsPing { get; }
    /// <summary>The link as last measured while a ping item is shown (the web page's preview shows it too).</summary>
    public PingReading? Ping { get; private set; }
    /// <summary>The last frame reading while a frame item is shown (the web page's preview shows it too).</summary>
    public FrameRateReading? Frames { get; private set; }
    public event Action? Updated;

    public OverlayViewModel(PollingEngine engine, Func<Action, object> dispatch, IReadOnlyList<OverlayChoice>? items = null, IFrameRateSource? frames = null, double opacity = 0.9, double scale = 1, string layout = "list",
        IPingSource? ping = null)
    {
        _engine = engine; _dispatch = dispatch; _frames = frames; _ping = ping;
        Opacity = Math.Clamp(opacity, 0.5, 1); Scale = Math.Clamp(scale, 0.7, 1.5); Layout = Layouts.Contains(layout) ? layout : Layouts[0];
        Sections = Build(engine.Hardware, items ?? OverlayCatalog.Presets[OverlayCatalog.DefaultPreset]);
        _wanted = [.. Sections.SelectMany(s => s.Rows).SelectMany(r => r.Sensors)];
        NeedsFrames = Sections.Any(s => s.Part == OverlayPart.Gaming); NeedsPing = Sections.Any(s => s.Rows.Any(r => r.Item.IsPingItem));
        Blocks = [.. Sections.Where(s => s.Part != OverlayPart.Gaming)];
        var game = Sections.FirstOrDefault(s => s.Part == OverlayPart.Gaming)?.Rows ?? [];
        HeroFps = game.FirstOrDefault(r => r.Id == "fps"); HeroLow = game.FirstOrDefault(r => r.Id == "low1"); HeroFrameTime = game.FirstOrDefault(r => r.Id == "frametime");
        HeroAvg = game.FirstOrDefault(r => r.Id == "fps.avg"); HeroMin = game.FirstOrDefault(r => r.Id == "fps.min"); HeroMax = game.FirstOrDefault(r => r.Id == "fps.max");
        engine.SnapshotPublished += OnSnapshot;
    }

    /// <summary>Snapshots are only read while the overlay is on screen; turning it on starts the charts afresh (and the frame tracing, if needed).</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (NeedsFrames && _frames is not null) { if (active) _frames.Start(); else _frames.Stop(); }
        if (NeedsPing && _ping is not null) { if (active) _ping.Start(); else _ping.Stop(); }
        if (!active) return;
        foreach (var r in Sections.SelectMany(s => s.Rows)) { r.History.Clear(); r.Trend = []; }
        _heroHistory.Clear(); HeroTrend = []; HeroLowValue = double.NaN; _session.Reset();
    }

    /// <summary>The chosen items this machine can show, grouped into blocks (a part, or one drive's own block), the blocks in the order their first
    /// item was put and the items in their chosen order: the technician's order is the overlay's order.</summary>
    internal static IReadOnlyList<OverlaySection> Build(IReadOnlyList<HardwareNode> hardware, IReadOnlyList<OverlayChoice> choices)
    {
        bool Include(HardwareNode n) => n.Kind != HardwareKind.Network || (!NetworkAdapterFilter.IsVirtualBinding(n.Name) && !n.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase));
        var sections = new List<OverlaySection>();
        foreach (var choice in choices)
        {
            if (OverlayCatalog.Find(choice.Id) is not { } item || sections.Any(s => s.Rows.Any(r => r.Id == item.Id))) continue;
            IReadOnlyList<SensorDefinition> sensors = item.IsMeasured ? [] : OverlayCatalog.Resolve(item, hardware, Include);
            if (!item.IsMeasured && sensors.Count == 0) continue;
            var section = sections.FirstOrDefault(s => s.Part == item.Part && s.Device == item.Device);
            if (section is null)
            {
                var (title, hue) = Look[item.Part];
                section = new OverlaySection(item.Part, title, hue, item.Device);
                if (item.Device is not null) section.Subtitle = hardware.FirstOrDefault(n => n.Id.Value == item.Device)?.Name ?? "";
                sections.Add(section);
            }
            section.Rows.Add(new(item, Loc.Get(item.LabelKey), item.IsMeasured ? Unit.None : item.Of is not null ? Unit.Percent : sensors[0].Unit, [.. sensors.Select(s => s.Id)], choice.Chart, section.Hue));
        }
        return sections;
    }

    private void OnSnapshot(SensorSnapshot snapshot) { if (_active) _dispatch(() => Apply(snapshot)); }

    internal void Apply(SensorSnapshot snapshot)
    {
        foreach (var r in snapshot.Readings) if (_wanted.Contains(r.Id)) _latest[r.Id] = r.Quality == DataQuality.Ok ? r.Value : null;
        Frames = NeedsFrames ? _frames?.Read() : null;
        Ping = NeedsPing ? _ping?.Read() : null;
        _session.Add(Frames);
        foreach (var section in Sections)
        {
            if (section.Part == OverlayPart.Gaming) { section.Subtitle = Frames?.App ?? ""; HeroApp = section.Subtitle; }
            foreach (var row in section.Rows)
            {
                double? v = row.Item.IsFrameItem ? FrameValue(row.Id, Frames) : row.Item.IsPingItem ? PingValue(row.Id, Ping)
                    : row.Item.Of is not null ? Share(_latest.GetValueOrDefault(row.Sensors[0]), _latest.GetValueOrDefault(row.Sensors[1]))
                    : OverlayCatalog.Combine(row.Item.Aggregate, row.Sensors.Select(id => _latest.GetValueOrDefault(id)));
                row.Value = v is { } x ? Text(row, x) : Missing;
                int cut = row.Value.LastIndexOf(' ');
                (row.Number, row.UnitText) = cut > 0 ? (row.Value[..cut], row.Value[(cut + 1)..]) : (row.Value, "");
                row.Fraction = v is { } f && row.Item.FixedMax is { } top ? Math.Clamp(f / top, 0, 1) : 0;
                if (row.HasChart) Chart(row, v);
                if (row == HeroFps)
                {
                    // The frame-rate block always draws its last minute, charted or not: it is what the block is for.
                    _heroHistory.Enqueue(v ?? double.NaN); while (_heroHistory.Count > TrendLength) _heroHistory.Dequeue();
                    HeroTrend = [.. _heroHistory];
                }
                if (row == HeroLow) HeroLowValue = v ?? double.NaN;
            }
        }
        Updated?.Invoke();
    }

    private double? FrameValue(string id, FrameRateReading? f) => id switch
    {
        // The session's numbers stay while the game is in front but has not drawn for a moment; the live ones do not.
        "fps.avg" => _session.Average, "fps.min" => _session.Min, "fps.max" => _session.Max,
        _ => f is null ? null : id switch { "fps" => f.Fps, "low1" => f.Low1Fps, "frametime" => f.FrameTimeMs, _ => null },
    };

    private void Chart(OverlayRow row, double? value)
    {
        row.History.Enqueue(value ?? double.NaN);
        while (row.History.Count > TrendLength) row.History.Dequeue();
        double[] points = [.. row.History];
        double seen = double.NaN;
        foreach (double p in points) if (double.IsFinite(p) && !(p <= seen)) seen = p;
        row.TrendMax = row.Item.FixedMax ?? Math.Max((double.IsNaN(seen) ? 0 : seen) * 1.15, 1e-6);
        row.TrendMaxValue = double.IsNaN(seen) ? "" : Text(row, seen);
        row.TrendCaption = row.TrendMaxValue.Length > 0 ? Loc.Format("Overlay_ChartMax", row.TrendMaxValue) : "";
        row.Trend = points;
    }

    /// <summary>A lost echo has no time: the ping is then the dash, not a number.</summary>
    /// <summary>A part of a whole as a percentage; null when either was not read (never 0).</summary>
    internal static double? Share(double? part, double? whole) => part is { } p && whole is > 0 ? Math.Clamp(p / whole.Value * 100, 0, 100) : null;
    internal static double? PingValue(string id, PingReading? p) => id switch { "net.ping" => p?.PingMs, "net.loss" => p?.LossPercent, "net.jitter" => p?.JitterMs, _ => null };
    internal static string FormatPing(string id, double v) => id == "net.loss" ? v.ToString("F0", CultureInfo.InvariantCulture) + " %" : v.ToString(id == "net.jitter" ? "F1" : "F0", CultureInfo.InvariantCulture) + " ms";
    private static string Text(OverlayRow row, double v) => row.Item.IsFrameItem ? FormatFrame(row.Id, v) : row.Item.IsPingItem ? FormatPing(row.Id, v) : Format(v, row.Unit);

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

    public void Dispose() { _engine.SnapshotPublished -= OnSnapshot; if (_active && NeedsFrames) _frames?.Stop(); if (_active && NeedsPing) _ping?.Stop(); }
}
