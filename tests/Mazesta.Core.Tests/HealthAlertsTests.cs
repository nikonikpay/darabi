using Mazesta.Core.Health; using Xunit;
namespace Mazesta.Core.Tests;

public class HealthAlertsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Min = TimeSpan.FromMinutes(1);
    private static HealthAlerts Rules(int holdMinutes = 10, double cpu = 98, double gpu = 90, double hotSpot = 98) => new(TimeSpan.FromMinutes(holdMinutes), cpu, gpu, null, hotSpot);
    /// <summary>A processor at full load and this temperature.</summary>
    private static HealthSample Hot(double c, double load = 100) => new(c, null, load, null);

    [Fact] public void Overheat_fires_only_after_the_hold_at_full_load() { var h = Rules(10); Assert.Empty(h.Evaluate(Hot(99), T0)); Assert.Empty(h.Evaluate(Hot(99), T0 + 9 * Min)); Assert.Single(h.Evaluate(Hot(99), T0 + 10 * Min)); }
    [Fact] public void A_hot_chip_that_is_not_loaded_is_not_an_overheat() { var h = Rules(1); Assert.Empty(h.Evaluate(Hot(99, load: 40), T0)); Assert.Empty(h.Evaluate(Hot(99, load: 40), T0 + 5 * Min)); Assert.False(h.IsWatching); }
    [Fact] public void Without_a_load_sensor_the_rule_does_not_run() { var h = Rules(1); Assert.Empty(h.Evaluate(new(99, null, null, null), T0)); Assert.Empty(h.Evaluate(new(99, null, null, null), T0 + 5 * Min)); }
    [Fact] public void A_cool_sample_starts_the_hold_over() { var h = Rules(10); h.Evaluate(Hot(99), T0); h.Evaluate(Hot(80), T0 + 5 * Min); Assert.Empty(h.Evaluate(Hot(99), T0 + 6 * Min)); Assert.Empty(h.Evaluate(Hot(99), T0 + 15 * Min)); Assert.Single(h.Evaluate(Hot(99), T0 + 16 * Min)); }
    [Fact] public void It_repeats_only_after_fifteen_minutes() { var h = Rules(1); h.Evaluate(Hot(99), T0); Assert.Single(h.Evaluate(Hot(99), T0 + Min)); Assert.Empty(h.Evaluate(Hot(99), T0 + 15 * Min)); Assert.Single(h.Evaluate(Hot(99), T0 + 16 * Min)); }
    [Fact] public void A_missing_sensor_neither_fires_nor_resets() { var h = Rules(2); h.Evaluate(Hot(99), T0); h.Evaluate(new(null, null, null, null), T0 + Min); Assert.Single(h.Evaluate(Hot(99), T0 + 2 * Min)); }
    [Fact] public void A_gap_longer_than_the_hold_starts_over() { var h = Rules(10); h.Evaluate(Hot(99), T0); Assert.Empty(h.Evaluate(Hot(99), T0 + 60 * Min)); }
    [Fact] public void Watching_is_true_only_while_a_rule_is_counting() { var h = Rules(10); Assert.False(h.IsWatching); h.Evaluate(Hot(99), T0); Assert.True(h.IsWatching); h.Evaluate(Hot(70), T0 + Min); Assert.False(h.IsWatching); }
    [Fact] public void The_graphics_card_has_its_own_core_and_hot_spot_thresholds()
    {
        var h = Rules(1);
        h.Evaluate(new(null, 91, null, null, 80, 99), T0);
        var kinds = h.Evaluate(new(null, 91, null, null, 80, 99), T0 + Min).Select(a => a.Kind).ToList();
        Assert.Equal([HealthAlertKind.GpuOverheat], kinds);                         // core 91 > 90; the hot spot (80) is under 98
        var hot = Rules(1); hot.Evaluate(new(null, 70, null, null, 99, 99), T0);
        Assert.Equal([HealthAlertKind.GpuHotSpotOverheat], hot.Evaluate(new(null, 70, null, null, 99, 99), T0 + Min).Select(a => a.Kind).ToList());
    }
    [Fact] public void The_graphics_card_must_be_loaded_too() { var h = Rules(1); h.Evaluate(new(null, 99, null, null, 99, 20), T0); Assert.Empty(h.Evaluate(new(null, 99, null, null, 99, 20), T0 + 5 * Min)); }
    [Fact] public void Throttle_is_low_clock_at_full_load()
    {
        var h = Rules(10); Assert.Empty(h.Evaluate(new(null, null, 50, 1500), T0));
        h.Evaluate(new(null, null, 99, 1500), T0 + Min);
        var a = Assert.Single(h.Evaluate(new(null, null, 99, 1500), T0 + 4 * Min)); Assert.Equal(HealthAlertKind.CpuThrottle, a.Kind);
    }
    [Fact] public void The_users_threshold_decides_and_clears_five_degrees_below()
    {
        var rules = Rules(1, cpu: 80); var t = DateTimeOffset.UnixEpoch;
        Assert.Empty(rules.Evaluate(Hot(79), t));
        rules.Evaluate(Hot(81), t + Min); Assert.Single(rules.Evaluate(Hot(81), t + 2 * Min));
        Assert.Empty(rules.Evaluate(Hot(77), t + 3 * Min));   // below 80, not yet 5 under: not re-armed
        Assert.Empty(rules.Evaluate(Hot(81), t + 4 * Min));
        Assert.Empty(rules.Evaluate(Hot(74), t + 5 * Min));
        rules.Evaluate(Hot(81), t + 6 * Min); Assert.Single(rules.Evaluate(Hot(81), t + 7 * Min));
    }
}

public class MemoryPressureTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Min = TimeSpan.FromMinutes(1);
    [Fact] public void A_short_spike_is_not_announced() { var m = new MemoryPressure(90, 5 * Min); Assert.False(m.Evaluate(95, T0)); Assert.False(m.Evaluate(95, T0 + 4 * Min)); Assert.False(m.Evaluate(50, T0 + 5 * Min)); Assert.False(m.Evaluate(95, T0 + 6 * Min)); }
    [Fact] public void Staying_over_for_the_hold_announces_once_a_day() { var m = new MemoryPressure(90, 5 * Min); m.Evaluate(95, T0); Assert.True(m.Evaluate(95, T0 + 5 * Min)); Assert.False(m.Evaluate(95, T0 + 20 * Min)); m.Evaluate(40, T0 + 21 * Min); m.Evaluate(95, T0 + 22 * Min); Assert.False(m.Evaluate(95, T0 + 40 * Min)); }
    [Fact] public void The_next_day_it_can_announce_again() { var m = new MemoryPressure(90, Min) { LastFired = T0 }; m.Evaluate(95, T0.AddDays(2)); Assert.True(m.Evaluate(95, T0.AddDays(2) + 2 * Min)); }
    [Fact] public void A_reading_bouncing_just_under_the_line_does_not_reset_the_hold() { var m = new MemoryPressure(90, 5 * Min); m.Evaluate(91, T0); m.Evaluate(89, T0 + 2 * Min); Assert.True(m.Evaluate(91, T0 + 5 * Min)); }
    [Fact] public void A_missing_reading_changes_nothing() { var m = new MemoryPressure(90, Min); Assert.False(m.Evaluate(null, T0)); Assert.False(m.IsWatching); }
}
