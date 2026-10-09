using Mazesta.Core.Tuning;
using Xunit;
namespace Mazesta.Core.Tests;

public class AutoTuneTests
{
    private static readonly GpuTuningLimits Limits = new(-1000, 1000, -2000, 6000, 2100, 100, 365, 350, 2, 30, 100);
    private static readonly AutoTuneOptions Options = new();

    private static LoadMeasurement M(double clock, double power, double temp = 70, double throughput = 100, long errors = 0, bool lost = false)
        => new(clock, power, temp, temp + 3, throughput, errors, lost);

    /// <summary>Drives a search to its end, answering each step with <paramref name="card"/>; returns the outcome and every step asked for.</summary>
    private static (AutoTuneOutcome Outcome, List<TuneStep> Steps) Drive(IAutoTuneSearch search, Func<TuneStep, LoadMeasurement> card)
    {
        var steps = new List<TuneStep>();
        while (search.Next() is { } step) { steps.Add(step); search.Report(card(step)); Assert.True(steps.Count < 200, "search never ends"); }
        return (search.Outcome!, steps);
    }

    /// <summary>A card stable up to +<paramref name="stableTo"/> MHz of offset, whose power falls by 0.2 W per MHz of offset at the same clock.</summary>
    private static Func<TuneStep, LoadMeasurement> Undervoltable(int stableTo) => step => step.Kind == TuneStepKind.Baseline
        ? M(1905, 340)
        : M(step.Settings.MaxClockMHz!.Value, 340 - 0.2 * step.Settings.CoreOffsetMHz, 70 - step.Settings.CoreOffsetMHz / 30.0, errors: step.Settings.CoreOffsetMHz > stableTo ? 3 : 0);

    [Fact] public void Measurement_uses_the_median_clock_and_leaves_missing_readings_null()
    {
        var t = DateTimeOffset.UnixEpoch;
        var m = LoadMeasurement.From([new(t, 1800, null, 60, null, null), new(t, 1900, null, 62, null, null), new(t, 2000, null, 70, null, null), new(t, 1950, null, null, null, null)], 10, 0, false);
        Assert.Equal(1925, m.MedianClockMHz); Assert.Null(m.AveragePowerW); Assert.Equal(64, m.AverageTemperatureC); Assert.Equal(70, m.MaxTemperatureC);
    }

    [Fact] public void Undervolt_keeps_the_stock_clock_and_backs_off_from_the_last_stable_offset()
    {
        var (outcome, steps) = Drive(new UndervoltSearch(Limits, Options), Undervoltable(stableTo: 150));
        Assert.Equal(AutoTuneVerdict.Improved, outcome.Verdict);
        Assert.Equal(new GpuTuningSettings(135, 0, 1905, null, null), outcome.Settings);   // last stable +150, minus the 15 MHz margin
        Assert.All(steps.Skip(2), s => Assert.Equal(1905, s.Settings.MaxClockMHz));   // (the first two are stock: the stress test and the scene)
        Assert.Equal(TuneStepKind.Confirm, steps[^1].Kind);
        Assert.True(outcome.Tuned!.AveragePowerW < outcome.Baseline!.AveragePowerW);
    }

    [Fact] public void Undervolt_never_tries_past_the_search_ceiling()
    {
        var (outcome, steps) = Drive(new UndervoltSearch(Limits, Options), Undervoltable(stableTo: 5000));
        Assert.All(steps, s => Assert.True(s.Settings.CoreOffsetMHz <= Options.MaxCoreOffset));
        Assert.Equal(Options.MaxCoreOffset - Options.SafetyMarginMHz, outcome.Settings!.CoreOffsetMHz);
    }

    [Fact] public void A_card_that_fails_at_stock_is_not_tuned()
    {
        var (outcome, steps) = Drive(new UndervoltSearch(Limits, Options), _ => M(1905, 340, errors: 1));
        Assert.Equal(AutoTuneVerdict.Failed, outcome.Verdict); Assert.Equal("Tuning_Out_StockUnstable", outcome.ReasonKey); Assert.Single(steps);
    }

    [Fact] public void An_undervolt_that_saves_nothing_measurable_is_not_offered()
    {
        var (outcome, _) = Drive(new UndervoltSearch(Limits, Options), step => M(step.Settings.MaxClockMHz ?? 1905, 340));
        Assert.Equal(AutoTuneVerdict.NoImprovement, outcome.Verdict); Assert.Equal("Tuning_Out_NoSaving", outcome.ReasonKey); Assert.Null(outcome.Settings);
    }

