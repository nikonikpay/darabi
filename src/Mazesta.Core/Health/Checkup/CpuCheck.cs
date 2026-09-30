using Mazesta.Core.Hardware; using static Mazesta.Core.Health.Checkup.Measures;
namespace Mazesta.Core.Health.Checkup;

/// <summary>
/// What the monitor measured while a CPU benchmark ran. <see cref="Clock"/> is, per second, the mean of the cores (the P-cores alone on a hybrid
/// CPU) for an all-thread run, or the busiest core for a one-thread run. <see cref="TjMaxC"/> is the limit the processor itself reports (Intel,
/// through its "distance to TjMax"), null where it reports none (AMD). <see cref="BaseClockMhz"/> is the base clock the firmware reports.
/// </summary>
public sealed record CpuRunTrace(bool AllThreads, double Seconds, Series? Temp, Series? Clock, Series? Power, Series? Load, double? TjMaxC, int? BaseClockMhz,
    bool? OnBattery = null);

/// <summary>
/// Reads a CPU run the way a technician would, from its own measurements only (no model table):
/// <list type="bullet">
/// <item>A clock under 1 GHz under load is a processor held down: a laptop on a failing or too weak charger, the board's PROCHOT signal, a power
/// plan. Nothing else is judged then.</item>
/// <item>Heat: with the processor's own limit (Intel), how close the run came to it; without one (AMD), whether the temperature sat flat at its
/// top - AMD holds its limit (95 °C on many models) and lowers the clock instead, so a flat top with a falling clock is the sign, not the degree.</item>
/// <item>Power: a step down in power while the temperature had room is the BIOS's (or the laptop's) short/long power limits, not a fault - Intel's
/// default profiles on Core 13th/14th gen hold the P-cores under 5 GHz this way.</item>
/// <item>An all-core clock below the base clock the firmware reports.</item>
/// </list>
/// </summary>
public static class CpuCheck
{
    public const double StuckMhz = 1000, WarmupSeconds = 3, EarlySeconds = 10;
    public const int MinSamples = 8;

    public static IReadOnlyList<Finding> Evaluate(CpuRunTrace run)
    {
        var found = new List<Finding>();
        if (run.Clock is not { } allClock || allClock.Between(WarmupSeconds, double.MaxValue) is not { Count: >= MinSamples } clock) return found;
        void Add(FindingCode code, FindingLevel level, IReadOnlyList<Measure> measures, FindingHint hint = FindingHint.None) => found.Add(new(code, level, HardwareKind.Cpu, measures, null, hint));

        // An all-thread run the CPU did not get to itself says little about its clock or heat.
        if (run.AllThreads && run.Load?.Between(WarmupSeconds, double.MaxValue) is { Count: >= MinSamples } load && load.Median() < 85)
        {
            Add(FindingCode.CpuNotFullyLoaded, FindingLevel.Note, [M("Check_M_Load", load.Median(), Percent)]);
            return found;
        }

        double median = clock.Median();
        if (median < StuckMhz)
        {
            var m = new List<Measure> { M("Check_M_Clock", median, MHz) };
            if (run.Power?.Between(WarmupSeconds, double.MaxValue) is { Count: > 0 } p) m.Add(M("Check_M_Power", p.Median(), Watt));
            Add(FindingCode.CpuStuckLowClock, FindingLevel.Problem, m, run.OnBattery == true ? FindingHint.OnBattery : FindingHint.None);
            return found;
        }

        double lateFrom = Math.Max(WarmupSeconds, run.Seconds * 0.6);
        var early = clock.Between(WarmupSeconds, WarmupSeconds + EarlySeconds); var late = clock.Between(lateFrom, double.MaxValue);
        if (late.Count < 3) late = clock;
        double earlyClock = early.Count > 0 ? early.Median() : median, lateClock = late.Median();
        double drop = earlyClock > 0 ? Math.Max(0, (earlyClock - lateClock) / earlyClock * 100) : 0;

        if (run.AllThreads && run.BaseClockMhz is int baseMhz && baseMhz > 0 && lateClock < baseMhz * 0.85)
            Add(FindingCode.CpuBelowBaseClock, lateClock < baseMhz * 0.7 ? FindingLevel.Problem : FindingLevel.Attention, [M("Check_M_ClockLoad", lateClock, MHz), M("Check_M_BaseClock", baseMhz, MHz)]);

        bool heatLimited = Heat(run, earlyClock, lateClock, drop, lateFrom, found);
        if (run.AllThreads && !heatLimited) PowerStep(run, lateFrom, lateClock, found);
        return found;
    }

