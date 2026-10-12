using Mazesta.Core.Health; using Mazesta.Core.Overlay; using Mazesta.Core.Tray; using Xunit;
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

public class OverlayNamesTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 5070", "RTX 5070")]
    [InlineData("AMD Ryzen 9 9950X3D 16-Core Processor", "Ryzen 9 9950X3D")]
    [InlineData("Intel(R) Core(TM) i9-14900K", "Core i9-14900K")]
    [InlineData("13th Gen Intel(R) Core(TM) i7-13700H", "13th Gen Intel Core i7-13700H")]
    [InlineData("AMD Radeon RX 7900 XTX", "Radeon RX 7900 XTX")]
    [InlineData("Samsung SSD 990 PRO 4TB", "Samsung SSD 990 PRO 4TB")]
    [InlineData("Processor", "Processor")]
    public void A_makers_long_name_is_trimmed_and_never_emptied(string name, string expected) => Assert.Equal(expected, OverlayNames.Short(name));
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Alt+U", "Alt+U")] [InlineData("ctrl+shift+f9", "Ctrl+Shift+F9")] [InlineData("Win+Alt+PageUp", "Alt+Win+PageUp")] [InlineData(" Alt + 7 ", "Alt+7")]
    public void A_usable_shortcut_is_normalised(string text, string expected) => Assert.Equal(expected, Hotkey.Normalize(text));
    [Theory]
    [InlineData("")] [InlineData("U")] [InlineData("Shift+U")] [InlineData("Alt")] [InlineData("Alt+U+P")] [InlineData("Alt+Enter")] [InlineData("Alt+F25")]
    public void A_shortcut_without_alt_ctrl_or_win_or_without_one_known_key_is_refused(string text) => Assert.Null(Hotkey.TryParse(text));
    [Fact] public void The_overlay_keys_fall_back_to_the_defaults_when_one_is_bad_or_two_clash()
    {
        Assert.Equal(new OverlayHotkeys(), new OverlayHotkeys("Alt+O", "Alt+M", "x", "Alt+P").Valid());
        Assert.Equal(new OverlayHotkeys(), new OverlayHotkeys("Alt+O", "Alt+M", "Alt+O", "Alt+P").Valid());
        Assert.Equal("Ctrl+Alt+U", new OverlayHotkeys("Alt+O", "Alt+M", "alt+ctrl+u", "Alt+P").Valid().Layout);
    }
    [Fact] public void The_presets_are_walked_in_order_and_back_to_the_first()
    {
        string[] mine = ["zeta", "Alpha"];
        Assert.Equal("render", OverlayCatalog.NextPreset("game", mine, false)); Assert.Equal("user:Alpha", OverlayCatalog.NextPreset("troubleshoot", mine, false));
        Assert.Equal("custom", OverlayCatalog.NextPreset("user:zeta", mine, true)); Assert.Equal("game", OverlayCatalog.NextPreset("custom", mine, true));
        Assert.Equal("game", OverlayCatalog.NextPreset("user:gone", [], false)); Assert.Equal("game", OverlayCatalog.NextPreset("troubleshoot", [], false));
    }
}

public class SsdAlertTests
{
    [Fact] public void A_drive_over_its_limit_for_the_hold_warns_with_no_load_condition()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero); var h = new HealthAlerts(TimeSpan.FromMinutes(5), ssdAlertC: 70);
        Assert.Empty(h.Evaluate(new(null, null, null, null, SsdTempC: 75), t0));
        Assert.Equal(HealthAlertKind.SsdOverheat, Assert.Single(h.Evaluate(new(null, null, null, null, SsdTempC: 75), t0.AddMinutes(5))).Kind);
        Assert.Empty(new HealthAlerts(TimeSpan.FromMinutes(1), ssdAlertC: 70).Evaluate(new(null, null, null, null, SsdTempC: 65), t0));
    }
}