    [Fact] public void An_offset_that_loses_the_clock_counts_as_unstable()
    {
        // From +90 up, the card no longer reaches its stock clock: that is not "the same frequency", whatever the power says.
        var (outcome, _) = Drive(new UndervoltSearch(Limits, Options), step => step.Kind == TuneStepKind.Baseline ? M(1905, 340)
            : M(step.Settings.CoreOffsetMHz >= 90 ? 1800 : 1905, 300));
        Assert.Equal(45, outcome.Settings!.CoreOffsetMHz);
    }

    [Fact] public void A_failed_confirmation_steps_down_and_gives_up_after_three_tries()
    {
        var (outcome, steps) = Drive(new UndervoltSearch(Limits, Options), step => step.Kind switch
        {
            TuneStepKind.Baseline => M(1905, 340), TuneStepKind.Probe => M(1905, 300), _ => M(1905, 300, lost: true)
        });
        Assert.Equal(AutoTuneVerdict.NoImprovement, outcome.Verdict); Assert.Equal("Tuning_Out_ConfirmFailed", outcome.ReasonKey);
        var confirms = steps.Where(s => s.Kind == TuneStepKind.Confirm).Select(s => s.Settings.CoreOffsetMHz).ToList();
        Assert.Equal([285, 255, 225], confirms);
    }

    [Fact] public void Undervolt_keeps_the_factory_top_clock_not_the_one_held_at_the_power_limit()
    {
        // Holds 1665 under the heavy load, reaches 1965 lightly. The cap is the top clock; offsets only count if the held clock is kept and the top is reached.
        var (outcome, steps) = Drive(new UndervoltSearch(Limits, Options), step => step.Load == GpuLoadKind.Scene ? M(1965, 200) : step.Kind == TuneStepKind.Baseline ? M(1665, 350) with { PeakClockMHz = 1965 }
            : M(1665, 330 - 0.1 * step.Settings.CoreOffsetMHz, errors: step.Settings.CoreOffsetMHz > 150 ? 2 : 0));
        Assert.All(steps.Skip(2), s => Assert.Equal(1965, s.Settings.MaxClockMHz));
        Assert.Equal(AutoTuneVerdict.Improved, outcome.Verdict); Assert.Equal(1965, outcome.Settings!.MaxClockMHz);
        Assert.Equal(GpuLoadKind.Scene, steps[^1].Load);   // the top of the curve is tested with the load that gets there
    }

    [Fact] public void An_undervolt_that_cannot_reach_the_top_clock_lightly_is_stepped_down()
    {
        var (outcome, _) = Drive(new UndervoltSearch(Limits, Options), step => step.Load == GpuLoadKind.Scene ? M(step.Kind != TuneStepKind.Baseline && step.Settings.CoreOffsetMHz > 100 ? 1800 : 1965, 200) : step.Kind == TuneStepKind.Baseline ? M(1665, 350) with { PeakClockMHz = 1965 }
            : M(1665, 320, errors: step.Settings.CoreOffsetMHz > 150 ? 2 : 0));
        Assert.Equal(AutoTuneVerdict.Improved, outcome.Verdict); Assert.True(outcome.Settings!.CoreOffsetMHz <= 100);
    }

    [Fact] public void An_undervolt_that_runs_faster_at_the_same_power_limit_counts()
    {
        var (outcome, _) = Drive(new UndervoltSearch(Limits, Options), step => step.Kind == TuneStepKind.Baseline ? M(1665, 350) : M(1665, 350, throughput: 105));
        Assert.Equal(AutoTuneVerdict.Improved, outcome.Verdict);
    }

    [Fact] public void No_core_offset_control_means_unsupported()
    {
        var (outcome, _) = Drive(new UndervoltSearch(Limits with { CoreOffsetMin = 0, CoreOffsetMax = 0 }, Options), _ => M(1905, 340));
        Assert.Equal(AutoTuneVerdict.Unsupported, outcome.Verdict);
    }

    /// <summary>Clock rises 30 MHz per step until <paramref name="stableTo"/>; throughput follows the clock; power stays under stock.</summary>
    private static Func<TuneStep, LoadMeasurement> Overclockable(int stableTo, int memoryStableTo) => step =>
    {
        int clock = step.Settings.MaxClockMHz ?? 1905;
        if (step.Load == GpuLoadKind.Memory) return M(clock, 300, throughput: 800 + step.Settings.MemoryOffsetMHz * 0.1, errors: step.Settings.MemoryOffsetMHz > memoryStableTo ? 1 : 0);
        return M(clock, step.Kind == TuneStepKind.Baseline ? 340 : 320, throughput: clock / 10.0, errors: clock > stableTo ? 1 : 0);
    };

