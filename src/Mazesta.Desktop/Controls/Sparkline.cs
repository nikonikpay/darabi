using System.Windows; using System.Windows.Media;
namespace Mazesta.Desktop.Controls;

/// <summary>A tiny two-line trend on a fixed 0..<see cref="Maximum"/> scale (load in %, temperature in °C share it): the first series filled
/// below its line, the second a plain line. NaN is a gap. Drawn only when a new list is set, once per monitor poll.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = Register(nameof(Values), typeof(IReadOnlyList<double>), null);
    public static readonly DependencyProperty Values2Property = Register(nameof(Values2), typeof(IReadOnlyList<double>), null);
    public static readonly DependencyProperty StrokeProperty = Register(nameof(Stroke), typeof(Brush), Brushes.Gold);
    public static readonly DependencyProperty Stroke2Property = Register(nameof(Stroke2), typeof(Brush), Brushes.OrangeRed);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), typeof(double), 100.0);
    public static readonly DependencyProperty CapacityProperty = Register(nameof(Capacity), typeof(int), 60);
    private static DependencyProperty Register(string name, Type type, object? value)
        => DependencyProperty.Register(name, type, typeof(Sparkline), new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IReadOnlyList<double>? Values2 { get => (IReadOnlyList<double>?)GetValue(Values2Property); set => SetValue(Values2Property, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush Stroke2 { get => (Brush)GetValue(Stroke2Property); set => SetValue(Stroke2Property, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    /// <summary>How many points fill the width: a short history starts at the right edge and grows leftwards.</summary>
    public int Capacity { get => (int)GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }

    private static readonly Pen Grid = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1) { DashStyle = DashStyles.Dot });
    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public Sparkline() => FlowDirection = FlowDirection.LeftToRight;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawLine(Grid, new Point(0, h / 2), new Point(w, h / 2));
        Draw(dc, Values, Stroke, fill: true); Draw(dc, Values2, Stroke2, fill: false);
    }

    private void Draw(DrawingContext dc, IReadOnlyList<double>? values, Brush stroke, bool fill)
    {
        if (values is not { Count: > 1 }) return;
        double w = ActualWidth, h = ActualHeight, step = w / Math.Max(1, Capacity - 1), x0 = w - (values.Count - 1) * step;
        var line = new StreamGeometry(); var area = fill ? new StreamGeometry() : null;
        using (var g = line.Open())
        using (var a = area?.Open())
        {
            bool open = false; double firstX = 0, lastX = 0;
            for (int i = 0; i < values.Count; i++)
            {
                double v = values[i]; var p = new Point(x0 + i * step, h - Math.Clamp(v / Maximum, 0, 1) * h);
                if (double.IsNaN(v)) { if (open && a is not null) { a.LineTo(new Point(lastX, h), false, false); a.LineTo(new Point(firstX, h), false, false); } open = false; continue; }
                if (!open) { g.BeginFigure(p, false, false); a?.BeginFigure(p, true, true); firstX = p.X; open = true; }
                else { g.LineTo(p, true, true); a?.LineTo(p, false, false); }
                lastX = p.X;
            }
            if (open && a is not null) { a.LineTo(new Point(lastX, h), false, false); a.LineTo(new Point(firstX, h), false, false); }
        }
        line.Freeze();
        if (area is not null)
        {
            area.Freeze();
            var tint = stroke.Clone(); tint.Opacity = 0.18; tint.Freeze();
            dc.DrawGeometry(tint, null, area);
        }
        dc.DrawGeometry(null, new Pen(stroke, 1.5) { LineJoin = PenLineJoin.Round }, line);
    }
}