    /// <summary>The heat verdict; true when the run was held back by heat.</summary>
    private static bool Heat(CpuRunTrace run, double earlyClock, double lateClock, double drop, double lateFrom, List<Finding> found)
    {
        if (run.Temp?.Between(WarmupSeconds, double.MaxValue) is not { Count: >= MinSamples } temp) return false;
        double max = temp.Max();
        var clocks = new List<Measure> { M("Check_M_ClockStart", earlyClock, MHz), M("Check_M_ClockEnd", lateClock, MHz), M("Check_M_ClockDrop", drop, Percent) };
        var late = temp.Between(lateFrom, double.MaxValue); if (late.Count < 3) late = temp;
        bool steadyPower = run.Power?.Between(lateFrom, double.MaxValue) is { Count: >= 3 } lp && lp.Median() >= 5 && lp.Variation() <= 0.04;
        Measure[] power = run.Power?.Between(lateFrom, double.MaxValue) is { Count: > 0 } pw ? [M("Check_M_Power", pw.Median(), Watt)] : [];
        if (run.TjMaxC is { } tj)
        {
            double margin = tj - max;
            if (margin <= 2)
            {
                found.Add(new(FindingCode.CpuAtTjMax, drop >= 10 ? FindingLevel.Problem : FindingLevel.Attention, HardwareKind.Cpu, [M("Check_M_TempMax", max, Celsius), M("Check_M_TjMax", tj, Celsius), .. clocks]));
                return true;
            }
            found.Add(margin <= 10
                ? new(FindingCode.CpuNearTjMax, FindingLevel.Note, HardwareKind.Cpu, [M("Check_M_TempMax", max, Celsius), M("Check_M_TjMax", tj, Celsius), .. power], null, steadyPower ? FindingHint.PowerSteady : FindingHint.None)
                : new(FindingCode.CpuHeatOk, FindingLevel.Good, HardwareKind.Cpu, [M("Check_M_TempMax", max, Celsius), M("Check_M_TjMax", tj, Celsius), .. power], null, steadyPower ? FindingHint.PowerSteady : FindingHint.None));
            return false;
        }
        // No limit from the processor: a temperature that sat flat at its top for most of the late run is a ceiling being held.
        double top = late.Max();
        bool flat = late.V.Count(v => v >= top - 1.5) >= late.Count * 0.6 && late.Percentile(0.9) - late.Percentile(0.1) <= 3;
        if (flat && top >= 85)
        {
            List<Measure> measures = [M("Check_M_TempHeld", late.Median(), Celsius), .. clocks];
            if (drop >= 3) { found.Add(new(FindingCode.CpuHeldAtCeiling, drop >= 10 ? FindingLevel.Problem : FindingLevel.Attention, HardwareKind.Cpu, measures)); return true; }
            found.Add(new(FindingCode.CpuHotSteady, FindingLevel.Note, HardwareKind.Cpu, [.. measures, .. power]));
            return false;
        }
        found.Add(new(FindingCode.CpuHeatOk, FindingLevel.Good, HardwareKind.Cpu, [M("Check_M_TempMax", max, Celsius), .. power], null, steadyPower ? FindingHint.PowerSteady : FindingHint.None));
        return false;
    }

    /// <summary>A clear step down in power partway through (the short-term limit ending, then the long-term one holding it) while heat was not the cause.</summary>
    private static void PowerStep(CpuRunTrace run, double lateFrom, double lateClock, List<Finding> found)
    {
        if (run.Power?.Between(WarmupSeconds, double.MaxValue) is not { Count: >= MinSamples } power) return;
        var early = power.Between(WarmupSeconds, WarmupSeconds + EarlySeconds); var late = power.Between(lateFrom, double.MaxValue);
        if (early.Count < 3 || late.Count < 3) return;
        double high = early.Median(), low = late.Median();
        if (low < 5 || high < low * 1.15) return;
        double mid = (high + low) / 2, at = double.NaN;
        for (int i = 0; i + 2 < power.Count; i++)
            if (power.V[i] < mid && power.V[i + 1] < mid && power.V[i + 2] < mid) { at = power.T[i]; break; }
        if (double.IsNaN(at)) return;
        found.Add(new(FindingCode.CpuPowerStep, FindingLevel.Note, HardwareKind.Cpu,
            [M("Check_M_PowerShort", high, Watt), M("Check_M_PowerLong", low, Watt), M("Check_M_StepAfter", at, Seconds), M("Check_M_ClockLoad", lateClock, MHz)]));
    }
}