    [Fact] public void Overclock_raises_the_clock_from_the_undervolt_then_the_memory_and_beats_stock()
    {
        var start = new GpuTuningSettings(135, 0, 1905, null, null);
        var (outcome, steps) = Drive(new OverclockSearch(Limits, Options, start, M(1905, 340, throughput: 190.5)), Overclockable(stableTo: 2025, memoryStableTo: 800));
        Assert.Equal(AutoTuneVerdict.Improved, outcome.Verdict);
        Assert.Equal(135, outcome.Settings!.CoreOffsetMHz);                      // the undervolt's offset is kept
        Assert.Equal(2025 - Options.SafetyMarginMHz, outcome.Settings.MaxClockMHz);
        Assert.Equal(600, outcome.Settings.MemoryOffsetMHz);                     // +800 was the last gain, backed off one step
        Assert.DoesNotContain(steps, s => s.Kind == TuneStepKind.Baseline && s.Load == GpuLoadKind.Compute);   // stock was already measured
    }

    [Fact] public void Overclock_stops_raising_when_the_card_no_longer_runs_faster()
    {
        // A power-limited card: the cap goes up but the clock stays at 1950.
        var (outcome, steps) = Drive(new OverclockSearch(Limits with { MemoryOffsetMax = 0, MemoryOffsetMin = 0 }, Options, new(90, 0, 1905, null, null), M(1905, 340, throughput: 100)),
            step => M(Math.Min(step.Settings.MaxClockMHz ?? 1905, 1950), 330, throughput: 103));
        Assert.Equal(1965, outcome.Settings!.MaxClockMHz);                       // the lowest cap that reached 1950; 1995 gained nothing
        Assert.Equal(3, steps.Count(s => s.Kind == TuneStepKind.Probe));
    }

    [Fact] public void Overclock_that_needs_more_power_than_stock_is_refused()
    {
        var (outcome, _) = Drive(new OverclockSearch(Limits with { MemoryOffsetMax = 0, MemoryOffsetMin = 0 }, Options, new(90, 0, 1905, null, null), M(1905, 300, throughput: 100)),
            step => M(step.Settings.MaxClockMHz ?? 1905, 400, throughput: 110));
        Assert.Equal(AutoTuneVerdict.NoImprovement, outcome.Verdict);
    }

    [Fact] public void Overclock_plus_raises_the_power_limit_and_may_draw_more_than_stock()
    {
        var start = new GpuTuningSettings(0, 0, 1905, null, null);
        var (outcome, steps) = Drive(new OverclockSearch(Limits, AutoTuneOptions.Plus(), start, M(1905, 300, throughput: 190.5), plus: true), step => step.Load == GpuLoadKind.Memory
            ? M(step.Settings.MaxClockMHz ?? 1905, 340, throughput: 800 + step.Settings.MemoryOffsetMHz * 0.1, errors: step.Settings.MemoryOffsetMHz > 1000 ? 1 : 0)
            : M(step.Settings.MaxClockMHz!.Value, 350, throughput: step.Settings.MaxClockMHz!.Value / 10.0, errors: step.Settings.MaxClockMHz > 2100 ? 1 : 0));   // 350 W: above stock's 300, under the 365 limit
        Assert.Equal(AutoTuneVerdict.Improved, outcome.Verdict);
        Assert.All(steps.Where(s => s.Kind != TuneStepKind.Baseline), s => Assert.Equal(365, s.Settings.PowerLimitW));
        Assert.Equal(365, outcome.Settings!.PowerLimitW);
        Assert.True(outcome.Settings.MaxClockMHz > 2000);
    }

    [Fact] public void Overclock_starts_from_the_highest_clock_the_card_reached_not_the_one_it_held()
    {
        // A factory-overclocked card holds 1665 under the load's power limit but reaches 1965: capping it at 1695 would take boost away.
        var stock = M(1665, 340, throughput: 100) with { PeakClockMHz = 1965 };
        var (outcome, steps) = Drive(new OverclockSearch(Limits with { MemoryOffsetMax = 0, MemoryOffsetMin = 0 }, Options, null, null), step => stock);
        Assert.All(steps.Where(s => s.Kind == TuneStepKind.Probe), s => Assert.True(s.Settings.MaxClockMHz >= 1995));
        Assert.Null(outcome.Settings?.MaxClockMHz);   // nothing found above the factory top: no cap that would sit below it
    }

