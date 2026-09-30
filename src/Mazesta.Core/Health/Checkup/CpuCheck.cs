using Mazesta.Core.Hardware; using static Mazesta.Core.Health.Checkup.Measures;
namespace Mazesta.Core.Health.Checkup;

/// <summary>
/// What the monitor measured while a CPU benchmark ran. <see cref="Clock"/> is, per second, the mean of the cores (the P-cores alone on a hybrid
/// CPU) for an all-thread run, or the fastest core for a one-thread run. <see cref="TjMaxC"/> is the limit the processor itself reports (Intel,
/// through its "distance to TjMax"), null where it reports none (AMD). <see cref="BaseClockMhz"/> is the base clock the firmware reports.
/// <see cref="Spec"/> is the model's published figures, when the model is listed.
/// </summary>
public sealed record CpuRunTrace(bool AllThreads, double Seconds, Series? Temp, Series? Clock, Series? Power, Series? Load, double? TjMaxC, int? BaseClockMhz,
    bool? OnBattery = null, CpuSpec? Spec = null);

/// <summary>
/// Reads a CPU run the way a technician would:
/// <list type="bullet">
/// <item>A clock under 1 GHz under load is a processor held down: a laptop on a failing or too weak charger, the board's PROCHOT signal, a power
/// plan. Nothing else is judged then.</item>
/// <item>Heat, against the limit the processor reports itself (Intel) or its maker publishes (AMD's Tjmax, 95 °C on many models, 89-90 °C on
/// others): a processor at its limit that keeps its clock is running as designed (Ryzen 7000/9000 and Core i9 13th/14th gen boost until they get
/// there); the clock it gives up at the limit is what says the cooling is short. With no limit known, a temperature flat at its top while the clock
/// falls is read the same way.</item>
/// <item>Boost: a one-thread run whose fastest core stays well under the model's published single-core boost.</item>
/// <item>Power: a step down in power while the temperature had room is the BIOS's (or the laptop's) short/long power limits; the power it settles
/// at is compared with Intel's published Base and Maximum Turbo Power, so the setting in use can be named.</item>
/// <item>An all-core clock below the base clock.</item>
/// </list>
/// </summary>
public static class CpuCheck
{
    public const double StuckMhz = 1000, WarmupSeconds = 3, EarlySeconds = 10;
    /// <summary>The clock given up at the temperature limit (start of the run to its end) that needs a look, and that is a fault.</summary>
    public const double DropAttention = 5, DropProblem = 15;
    public const int MinSamples = 8;

    public static IReadOnlyList<Finding> Evaluate(CpuRunTrace run)
    {
        var found = new List<Finding>();
        if (run.Clock is not { } allClock || allClock.Between(WarmupSeconds, double.MaxValue) is not { Count: >= MinSamples } clock) return found;
        void Add(FindingCode code, FindingLevel level, IReadOnlyList<Measure> measures, FindingHint hint = FindingHint.None, string? source = null)
            => found.Add(new(code, level, HardwareKind.Cpu, measures, null, hint, source));

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
        var spec = run.Spec;

        int? baseMhz = run.BaseClockMhz is > 0 ? run.BaseClockMhz : spec?.BaseMhz;
        if (run.AllThreads && baseMhz is int b && lateClock < b * 0.85)
            Add(FindingCode.CpuBelowBaseClock, lateClock < b * 0.7 ? FindingLevel.Problem : FindingLevel.Attention, [M("Check_M_ClockLoad", lateClock, MHz), M("Check_M_BaseClock", b, MHz)],
                source: run.BaseClockMhz is > 0 ? null : spec?.Source);

        // One busy thread lifts one core to its boost: most seconds should be near the model's published single-core boost.
        if (!run.AllThreads && spec?.BoostMhz is int boost && median < boost * 0.85)
            Add(FindingCode.CpuBelowBoost, median < boost * 0.7 ? FindingLevel.Problem : FindingLevel.Attention, [M("Check_M_ClockPeak", median, MHz), M("Check_M_BoostSpec", boost, MHz)],
                run.OnBattery == true ? FindingHint.OnBattery : FindingHint.None, spec.Source);

        bool heatLimited = Heat(run, earlyClock, lateClock, drop, lateFrom, found);
        if (run.AllThreads && !heatLimited) { PowerStep(run, lateFrom, lateClock, found); PowerLimit(run, lateFrom, found); }
        return found;
    }

    private static FindingLevel ByDrop(double drop) => drop >= DropProblem ? FindingLevel.Problem : drop >= DropAttention ? FindingLevel.Attention : FindingLevel.Note;

