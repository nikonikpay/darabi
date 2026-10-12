using Mazesta.Core.Health; using Xunit;
namespace Mazesta.Core.Tests;

public class ThermalGuardTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Min = TimeSpan.FromMinutes(1);
    private static ThermalGuard Guard() => new(new(98, 90, 98, 100, 70, 10 * Min));
    private static ThermalSample Vram(double c) => new(null, null, null, null, c, null);

    [Fact] public void A_reading_at_its_ceiling_trips_at_once() { var t = Guard().Evaluate(Vram(110), T0); Assert.Equal(ThermalSensor.GpuVram, t!.Sensor); Assert.False(t.Sustained); }
    [Fact] public void A_reading_at_its_limit_trips_only_after_the_hold() { var g = Guard(); Assert.Null(g.Evaluate(Vram(100), T0)); Assert.Null(g.Evaluate(Vram(106), T0 + 9 * Min)); var t = g.Evaluate(Vram(100), T0 + 10 * Min); Assert.True(t!.Sustained); Assert.Equal(100, t.LimitC); }
    [Fact] public void Dropping_under_the_limit_starts_the_count_over() { var g = Guard(); g.Evaluate(Vram(100), T0); g.Evaluate(Vram(90), T0 + 5 * Min); Assert.Null(g.Evaluate(Vram(100), T0 + 10 * Min)); Assert.NotNull(g.Evaluate(Vram(100), T0 + 20 * Min)); }
    [Fact] public void A_missing_sensor_neither_trips_nor_resets() { var g = Guard(); g.Evaluate(Vram(100), T0); Assert.Null(g.Evaluate(new(null, null, null, null, null, null), T0 + 5 * Min)); Assert.NotNull(g.Evaluate(Vram(100), T0 + 10 * Min)); }
    [Fact] public void A_gap_longer_than_the_hold_starts_over() { var g = Guard(); g.Evaluate(Vram(100), T0); Assert.Null(g.Evaluate(Vram(100), T0 + 30 * Min)); }
    [Fact] public void Each_part_has_its_own_limit() { var g = Guard(); Assert.Null(g.Evaluate(new(97, 97, 89, 97, 99, 69), T0)); Assert.Equal(ThermalSensor.Drive, g.Evaluate(new(null, null, null, null, null, 85), T0)!.Sensor); }
}
