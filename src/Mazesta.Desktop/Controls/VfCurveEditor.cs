using System.Globalization; using System.Windows; using System.Windows.Input; using System.Windows.Media;
using Mazesta.Core.Tuning;
namespace Mazesta.Desktop.Controls;

/// <summary>
/// The voltage/frequency curve editor: voltage across, clock up. The grey line is the card's stock curve as the
/// scan measured it; the yellow line is the curve the current settings give (every point raised by the core offset, flattened at the clock cap).
/// Dragging a yellow point up or down pins that voltage to a clock (offset + cap, see <see cref="VfCurve.PinPoint"/>) - the usual curve undervolt;
/// dragging the dashed cap line moves only the cap; Ctrl+drag moves the whole curve. NVML cannot move single points, so no drag does.
/// It only draws when something changed: a setting, the live reading (once a second while the page is shown), or the mouse while dragging.
/// </summary>
public sealed class VfCurveEditor : FrameworkElement
{
    public static readonly DependencyProperty StockProperty = Register(nameof(Stock), typeof(IReadOnlyList<VfPoint>), null);
    public static readonly DependencyProperty OffsetMHzProperty = DependencyProperty.Register(nameof(OffsetMHz), typeof(int), typeof(VfCurveEditor),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    /// <summary>The clock cap; 0 means none.</summary>
    public static readonly DependencyProperty CapMHzProperty = DependencyProperty.Register(nameof(CapMHz), typeof(int), typeof(VfCurveEditor),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty MinOffsetProperty = Register(nameof(MinOffset), typeof(int), -1000);
    public static readonly DependencyProperty MaxOffsetProperty = Register(nameof(MaxOffset), typeof(int), 1000);
    public static readonly DependencyProperty MaxCapProperty = Register(nameof(MaxCap), typeof(int), 3000);
    public static readonly DependencyProperty LiveClockMHzProperty = Register(nameof(LiveClockMHz), typeof(double), double.NaN);
    public static readonly DependencyProperty LiveVoltageVProperty = Register(nameof(LiveVoltageV), typeof(double), double.NaN);
    public static readonly DependencyProperty IsLockedProperty = Register(nameof(IsLocked), typeof(bool), false);
    public static readonly DependencyProperty EmptyTextProperty = Register(nameof(EmptyText), typeof(string), "");

    private static DependencyProperty Register(string name, Type type, object? value)
        => DependencyProperty.Register(name, type, typeof(VfCurveEditor), new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<VfPoint>? Stock { get => (IReadOnlyList<VfPoint>?)GetValue(StockProperty); set => SetValue(StockProperty, value); }
    public int OffsetMHz { get => (int)GetValue(OffsetMHzProperty); set => SetValue(OffsetMHzProperty, value); }
    public int CapMHz { get => (int)GetValue(CapMHzProperty); set => SetValue(CapMHzProperty, value); }
    public int MinOffset { get => (int)GetValue(MinOffsetProperty); set => SetValue(MinOffsetProperty, value); }
    public int MaxOffset { get => (int)GetValue(MaxOffsetProperty); set => SetValue(MaxOffsetProperty, value); }
    public int MaxCap { get => (int)GetValue(MaxCapProperty); set => SetValue(MaxCapProperty, value); }
    public double LiveClockMHz { get => (double)GetValue(LiveClockMHzProperty); set => SetValue(LiveClockMHzProperty, value); }
    public double LiveVoltageV { get => (double)GetValue(LiveVoltageVProperty); set => SetValue(LiveVoltageVProperty, value); }
    public bool IsLocked { get => (bool)GetValue(IsLockedProperty); set => SetValue(IsLockedProperty, value); }
    public string EmptyText { get => (string)GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }

    private const double PadLeft = 52, PadRight = 16, PadTop = 26, PadBottom = 30, HitRadius = 12, Bin = 15;
    private enum Drag { None, Point, Cap, Curve }
    private Drag _drag; private int _dragIndex; private double _dragStartY; private int _dragStartOffset; private int? _hover;
    // The axes are fixed for the length of a drag, so the point under the mouse does not slide away as the curve rescales.
    private (double V0, double V1, double C0, double C1)? _frozenAxes;

    public VfCurveEditor() { FlowDirection = FlowDirection.LeftToRight; Focusable = false; SnapsToDevicePixels = true; }

    private static readonly Pen GridPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)), 1));
    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    private Brush Res(string key) => (Brush)FindResource(key);

    private (double V0, double V1, double C0, double C1) Axes(IReadOnlyList<VfPoint> stock)
    {
        if (_frozenAxes is { } f) return f;
        double v0 = stock.Min(p => p.VoltageV), v1 = stock.Max(p => p.VoltageV);
        double c0 = stock.Min(p => p.ClockMHz) + Math.Min(0, OffsetMHz), c1 = Math.Max(stock.Max(p => p.ClockMHz) + Math.Max(0, OffsetMHz), CapMHz);
        if (!double.IsNaN(LiveClockMHz)) { c0 = Math.Min(c0, LiveClockMHz); c1 = Math.Max(c1, LiveClockMHz); }
        if (!double.IsNaN(LiveVoltageV)) { v0 = Math.Min(v0, LiveVoltageV); v1 = Math.Max(v1, LiveVoltageV); }
        return (Math.Floor((v0 - 0.02) / 0.05) * 0.05, Math.Ceiling((v1 + 0.02) / 0.05) * 0.05, Math.Floor((c0 - 60) / 100) * 100, Math.Ceiling((c1 + 60) / 100) * 100);
    }

    private Rect Plot => new(PadLeft, PadTop, Math.Max(10, ActualWidth - PadLeft - PadRight), Math.Max(10, ActualHeight - PadTop - PadBottom));
    private Point ToScreen(VfPoint p, (double V0, double V1, double C0, double C1) a)
    {
        var r = Plot;
        return new(r.Left + (p.VoltageV - a.V0) / (a.V1 - a.V0) * r.Width, r.Bottom - (p.ClockMHz - a.C0) / (a.C1 - a.C0) * r.Height);
    }
    private double ClockAtY(double y, (double V0, double V1, double C0, double C1) a) { var r = Plot; return a.C0 + (r.Bottom - y) / r.Height * (a.C1 - a.C0); }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));   // the whole area takes the mouse
        var stock = Stock; double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (stock is not { Count: >= 2 }) { Label(dc, EmptyText, new Point(ActualWidth / 2, ActualHeight / 2), Res("Brush.TextMuted"), 13, dip, center: true, persian: true); return; }
        var a = Axes(stock); var r = Plot; var muted = Res("Brush.TextMuted"); var accent = Res("Brush.Accent");

        for (double c = a.C0; c <= a.C1 + 0.1; c += a.C1 - a.C0 > 1200 ? 200 : 100)
        {
            double y = ToScreen(new(c, a.V0), a).Y; dc.DrawLine(GridPen, new(r.Left, y), new(r.Right, y));
            Label(dc, c.ToString("F0", CultureInfo.InvariantCulture), new Point(r.Left - 8, y), muted, 11, dip, alignRight: true);
        }
        for (double v = a.V0; v <= a.V1 + 1e-9; v += 0.05)
        {
            double x = ToScreen(new(a.C0, v), a).X; dc.DrawLine(GridPen, new(x, r.Top), new(x, r.Bottom));
            Label(dc, v.ToString("F2", CultureInfo.InvariantCulture) + (v + 0.05 > a.V1 + 1e-9 ? " V" : ""), new Point(x, r.Bottom + 14), muted, 11, dip, center: true);
        }
        Label(dc, "MHz", new Point(r.Left - 8, 4), muted, 10.5, dip, alignRight: true);

        var (ordered, shaped) = Curves(stock); int? cap = CapMHz > 0 ? CapMHz : null;
        Polyline(dc, ordered.Select(p => ToScreen(p, a)), new Pen(Res("Brush.StateMissing"), 1.6) { DashStyle = DashStyles.Dash });
        Polyline(dc, shaped.Select(p => ToScreen(p, a)), new Pen(accent, 2.4) { LineJoin = PenLineJoin.Round });
        var bg = Res("Brush.Background");
        for (int i = 0; i < shaped.Count; i++)
        {
            var s = ToScreen(shaped[i], a); bool hot = _hover == i || (_drag == Drag.Point && _dragIndex == i);
            dc.DrawEllipse(hot ? accent : bg, new Pen(accent, 2), s, hot ? 6 : 4, hot ? 6 : 4);
        }

        if (cap is { } c2)
        {
            double y = ToScreen(new(c2, a.V0), a).Y;
            dc.DrawLine(new Pen(accent, 1.2) { DashStyle = DashStyles.Dot }, new(r.Left, y), new(r.Right, y));
            var tag = new Rect(r.Left + 6, y - 24, 96, 20);
            dc.DrawRoundedRectangle(accent, null, tag, 6, 6);
            Label(dc, $"CAP {c2} MHz", new Point(tag.Left + tag.Width / 2, tag.Top + tag.Height / 2), Res("Brush.OnAccent"), 11, dip, center: true, bold: true);
        }

        if (!double.IsNaN(LiveClockMHz) && !double.IsNaN(LiveVoltageV))
        {
            var live = ToScreen(new(LiveClockMHz, LiveVoltageV), a); var green = Res("Brush.Success");
            dc.DrawEllipse(null, new Pen(green, 1.5), live, 9, 9); dc.DrawEllipse(green, null, live, 4, 4);
        }

        if ((_drag == Drag.Point ? _dragIndex : _hover) is int h && h >= 0 && h < shaped.Count)
        {
            var p = shaped[h]; var s = ToScreen(p, a); string text = $"{p.VoltageV:F3} V  ·  {p.ClockMHz:F0} MHz";
            var box = new Rect(Math.Clamp(s.X - 80, r.Left, r.Right - 160), Math.Max(r.Top, s.Y - 34), 160, 22);
            dc.DrawRoundedRectangle(Res("Brush.SurfaceAlt"), new Pen(Res("Brush.Border"), 1), box, 6, 6);
            Label(dc, text, new Point(box.Left + box.Width / 2, box.Top + box.Height / 2), Res("Brush.Text"), 11.5, dip, center: true);
        }
    }

    private static void Polyline(DrawingContext dc, IEnumerable<Point> points, Pen pen)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            bool first = true;
            foreach (var p in points) { if (first) { g.BeginFigure(p, false, false); first = false; } else g.LineTo(p, true, true); }
        }
        geometry.Freeze(); dc.DrawGeometry(null, pen, geometry);
    }

    private void Label(DrawingContext dc, string text, Point at, Brush brush, double size, double dip, bool center = false, bool alignRight = false, bool bold = false, bool persian = false)
    {
        var family = (FontFamily)FindResource(persian ? "App.Font" : "App.Font.Latin");
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, persian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), size, brush, dip);
        double x = center ? at.X - ft.Width / 2 : alignRight ? at.X - ft.Width : at.X;
        if (persian) x += ft.Width;   // a right-to-left FormattedText is drawn leftwards from its origin
        dc.DrawText(ft, new Point(x, at.Y - (center || alignRight ? ft.Height / 2 : 0)));
    }

    // Interaction

    /// <summary>The stock points in drawing order (by voltage) and, at the same index, where the current settings put each.</summary>
    private (List<VfPoint> Stock, IReadOnlyList<VfPoint> Shaped) Curves(IReadOnlyList<VfPoint> stock)
    {
        List<VfPoint> ordered = [.. stock.OrderBy(p => p.VoltageV).ThenBy(p => p.ClockMHz)];
        return (ordered, VfCurve.Shape(ordered, OffsetMHz, CapMHz > 0 ? CapMHz : null));
    }

    private int? Nearest(Point mouse)
    {
        if (Stock is not { Count: >= 2 } stock) return null;
        var a = Axes(stock); var shaped = Curves(stock).Shaped;
        int? best = null; double bestD = HitRadius;
        for (int i = 0; i < shaped.Count; i++) { double d = (ToScreen(shaped[i], a) - mouse).Length; if (d <= bestD) { bestD = d; best = i; } }
        return best;
    }

    private bool NearCap(Point mouse) => CapMHz > 0 && Stock is { Count: >= 2 } stock && Math.Abs(ToScreen(new(CapMHz, 0), Axes(stock)).Y - mouse.Y) <= 6 && Plot.Contains(mouse);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (IsLocked || Stock is not { Count: >= 2 } stock) return;
        var at = e.GetPosition(this); _frozenAxes = Axes(stock);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Plot.Contains(at)) _drag = Drag.Curve;
        else if (Nearest(at) is { } i) { _drag = Drag.Point; _dragIndex = i; }
        else if (NearCap(at)) _drag = Drag.Cap;
        else { _frozenAxes = null; return; }
        _dragStartY = at.Y; _dragStartOffset = OffsetMHz; CaptureMouse(); e.Handled = true; InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var at = e.GetPosition(this);
        if (_drag == Drag.None)
        {
            var hover = IsLocked ? null : Nearest(at);
            Cursor = hover is not null || (!IsLocked && NearCap(at)) ? Cursors.SizeNS : null;
            if (hover != _hover) { _hover = hover; InvalidateVisual(); }
            return;
        }
        if (Stock is not { Count: >= 2 } stock || _frozenAxes is not { } a) return;
        int clock = (int)(Math.Round(ClockAtY(at.Y, a) / Bin) * Bin);
        switch (_drag)
        {
            case Drag.Cap: CapMHz = Math.Clamp(clock, GpuTuningLimits.MinLockMHz, MaxCap); break;
            case Drag.Curve:
                int delta = (int)(Math.Round((ClockAtY(at.Y, a) - ClockAtY(_dragStartY, a)) / Bin) * Bin);
                OffsetMHz = Math.Clamp(_dragStartOffset + delta, MinOffset, MaxOffset); break;
            case Drag.Point when VfCurve.PinPoint(stock, Curves(stock).Stock[_dragIndex].VoltageV, Math.Clamp(clock, GpuTuningLimits.MinLockMHz, MaxCap)) is { } pin:
                OffsetMHz = Math.Clamp(pin.OffsetMHz, MinOffset, MaxOffset); CapMHz = pin.CapMHz; break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => EndDrag();
    protected override void OnLostMouseCapture(MouseEventArgs e) => EndDrag();
    protected override void OnMouseLeave(MouseEventArgs e) { if (_hover is not null && _drag == Drag.None) { _hover = null; InvalidateVisual(); } }

    private void EndDrag()
    {
        if (_drag == Drag.None) return;
        _drag = Drag.None; _frozenAxes = null; if (IsMouseCaptured) ReleaseMouseCapture(); InvalidateVisual();
    }
}