    /// <summary>The heat verdict; true when the run was held back by heat.</summary>
    private static bool Heat(CpuRunTrace run, double earlyClock, double lateClock, double drop, double lateFrom, List<Finding> found)
    {
        if (run.Temp?.Between(WarmupSeconds, double.MaxValue) is not { Count: >= MinSamples } temp) return false;
        double max = temp.Max();
        Measure[] clocks = [M("Check_M_ClockStart", earlyClock, MHz), M("Check_M_ClockEnd", lateClock, MHz), M("Check_M_ClockDrop", drop, Percent)];
        var late = temp.Between(lateFrom, double.MaxValue); if (late.Count < 3) late = temp;
        bool steadyPower = run.Power?.Between(lateFrom, double.MaxValue) is { Count: >= 3 } lp && lp.Median() >= 5 && lp.Variation() <= 0.04;
        Measure[] power = run.Power?.Between(lateFrom, double.MaxValue) is { Count: > 0 } pw ? [M("Check_M_Power", pw.Median(), Watt)] : [];
        var steady = steadyPower ? FindingHint.PowerSteady : FindingHint.None;

        // The chip's own limit first; else the one its maker publishes.
        double? tj = run.TjMaxC ?? run.Spec?.TjMaxC;
        if (tj is { } limit)
        {
            string? source = run.TjMaxC is null ? run.Spec?.Source : null;
            var tjMeasure = M(run.TjMaxC is null ? "Check_M_TjMaxSpec" : "Check_M_TjMax", limit, Celsius);
            double margin = limit - max;
            if (margin <= 2)
            {
                found.Add(new(FindingCode.CpuAtTjMax, ByDrop(drop), HardwareKind.Cpu, [M("Check_M_TempMax", max, Celsius), tjMeasure, .. clocks], null, FindingHint.None, source));
                return drop >= DropAttention;
            }
            found.Add(margin <= 10
                ? new(FindingCode.CpuNearTjMax, FindingLevel.Note, HardwareKind.Cpu, [M("Check_M_TempMax", max, Celsius), tjMeasure, .. power], null, steady, source)
                : new(FindingCode.CpuHeatOk, FindingLevel.Good, HardwareKind.Cpu, [M("Check_M_TempMax", max, Celsius), tjMeasure, .. power], null, steady, source));
            return false;
        }
        // No limit known: a temperature that sat flat at its top for most of the late run is a ceiling being held.
        double top = late.Max();
        bool flat = late.V.Count(v => v >= top - 1.5) >= late.Count * 0.6 && late.Percentile(0.9) - late.Percentile(0.1) <= 3;
        if (flat && top >= 85)
        {
            Measure[] measures = [M("Check_M_TempHeld", late.Median(), Celsius), .. clocks];
            if (drop >= DropAttention) { found.Add(new(FindingCode.CpuHeldAtCeiling, ByDrop(drop), HardwareKind.Cpu, measures)); return true; }
            found.Add(new(FindingCode.CpuHotSteady, FindingLevel.Note, HardwareKind.Cpu, [.. measures, .. power]));
            return false;
        }
        found.Add(new(FindingCode.CpuHeatOk, FindingLevel.Good, HardwareKind.Cpu, [M("Check_M_TempMax", max, Celsius), .. power], null, steady));
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

    /// <summary>The power an all-core run settled at, against Intel's published Processor Base Power and Maximum Turbo Power: it names the setting in
    /// use (a base-power limit, Intel's turbo-power limit, or a board past Intel's figures). AMD publishes only a TDP, which is not the limit its
    /// processors run to, so AMD runs are not judged this way.</summary>
    private static void PowerLimit(CpuRunTrace run, double lateFrom, List<Finding> found)
    {
        if (run.Spec is not { Vendor: HardwareVendor.Intel, BasePowerW: int pbp, TurboPowerW: int mtp } spec) return;
        if (run.Power?.Between(lateFrom, double.MaxValue) is not { Count: >= 3 } late) return;
        double w = late.Median();
        Measure[] m = [M("Check_M_PowerLong", w, Watt), M("Check_M_BasePowerSpec", pbp, Watt), M("Check_M_TurboPowerSpec", mtp, Watt)];
        if (w > mtp * 1.08)
        {
            // Intel's 2024 guidance for Core 13th/14th gen desktop processors is to run them at its default power settings.
            bool raptor = spec.Model.StartsWith("I", StringComparison.Ordinal) && (spec.Model.Contains("-13", StringComparison.Ordinal) || spec.Model.Contains("-14", StringComparison.Ordinal)) && spec.Segment == "Desktop";
            found.Add(new(FindingCode.CpuPowerLimit, raptor ? FindingLevel.Attention : FindingLevel.Note, HardwareKind.Cpu, m, null, FindingHint.PowerAboveSpec, spec.Source));
        }
        else if (late.Variation() <= 0.04 && Math.Abs(w - mtp) <= mtp * 0.08) found.Add(new(FindingCode.CpuPowerLimit, FindingLevel.Note, HardwareKind.Cpu, m, null, FindingHint.PowerAtTurboSpec, spec.Source));
        else if (late.Variation() <= 0.04 && Math.Abs(w - pbp) <= pbp * 0.12) found.Add(new(FindingCode.CpuPowerLimit, FindingLevel.Note, HardwareKind.Cpu, m, null, FindingHint.PowerAtBaseSpec, spec.Source));
    }
}
