using Mazesta.Desktop.Controls; using Mazesta.Monitoring; using Xunit;
namespace Mazesta.Desktop.Tests;
public class TimeSeriesChartTests
{
    [Fact] public void Nice_scale_pads_and_rounds() { var (mn, mx, step) = ChartScale.Nice(41.2, 58.9); Assert.True(mn <= 41.2 && mx >= 58.9); Assert.Equal(5, step); Assert.Equal(40, mn); Assert.Equal(60, mx); }
    [Fact] public void Flat_series_still_gets_a_visible_range() { var (mn, mx, _) = ChartScale.Nice(50, 50); Assert.True(mx > mn); }
    [Fact] public void Projection_keeps_window_and_marks_gaps()
    {
        var raw = new RawSeries([0, 2, 4, 6, 8], [1f, 2f, float.NaN, 4f, 5f]);
        var pts = ChartScale.Project(raw, windowSeconds: 6, nowSeconds: 8, maxGapSeconds: 1000, width: 100, height: 50, yMin: 0, yMax: 10);
        Assert.Equal(4, pts.Count);                                        // seconds 2..8 only
        Assert.True(pts[1].Gap); Assert.Equal(100, pts[^1].X, 3); Assert.Equal(25, pts[^1].Y, 3);   // y=5 of 0..10 → middle, top-left origin
    }
    [Fact] public void Projection_synthesizes_gap_for_time_discontinuity()
    {
        var raw = new RawSeries([0, 2, 4, 30, 32], [1f, 1f, 1f, 1f, 1f]);
        var pts = ChartScale.Project(raw, windowSeconds: 40, nowSeconds: 32, maxGapSeconds: 6, width: 100, height: 50, yMin: 0, yMax: 10);
        Assert.Equal(6, pts.Count);                                        // 5 real samples + 1 synthetic gap point
        Assert.False(pts[0].Gap); Assert.False(pts[1].Gap); Assert.False(pts[2].Gap);
        Assert.True(pts[3].Gap);                                           // the synthesized gap sits between sec=4 and sec=30
        Assert.False(pts[4].Gap); Assert.False(pts[5].Gap);
        Assert.Equal(pts[2].X, pts[3].X, 3);                               // gap point's X equals the X of the sample at sec=4
    }
    [Fact] public void Projection_has_no_gap_when_spacing_is_within_max()
    {
        var raw = new RawSeries([0, 2, 4, 30, 32], [1f, 1f, 1f, 1f, 1f]);
        var pts = ChartScale.Project(raw, windowSeconds: 40, nowSeconds: 32, maxGapSeconds: 30, width: 100, height: 50, yMin: 0, yMax: 10);
        Assert.Equal(5, pts.Count);
        Assert.DoesNotContain(pts, p => p.Gap);
    }
}
