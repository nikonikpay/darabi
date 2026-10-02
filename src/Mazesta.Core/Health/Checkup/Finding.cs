using Mazesta.Core.Hardware;
namespace Mazesta.Core.Health.Checkup;

/// <summary>How much a finding matters. <see cref="Good"/> is a check that was made and held; <see cref="Note"/> explains what was measured without
/// calling it a fault (a power limit the BIOS sets, too few other systems to judge by); <see cref="Attention"/> is worth a look; <see cref="Problem"/>
/// means the part is not working as it should.</summary>
public enum FindingLevel { Good, Note, Attention, Problem }

/// <summary>What a finding says. The app words each one (title, explanation, what to do); stored reports keep the words, so a code may be added
/// but never renamed.</summary>
public enum FindingCode
{
    CpuNotFullyLoaded, CpuStuckLowClock, CpuBelowBaseClock, CpuAtTjMax, CpuNearTjMax, CpuHeldAtCeiling, CpuHotSteady, CpuHeatOk, CpuPowerStep,
    PowerPlanCap, PowerPlanNoBoost, PowerOnBattery, PowerPlanOk,
    RamXmpOff, RamXmpOn, RamBelowJedec, RamAtJedec, RamProfileUnread, RamSingleChannel, RamSameChannel, RamMixed,
    GpuPowerBrake, GpuHwThermal, GpuHwSlowdown, GpuThermalSlowdown, GpuPowerLimited, GpuHeatOk, GpuHotspotGap,
    GpuLinkNarrow, GpuLinkSlowGen, GpuSlotNarrow, GpuSlotOlderGen, GpuLinkOk,
    DriveLinkNarrow, DriveSlotLimited,
    BenchBelowPeers, BenchWithPeers, BenchFewPeers,
    GpuLinkBelowCard, DriveLinkBelowDrive,
    CpuBelowBoost, CpuPowerLimit,
    GpuPcieErrorsOk, GpuPcieErrorsMany, GpuPcieErrorsFatal, GpuPcieErrorsUnderLoad, GpuPcieCleanUnderLoad,
}

/// <summary>A second sentence a finding may carry: what the measurements beside it point to.</summary>
public enum FindingHint { None, OnBattery, PowerSteady, LessPowerThanPeers, HotterThanPeers, LowerClockThanPeers, PowerAtBaseSpec, PowerAtTurboSpec, PowerAboveSpec }

/// <summary>One measured number a finding stands on. <see cref="Key"/> names it (a localisation key), <see cref="Unit"/> is its symbol, "" for a count.</summary>
public sealed record Measure(string Key, double Value, string Unit);

/// <summary>A finding as data; the app words it in the user's language. <see cref="Subject"/> names the device when there can be several (a GPU,
/// a drive, a benchmark). Every finding is backed by what was measured or read from the part itself, or, where <see cref="Source"/> is set, by a
/// figure its maker publishes on that page.</summary>
public sealed record Finding(FindingCode Code, FindingLevel Level, HardwareKind Part, IReadOnlyList<Measure> Measures, string? Subject = null, FindingHint Hint = FindingHint.None,
    string? Source = null);

/// <summary>One quantity sampled over a run: <see cref="T"/> seconds from its start, <see cref="V"/> the readings, in time order. A missed poll is
/// simply absent, never a zero.</summary>
public sealed record Series(IReadOnlyList<double> T, IReadOnlyList<double> V)
{
    public int Count => V.Count;
    public static Series Of(IEnumerable<(double T, double V)> points)
    {
        var p = points.Where(x => double.IsFinite(x.V)).OrderBy(x => x.T).ToList();
        return new([.. p.Select(x => x.T)], [.. p.Select(x => x.V)]);
    }
    public Series Between(double from, double to) => Of(T.Select((t, i) => (t, V[i])).Where(x => x.t >= from && x.t < to));
    public double Max() => V.Max();
    public double Mean() => V.Average();
    public double Median() => Percentile(0.5);
    /// <summary>The p-th quantile (0 … 1) by linear interpolation between the two nearest readings.</summary>
    public double Percentile(double p)
    {
        var s = V.Order().ToArray();
        double at = Math.Clamp(p, 0, 1) * (s.Length - 1);
        int lo = (int)Math.Floor(at), hi = (int)Math.Ceiling(at);
        return s[lo] + (s[hi] - s[lo]) * (at - lo);
    }
    /// <summary>Spread over mean (the coefficient of variation): 0.02 means the readings stayed within about 2 % of their mean.</summary>
    public double Variation()
    {
        double mean = Mean();
        return mean == 0 ? double.PositiveInfinity : Math.Sqrt(V.Sum(v => (v - mean) * (v - mean)) / V.Count) / Math.Abs(mean);
    }
}

internal static class Measures
{
    public const string Celsius = "°C", MHz = "MHz", Watt = "W", Percent = "%", Seconds = "s", MTs = "MT/s", Lanes = "×", None = "";
    public static Measure M(string key, double value, string unit) => new(key, value, unit);
}
