namespace Mazesta.Core.Tuning;

/// <summary>One measured point of a card's voltage/frequency curve: the clock it ran and the core voltage it chose for it, under full load.</summary>
public readonly record struct VfPoint(double ClockMHz, double VoltageV);

/// <summary>
/// A measured stock curve and what NVIDIA's public controls do to it. NVML moves the whole curve (a core offset adds the same MHz at every
/// voltage) and caps the clock; that is enough for the curve undervolt of MSI Afterburner - pick a voltage, raise its point to a clock, flatten
/// everything above - but not for moving single points, which needs NVIDIA's unpublished interface and is not offered.
/// Everything here is derived from measured points and never extrapolated past them.
/// </summary>
public static class VfCurve
{
    /// <summary>The voltage the curve, moved by <paramref name="offsetMHz"/>, needs for <paramref name="clockMHz"/>: the stock voltage of
    /// (clock - offset), interpolated between the two measured points around it. Null outside the measured clocks.</summary>
    public static double? VoltageAt(IReadOnlyList<VfPoint> stock, double clockMHz, int offsetMHz)
    {
        double target = clockMHz - offsetMHz;
        var points = Sorted(stock);
        if (points.Count == 0 || target < points[0].ClockMHz || target > points[^1].ClockMHz) return null;
        for (int i = 1; i < points.Count; i++)
        {
            if (target > points[i].ClockMHz) continue;
            var (a, b) = (points[i - 1], points[i]);
            double f = b.ClockMHz - a.ClockMHz < 1e-9 ? 0 : (target - a.ClockMHz) / (b.ClockMHz - a.ClockMHz);
            return a.VoltageV + f * (b.VoltageV - a.VoltageV);
        }
        return points[0].VoltageV;
    }

    /// <summary>The stock clock at <paramref name="voltageV"/>, interpolated; null outside the measured voltages.</summary>
    public static double? ClockAt(IReadOnlyList<VfPoint> stock, double voltageV)
    {
        var points = Sorted(stock);
        if (points.Count == 0 || voltageV < points[0].VoltageV || voltageV > points[^1].VoltageV) return null;
        double? best = null;
        for (int i = 1; i < points.Count; i++)
        {
            var (a, b) = (points[i - 1], points[i]);
            if (voltageV < a.VoltageV || voltageV > b.VoltageV) continue;
            double clock = b.VoltageV - a.VoltageV < 1e-9 ? b.ClockMHz : a.ClockMHz + (voltageV - a.VoltageV) / (b.VoltageV - a.VoltageV) * (b.ClockMHz - a.ClockMHz);
            best = Math.Max(best ?? clock, clock);   // on a flat voltage stretch the card runs its highest clock there
        }
        return best ?? points[0].ClockMHz;
    }

    /// <summary>The curve the card runs after the offset and the cap: each measured point raised by the offset, and no clock above the cap.
    /// The points keep the order they were given in, so a caller can pair each with its stock point.</summary>
    public static IReadOnlyList<VfPoint> Shape(IReadOnlyList<VfPoint> stock, int offsetMHz, int? capMHz)
        => [.. stock.Select(p => p with { ClockMHz = Math.Min(p.ClockMHz + offsetMHz, capMHz ?? double.MaxValue) })];

    /// <summary>The settings that put the point measured at <paramref name="voltageV"/> on <paramref name="targetClockMHz"/> and keep every
    /// higher voltage from running faster: the offset lifts the whole curve, the cap flattens it from that point on. Offsets snap to the card's
    /// clock bins. Null when the voltage is outside the measured curve.</summary>
    public static (int OffsetMHz, int CapMHz)? PinPoint(IReadOnlyList<VfPoint> stock, double voltageV, int targetClockMHz, int binMHz = 15)
    {
        if (ClockAt(stock, voltageV) is not { } stockClock) return null;
        int offset = (int)Math.Round((targetClockMHz - stockClock) / binMHz) * binMHz;
        return (offset, targetClockMHz);
    }

    private static List<VfPoint> Sorted(IReadOnlyList<VfPoint> points) => [.. points.OrderBy(p => p.ClockMHz).ThenBy(p => p.VoltageV)];
}
