using Mazesta.Hardware.Nvidia; using Microsoft.Extensions.Logging.Abstractions; using Xunit;
namespace Mazesta.Hardware.Tests;

/// <summary>Read-only: opens the NVIDIA card through NVML and reads what the tuning page shows. Nothing is applied - writing needs
/// administrator rights and changes the card, so it is verified by hand (docs/VERIFICATION-slice9-gpu-tuning.md).</summary>
[Trait("Category", "Hardware")]
public class NvmlTuningHardwareTests
{
    [Fact] public void The_nvidia_card_reports_offset_ranges_limits_and_live_readings()
    {
        var provider = new NvmlTuningProvider(NullLogger.Instance);
        var card = Assert.Single(provider.Devices, d => d.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("GPU-", card.Id);
        Assert.True(card.Limits.HasCoreOffset); Assert.True(card.Limits.HasMemoryOffset); Assert.True(card.Limits.HasPowerLimit);
        var t = card.ReadTelemetry();
        Assert.True(t.CoreClockMHz > 0); Assert.True(t.TemperatureC > 0); Assert.True(t.PowerW > 0);
        Assert.NotNull(card.ReadCurrent());
    }

    /// <summary>The curve the driver runs the card by, read without loading it. The reading is printed, to be compared by eye with a maker's program.</summary>
    [Fact] public void The_driver_gives_the_cards_stock_curve_without_a_scan()
    {
        var provider = new NvmlTuningProvider(NullLogger.Instance);
        var card = Assert.Single(provider.Devices, d => d.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        var curve = NvApiCurve.ReadStock(card.Name);
        Assert.NotNull(curve);
        if (Environment.GetEnvironmentVariable("MAZESTA_RENDER_DIR") is { Length: > 0 } dir)
            File.WriteAllLines(Path.Combine(dir, "curve.txt"), [$"{card.Name} core offset now {card.ReadCurrent().CoreOffsetMHz}", .. curve.Select(p => $"{p.VoltageV:F4} V  {p.ClockMHz:F1} MHz")]);
        Assert.True(curve.Count >= 8);
        Assert.InRange(curve[^1].ClockMHz, 500, 4000); Assert.InRange(curve[^1].VoltageV, 0.6, 1.6);
    }
}

public class NvApiCurveTests
{
    private static Mazesta.Core.Tuning.VfPoint[] Rising(int n) => [.. Enumerable.Range(0, n).Select(i => new Mazesta.Core.Tuning.VfPoint(300 + i * 20, 0.5 + i * 0.01))];
    [Fact] public void A_rising_table_of_card_like_values_is_believed() => Assert.True(NvApiCurve.Plausible(Rising(40)));
    [Fact] public void Too_few_points_are_not_a_curve() => Assert.False(NvApiCurve.Plausible(Rising(5)));
    [Fact] public void A_table_that_falls_is_not_a_curve() { var p = Rising(40); p[20] = new(100, p[20].VoltageV); Assert.False(NvApiCurve.Plausible(p)); }
    [Fact] public void Values_no_card_runs_at_are_refused() { var p = Rising(40); p[^1] = new(9500, p[^1].VoltageV); Assert.False(NvApiCurve.Plausible(p)); }
}
