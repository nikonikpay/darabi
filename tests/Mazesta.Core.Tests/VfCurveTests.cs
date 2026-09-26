using Mazesta.Core.Tuning; using Xunit;
namespace Mazesta.Core.Tests;

public class VfCurveTests
{
    // A measured stock curve: flat at the minimum voltage up to 1350 MHz, then rising.
    private static readonly VfPoint[] Stock = [new(1200, 0.725), new(1350, 0.725), new(1500, 0.775), new(1800, 0.900), new(1950, 1.000)];

    [Fact] public void Voltage_for_a_clock_is_interpolated_between_measured_points_and_moves_with_the_offset()
    {
        Assert.Equal(0.8375, VfCurve.VoltageAt(Stock, 1650, 0)!.Value, 4);
        Assert.Equal(0.775, VfCurve.VoltageAt(Stock, 1650, 150)!.Value, 4);   // +150 MHz: 1650 is where 1500 was
    }
    [Fact] public void Nothing_is_extrapolated_past_the_measured_curve()
    {
        Assert.Null(VfCurve.VoltageAt(Stock, 2100, 0)); Assert.Null(VfCurve.VoltageAt(Stock, 1100, 0)); Assert.Null(VfCurve.ClockAt(Stock, 1.1));
        Assert.Null(VfCurve.PinPoint([], 0.8, 1800));
    }
    [Fact] public void On_a_flat_voltage_stretch_the_clock_at_that_voltage_is_the_highest_one()
        => Assert.Equal(1350, VfCurve.ClockAt(Stock, 0.725));

    [Fact] public void Shape_lifts_every_point_by_the_offset_and_flattens_at_the_cap()
        => Assert.Equal([1320, 1470, 1620, 1800, 1800], VfCurve.Shape(Stock, 120, 1800).Select(p => p.ClockMHz));

    [Fact] public void Pinning_a_voltage_to_a_clock_is_an_offset_in_whole_bins_and_a_cap_at_that_clock()
    {
        // Stock runs 1800 MHz at 0.900 V; asking 1905 MHz there is +105 MHz (7 bins of 15) and a cap at 1905.
        Assert.Equal((105, 1905), VfCurve.PinPoint(Stock, 0.900, 1905));
        Assert.Equal((105, 1905), VfCurve.PinPoint(Stock, 0.900, 1905, binMHz: 15));
    }
}