    [Fact] public void Measurement_keeps_the_peak_clock_and_power_and_the_average_voltage()
    {
        var t = DateTimeOffset.UnixEpoch;
        var m = LoadMeasurement.From([new(t, 1665, null, 60, 340, null), new(t, 1965, null, 62, 349, null)], 10, 0, false, [0.9, 1.0]);
        Assert.Equal(1965, m.PeakClockMHz); Assert.Equal(349, m.PeakPowerW); Assert.Equal(0.95, m.AverageVoltageV!.Value, 3);
    }

    [Fact] public void Measurement_keeps_the_hottest_and_the_average_hot_spot_and_none_when_it_was_not_read()
    {
        var t = DateTimeOffset.UnixEpoch; var samples = new GpuTelemetry[] { new(t, 1665, null, 60, 340, null) };
        var m = LoadMeasurement.From(samples, 10, 0, false, null, [70, 80]);
        Assert.Equal(80, m.MaxHotSpotC); Assert.Equal(75, m.AverageHotSpotC);
        Assert.Null(LoadMeasurement.From(samples, 10, 0, false).MaxHotSpotC);
    }

    [Fact] public void An_overclock_counts_when_the_scene_runs_faster_though_the_stress_test_is_power_bound()
    {
        // Held at the power limit the stress test never sees the clock cap; the garden scene, a game's load, does: more clock, more frames.
        var stock = M(1665, 340, throughput: 100) with { PeakClockMHz = 1965 };
        var (outcome, steps) = Drive(new OverclockSearch(Limits with { MemoryOffsetMax = 0, MemoryOffsetMin = 0 }, Options, null, null),
            step => step.Load == GpuLoadKind.Scene ? M(Math.Min(step.Settings.MaxClockMHz ?? 1965, 2025), 300, throughput: 60 + (Math.Min(step.Settings.MaxClockMHz ?? 1965, 2025) - 1965) / 10.0) : stock);
        Assert.Equal(AutoTuneVerdict.Improved, outcome.Verdict); Assert.Equal(2025, outcome.Settings!.MaxClockMHz);
        Assert.Equal(GpuLoadKind.Scene, steps[^1].Load);
    }

    [Fact] public void Overclock_plus_stops_at_the_temperature_ceiling_and_needs_a_power_limit_to_raise()
    {
        var (hot, _) = Drive(new OverclockSearch(Limits, AutoTuneOptions.Plus(), new(0, 0, 1905, null, null), M(1905, 300, throughput: 100), plus: true), step => M(step.Settings.MaxClockMHz ?? 1905, 330, temp: 90, throughput: 110));
        Assert.Equal(AutoTuneVerdict.NoImprovement, hot.Verdict);
        var (none, steps) = Drive(new OverclockSearch(Limits with { PowerLimitMinW = null, PowerLimitMaxW = null }, AutoTuneOptions.Plus(), null, null, plus: true), _ => M(1905, 300));
        Assert.Equal(AutoTuneVerdict.Unsupported, none.Verdict); Assert.Empty(steps);
    }

    [Fact] public void Overclock_without_a_baseline_measures_stock_first()
    {
        var (_, steps) = Drive(new OverclockSearch(Limits, Options, null, null), Overclockable(2000, 400));
        Assert.Equal(TuneStepKind.Baseline, steps[0].Kind); Assert.True(steps[0].Settings.IsStock);
        Assert.Equal(GpuLoadKind.Scene, steps[1].Load); Assert.Equal(1905 + Options.ClockStep, steps[2].Settings.MaxClockMHz);
    }

    [Fact] public void Limits_refuse_values_outside_the_driver_range_instead_of_clamping()
    {
        Assert.Empty(Limits.Check(new(150, 500, 1950, 300, 60)));
        var problems = Limits.Check(new(1500, 0, null, 400, 10)).Select(p => p.Key).ToList();
        Assert.Equal(["Tuning_Bad_CoreOffset", "Tuning_Bad_PowerLimit", "Tuning_Bad_Fan"], problems);
        Assert.Contains("Tuning_Bad_Fan", (Limits with { FanCount = 0 }).Check(new(0, 0, null, null, 50)).Select(p => p.Key));
    }
}
