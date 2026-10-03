namespace Mazesta.Core.Inventory;

/// <summary>
/// A laptop battery as its own controller reports it to Windows. Capacities are in mWh, the rate in mW (charging or discharging, as the flags
/// say), the voltage in mV. A figure the battery does not report is null, never 0: a controller that gives no design capacity has no health.
/// </summary>
public sealed record BatteryInfo(string? Name, string? Maker, long? DesignMwh, long? FullMwh, long? RemainingMwh, int? CycleCount, bool? Charging, bool? Discharging, bool? OnMains,
    long? RateMw, long? VoltageMv)
{
    /// <summary>What the battery holds when full, against what it held when new. A new battery may read a little over 100; it is given as measured.</summary>
    public double? HealthPercent => Ratio(FullMwh, DesignMwh);
    /// <summary>What it holds now, against what it holds when full.</summary>
    public double? ChargePercent => Ratio(RemainingMwh, FullMwh);
    /// <summary>The capacity lost since new, in mWh; null without both figures.</summary>
    public long? LostMwh => DesignMwh is > 0 and var d && FullMwh is > 0 and var f ? Math.Max(0, d - f) : null;

    private static double? Ratio(long? part, long? whole) => part is >= 0 and var p && whole is > 0 and var w ? Math.Round(p * 100.0 / w, 1) : null;
}

/// <summary>What a drain test measured between two readings of the same battery: the energy that left it, the mean power, and how long a full
/// charge would last at that power. Null when the readings can not give it (no capacity reported, the charger was in, the charge did not fall).</summary>
public sealed record BatteryDrain(long UsedMwh, double Minutes, double MeanWatts, double? FullChargeMinutes)
{
    /// <summary>Under this, the capacity counter's own step (a few mWh on most packs) is too large a share of what was measured.</summary>
    public const long SmallestMwh = 100;

    public static BatteryDrain? Between(BatteryInfo start, BatteryInfo end, TimeSpan elapsed)
    {
        if (start.RemainingMwh is not { } a || end.RemainingMwh is not { } b || elapsed.TotalSeconds < 30) return null;
        long used = a - b;
        if (used < SmallestMwh) return null;
        double watts = used / 1000.0 / elapsed.TotalHours;
        return new(used, Math.Round(elapsed.TotalMinutes, 1), Math.Round(watts, 1), end.FullMwh is > 0 and var full ? Math.Round(full / 1000.0 / watts * 60) : null);
    }
}
