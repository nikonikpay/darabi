using System.Windows; using System.Windows.Media;
namespace Mazesta.Desktop.Controls;

/// <summary>The overlay's frame-rate history as a trace on a scope screen: dotted divisions, the area under the line faintly filled, a dashed
/// <see cref="Level"/> (the 1 % low), and the newest point marked. Scaled from zero to a little above the highest shown, so a stutter reads as a
/// dip and a steady rate as a flat line, not as noise blown up to the full height. A missing reading (NaN) breaks the line. Drawn only when a
/// new list is set, once per monitor poll. The web page's preview draws the same thing (overlay.js, frameChart).</summary>
public sealed class FrameChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = Register(nameof(Values), typeof(IReadOnlyList<double>), null);
    public static readonly DependencyProperty LevelProperty = Register(nameof(Level), typeof(double), double.NaN);
    public static readonly DependencyProperty StrokeProperty = Register(nameof(Stroke), typeof(Brush), Brushes.Gold);
    public static readonly DependencyProperty CapacityProperty = Register(nameof(Capacity), typeof(int), 60);
    private static DependencyProperty Register(string name, Type type, object? value)
        => DependencyProperty.Register(name, type, typeof(FrameChart), new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    /// <summary>A reference drawn as a dashed line across (the 1 % low); NaN draws none.</summary>
    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    /// <summary>How many points fill the width: a short history starts at the right edge and grows leftwards.</summary>
    public int Capacity { get => (int)GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }

    private static readonly Brush Screen = Frozen(new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)));
    private static readonly Pen Division = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x21, 0xFF, 0xFF, 0xFF)), 1) { DashStyle = new DashStyle([1, 3], 0) });
    private static readonly Pen LevelPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)), 1) { DashStyle = new DashStyle([4, 3], 0) });
    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public FrameChart() => FlowDirection = FlowDirection.LeftToRight;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRoundedRectangle(Screen, null, new Rect(0, 0, w, h), 3, 3);
        for (int i = 1; i < 6; i++) { double x = Math.Round(w * i / 6) + 0.5; dc.DrawLine(Division, new Point(x, 0), new Point(x, h)); }
        for (int i = 1; i < 3; i++) { double y = Math.Round(h * i / 3) + 0.5; dc.DrawLine(Division, new Point(0, y), new Point(w, y)); }
        if (Values is not { Count: > 1 } values) return;
        var finite = values.Where(double.IsFinite).ToList();
        if (finite.Count < 2) return;
        double top = Math.Max(finite.Max(), double.IsFinite(Level) ? Level : 0) * 1.15;
        if (top <= 0) return;
        double step = w / Math.Max(1, Capacity - 1), x0 = w - (values.Count - 1) * step;
        double Y(double v) => h - 2 - v / top * (h - 4);

        var line = new StreamGeometry(); var area = new StreamGeometry();
        using (var g = line.Open())
        using (var a = area.Open())
        {
            bool open = false; double firstX = 0, lastX = 0;
            void Close() { if (open) { a.LineTo(new Point(lastX, h), false, false); a.LineTo(new Point(firstX, h), false, false); } open = false; }
            for (int i = 0; i < values.Count; i++)
            {
                double v = values[i];
                if (!double.IsFinite(v)) { Close(); continue; }
                var p = new Point(x0 + i * step, Y(v));
                if (!open) { g.BeginFigure(p, false, false); a.BeginFigure(p, true, true); firstX = p.X; open = true; }
                else { g.LineTo(p, true, true); a.LineTo(p, false, false); }
                lastX = p.X;
            }
            Close();
        }
        line.Freeze(); area.Freeze();
        var color = (Stroke as SolidColorBrush)?.Color ?? Colors.Gold;
        var fill = new LinearGradientBrush(Color.FromArgb(0x48, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90); fill.Freeze();
        dc.DrawGeometry(fill, null, area);
        if (double.IsFinite(Level)) { double y = Math.Round(Y(Level)) + 0.5; dc.DrawLine(LevelPen, new Point(0, y), new Point(w, y)); }
        dc.DrawGeometry(null, new Pen(Stroke, 1.75) { LineJoin = PenLineJoin.Round }, line);
        if (double.IsFinite(values[^1])) dc.DrawEllipse(Brushes.White, null, new Point(x0 + (values.Count - 1) * step - 2.5, Y(values[^1])), 2.5, 2.5);
    }
}
