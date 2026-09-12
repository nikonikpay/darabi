using System.Globalization; using System.Windows; using System.Windows.Media; using Mazesta.Monitoring;
namespace Mazesta.Desktop.Controls;
public readonly record struct ChartPoint(double X, double Y, bool Gap);
public static class ChartScale
{
    public static (double min, double max, double step) Nice(double dataMin, double dataMax, int targetTicks = 5)
    {
        if (double.IsNaN(dataMin) || double.IsNaN(dataMax)) return (0, 1, 0.2);
        if (dataMax - dataMin < 1e-9) { dataMin -= 1; dataMax += 1; }
        double range = dataMax - dataMin, rough = range / targetTicks, mag = Math.Pow(10, Math.Floor(Math.Log10(rough))), norm = rough / mag;
        double step = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10) * mag;
        return (Math.Floor(dataMin / step) * step, Math.Ceiling(dataMax / step) * step, step);
    }
    public static IReadOnlyList<ChartPoint> Project(RawSeries raw, int windowSeconds, int nowSeconds, double width, double height, double yMin, double yMax)
    {
        var pts = new List<ChartPoint>(); int start = nowSeconds - windowSeconds; double ySpan = Math.Max(yMax - yMin, 1e-9);
        for (int i = 0; i < raw.Seconds.Length; i++)
        {
            if (raw.Seconds[i] < start) continue;
            double x = (raw.Seconds[i] - start) / (double)windowSeconds * width; bool gap = float.IsNaN(raw.Values[i]);
            pts.Add(new ChartPoint(x, gap ? 0 : height - (raw.Values[i] - yMin) / ySpan * height, gap));
        }
        return pts;
    }
}
public sealed class TimeSeriesChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = Register(nameof(Series), typeof(RawSeries)), MinutesProperty = Register(nameof(Minutes), typeof(MinuteSeries)), UseMinutesProperty = Register(nameof(UseMinutes), typeof(bool)),
        WindowSecondsProperty = Register(nameof(WindowSeconds), typeof(int)), NowSecondsProperty = Register(nameof(NowSeconds), typeof(int)), SeriesBrushProperty = Register(nameof(SeriesBrush), typeof(Brush)), UnitSymbolProperty = Register(nameof(UnitSymbol), typeof(string));
    private static DependencyProperty Register(string n, Type t) => DependencyProperty.Register(n, t, typeof(TimeSeriesChart), new FrameworkPropertyMetadata(t.IsValueType ? Activator.CreateInstance(t) : null, FrameworkPropertyMetadataOptions.AffectsRender));
    public RawSeries Series { get => (RawSeries)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public MinuteSeries Minutes { get => (MinuteSeries)GetValue(MinutesProperty); set => SetValue(MinutesProperty, value); }
    public bool UseMinutes { get => (bool)GetValue(UseMinutesProperty); set => SetValue(UseMinutesProperty, value); }
    public int WindowSeconds { get => (int)GetValue(WindowSecondsProperty); set => SetValue(WindowSecondsProperty, value); }
    public int NowSeconds { get => (int)GetValue(NowSecondsProperty); set => SetValue(NowSecondsProperty, value); }
    public Brush SeriesBrush { get => (Brush)GetValue(SeriesBrushProperty); set => SetValue(SeriesBrushProperty, value); }
    public string UnitSymbol { get => (string)GetValue(UnitSymbolProperty) ?? ""; set => SetValue(UnitSymbolProperty, value); }
    private const double LeftAxis = 56, Bottom = 24, Top = 8, Right = 8;
    protected override void OnRender(DrawingContext dc)
    {
        double w = Math.Max(ActualWidth - LeftAxis - Right, 10), h = Math.Max(ActualHeight - Top - Bottom, 10);
        var axisPen = new Pen((Brush)FindResource("Brush.Border"), 1); var textBrush = (Brush)FindResource("Brush.TextMuted"); var font = new Typeface((FontFamily)FindResource("App.Font"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        dc.DrawRectangle((Brush)FindResource("Brush.Surface"), null, new Rect(0, 0, ActualWidth, ActualHeight));
        // data range
        var minutes = Minutes.Minute is null ? new MinuteSeries([], [], [], []) : Minutes; var series = Series.Seconds is null ? new RawSeries([], []) : Series;
        RawSeries raw = UseMinutes ? new RawSeries(minutes.Minute.Select(m => m * 60 + 30).ToArray(), minutes.Avg) : series;
        int start = NowSeconds - WindowSeconds; var visible = raw.Values.Where((v, i) => raw.Seconds[i] >= start && !float.IsNaN(v)).ToArray();
        var (yMin, yMax, step) = visible.Length == 0 ? (0, 1, 0.2) : ChartScale.Nice(visible.Min(), visible.Max());
        // grid + y labels
        for (double y = yMin; y <= yMax + 1e-9; y += step)
        {
            double py = Top + h - (y - yMin) / (yMax - yMin) * h; dc.DrawLine(axisPen, new Point(LeftAxis, py), new Point(LeftAxis + w, py));
            var ft = new FormattedText(y.ToString("0.#", CultureInfo.InvariantCulture) + " " + UnitSymbol, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 11, textBrush, 1.0); dc.DrawText(ft, new Point(LeftAxis - ft.Width - 4, py - ft.Height / 2));
        }
        // x labels (4 ticks, "-mm:ss" or "-hh:mm")
        for (int i = 0; i <= 4; i++)
        {
            double px = LeftAxis + w * i / 4; int secAgo = WindowSeconds - WindowSeconds * i / 4; string label = WindowSeconds >= 3600 ? $"-{secAgo / 3600}:{secAgo % 3600 / 60:00}" : $"-{secAgo / 60}:{secAgo % 60:00}";
            var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 11, textBrush, 1.0); dc.DrawText(ft, new Point(px - ft.Width / 2, Top + h + 4));
        }
        // min/max band for minute tier
        if (UseMinutes && minutes.Minute.Length > 0)
        {
            var band = new StreamGeometry(); using (var g = band.Open())
            {
                bool open = false; var backs = new List<Point>();
                for (int i = 0; i < minutes.Minute.Length; i++)
                {
                    int sec = minutes.Minute[i] * 60 + 30; if (sec < start || float.IsNaN(minutes.Max[i])) { if (open) { for (int b = backs.Count - 1; b >= 0; b--) g.LineTo(backs[b], true, false); backs.Clear(); open = false; } continue; }
                    double px = LeftAxis + (sec - start) / (double)WindowSeconds * w, pMax = Top + h - (minutes.Max[i] - yMin) / (yMax - yMin) * h, pMin = Top + h - (minutes.Min[i] - yMin) / (yMax - yMin) * h;
                    if (!open) { g.BeginFigure(new Point(px, pMax), true, true); open = true; } else g.LineTo(new Point(px, pMax), true, false); backs.Add(new Point(px, pMin));
                }
                if (open) for (int b = backs.Count - 1; b >= 0; b--) g.LineTo(backs[b], true, false);
            }
            var bandBrush = SeriesBrush.Clone(); bandBrush.Opacity = 0.18; dc.DrawGeometry(bandBrush, null, band);
        }
        // line with gaps
        var pts = ChartScale.Project(raw, WindowSeconds, NowSeconds, w, h, yMin, yMax); var pen = new Pen(SeriesBrush, 1.6) { LineJoin = PenLineJoin.Round };
        var geo = new StreamGeometry(); using (var g = geo.Open()) { bool pendown = false; foreach (var p in pts) { if (p.Gap) { pendown = false; continue; } var pt = new Point(LeftAxis + p.X, Top + p.Y); if (!pendown) { g.BeginFigure(pt, false, false); pendown = true; } else g.LineTo(pt, true, false); } }
        dc.DrawGeometry(null, pen, geo);
        // gap markers: hatched vertical strip between neighbours of a gap
        var gapBrush = (Brush)FindResource("Brush.StateMissing"); for (int i = 1; i < pts.Count; i++) if (pts[i].Gap && !pts[i - 1].Gap) { double x0 = LeftAxis + pts[i - 1].X; int j = i; while (j < pts.Count && pts[j].Gap) j++; double x1 = LeftAxis + (j < pts.Count ? pts[j].X : w); dc.DrawRectangle(new SolidColorBrush(((SolidColorBrush)gapBrush).Color) { Opacity = 0.25 }, null, new Rect(x0, Top, Math.Max(x1 - x0, 2), h)); }
    }
}
