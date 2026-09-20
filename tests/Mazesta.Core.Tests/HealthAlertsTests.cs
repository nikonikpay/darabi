using Mazesta.Core.Health; using Xunit;
namespace Mazesta.Core.Tests;

public class HealthAlertsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static HealthSample Hot(double c) => new(c, null, null, null);

    [Fact] public void Overheat_needs_the_required_consecutive_samples() { var h = new HealthAlerts(3); Assert.Empty(h.Evaluate(Hot(96), T0)); Assert.Empty(h.Evaluate(Hot(96), T0.AddSeconds(30))); Assert.Single(h.Evaluate(Hot(97), T0.AddSeconds(60))); }
    [Fact] public void A_cool_sample_resets_the_count() { var h = new HealthAlerts(2); h.Evaluate(Hot(96), T0); h.Evaluate(Hot(80), T0.AddSeconds(30)); Assert.Empty(h.Evaluate(Hot(96), T0.AddSeconds(60))); }
    [Fact] public void It_repeats_only_after_fifteen_minutes() { var h = new HealthAlerts(1); Assert.Single(h.Evaluate(Hot(96), T0)); Assert.Empty(h.Evaluate(Hot(96), T0.AddMinutes(14))); Assert.Single(h.Evaluate(Hot(96), T0.AddMinutes(15))); }
    [Fact] public void Recovering_below_ninety_re_arms_at_once_but_a_bounce_near_the_threshold_does_not() { var h = new HealthAlerts(1); h.Evaluate(Hot(96), T0); h.Evaluate(Hot(92), T0.AddMinutes(1)); Assert.Empty(h.Evaluate(Hot(96), T0.AddMinutes(2))); h.Evaluate(Hot(85), T0.AddMinutes(3)); Assert.Single(h.Evaluate(Hot(96), T0.AddMinutes(4))); }
    [Fact] public void A_missing_sensor_neither_fires_nor_resets() { var h = new HealthAlerts(2); h.Evaluate(Hot(96), T0); h.Evaluate(new(null, null, null, null), T0.AddSeconds(30)); Assert.Single(h.Evaluate(Hot(96), T0.AddSeconds(60))); }
    [Fact] public void Throttle_is_low_clock_at_full_load() { var h = new HealthAlerts(1); Assert.Empty(h.Evaluate(new(null, null, 50, 1500), T0)); var a = Assert.Single(h.Evaluate(new(null, null, 99, 1500), T0.AddSeconds(30))); Assert.Equal(HealthAlertKind.CpuThrottle, a.Kind); Assert.Equal(1500, a.Value); }
    [Fact] public void Watching_is_true_only_while_a_rule_is_counting() { var h = new HealthAlerts(3); Assert.False(h.IsWatching); h.Evaluate(Hot(96), T0); Assert.True(h.IsWatching); h.Evaluate(Hot(70), T0.AddSeconds(30)); Assert.False(h.IsWatching); }
    [Fact] public void Gpu_overheat_is_independent_of_the_cpu() { var h = new HealthAlerts(1); var a = Assert.Single(h.Evaluate(new(60, 97, null, null), T0)); Assert.Equal(HealthAlertKind.GpuOverheat, a.Kind); }
}
