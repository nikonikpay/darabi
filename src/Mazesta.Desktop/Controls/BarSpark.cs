using System.Windows; using System.Windows.Media;
namespace Mazesta.Desktop.Controls;

/// <summary>The overlay's frame-rate history as a row of rounded bars, newest on the right, scaled to the highest shown (so a steady rate reads as
/// an even row and a stutter as a dip). A missing reading is a gap. The newest bar is drawn at full strength, the rest a little softer. Drawn
/// only when a new list is set, once per monitor poll.</summary>
public sealed class BarSpark : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = Register(nameof(Values), typeof(IReadOnlyList<double>), null);
    public static readonly DependencyProperty FillProperty = Register(nameof(Fill), typeof(Brush), Brushes.MediumSeaGreen);
    public static readonly DependencyProperty CapacityProperty = Register(nameof(Capacity), typeof(int), 30);
    private static DependencyProperty Register(string name, Type type, object? value)
        => DependencyProperty.Register(name, type, typeof(BarSpark), new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    /// <summary>How many bars fill the width; the newest <see cref="Capacity"/> values are drawn.</summary>
    public int Capacity { get => (int)GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }

    public BarSpark() => FlowDirection = FlowDirection.LeftToRight;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight; int n = Math.Max(1, Capacity);
        if (w <= 0 || h <= 0) return;
        // At any width a bar keeps some thickness and the gap never takes more than half its slot (a first layout pass can be very narrow).
        double slot = w / n, gap = Math.Min(slot / 2, Math.Max(1.5, slot * 0.28)), bar = Math.Max(0.5, slot - gap), r = Math.Min(2.5, bar / 2);
        for (int i = 0; i < n; i++) dc.DrawRoundedRectangle(Track, null, new Rect(i * slot + gap / 2, h - 3, bar, 3), 1.5, 1.5);   // the empty slots, faint
        if (Values is not { Count: > 0 } values) return;
        var shown = values.Skip(Math.Max(0, values.Count - n)).ToList();
        double max = shown.Where(double.IsFinite).DefaultIfEmpty(0).Max();
        if (max <= 0) return;
        var soft = Fill.Clone(); soft.Opacity = 0.72; soft.Freeze();
        for (int i = 0; i < shown.Count; i++)
        {
            double v = shown[i]; if (!double.IsFinite(v)) continue;
            double bh = Math.Max(3, v / max * (h - 1)), x = w - (shown.Count - i) * slot + gap / 2;
            dc.DrawRoundedRectangle(i == shown.Count - 1 ? Fill : soft, null, new Rect(x, h - bh, bar, bh), r, r);
        }
    }

    private static readonly SolidColorBrush Track = Frozen(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
