using Mazesta.Core.Fans; using Xunit;
namespace Mazesta.Core.Tests;

public class FanCurveTests
{
    private static readonly FanPoint[] Line = [new(40, 30), new(60, 50), new(80, 100)];

    [Fact] public void Between_two_points_the_duty_is_on_the_line_between_them() => Assert.Equal(40, FanCurve.Evaluate(Line, 50), 6);
    [Fact] public void Before_the_first_and_after_the_last_point_it_stays_flat() { Assert.Equal(30, FanCurve.Evaluate(Line, 10)); Assert.Equal(100, FanCurve.Evaluate(Line, 95)); }
    [Fact] public void A_point_gives_exactly_its_duty() => Assert.Equal(50, FanCurve.Evaluate(Line, 60));
    [Fact] public void With_no_points_the_fan_runs_full() => Assert.Equal(100, FanCurve.Evaluate([], 50));

    [Fact] public void A_curve_is_put_in_order_clamped_and_never_dips()
    {
        var c = FanCurve.Clean([new(70, 20), new(40, 60), new(150, 200), new(40, 10)])!;
        Assert.Equal([new FanPoint(40, 10), new FanPoint(70, 20), new FanPoint(110, 100)], c);
        var dipping = FanCurve.Clean([new(40, 50), new(60, 30)])!; Assert.Equal(50, dipping[1].Percent);
    }
    [Fact] public void Too_few_or_too_many_points_are_no_curve()
    {
        Assert.Null(FanCurve.Clean([new(40, 30)]));
        Assert.Null(FanCurve.Clean(Enumerable.Range(0, 9).Select(i => new FanPoint(30 + i * 5, 30 + i))));
    }
    [Fact] public void Every_preset_is_a_valid_curve_and_unknown_names_have_none()
    {
        foreach (var n in new[] { "silent", "standard", "performance", "full" }) Assert.NotNull(FanCurve.Clean(FanCurve.Preset(n)!));
        Assert.Null(FanCurve.Preset("turbo"));
    }
}
