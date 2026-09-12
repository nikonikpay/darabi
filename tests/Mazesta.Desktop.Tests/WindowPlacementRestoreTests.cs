using System.Windows; using Mazesta.Desktop.Composition; using Xunit;
namespace Mazesta.Desktop.Tests;
public class WindowPlacementRestoreTests
{
    [Fact] public void A_placement_on_a_screen_that_no_longer_exists_is_rejected()
        => Assert.False(WindowPlacementRestore.IsVisibleEnough(new Rect(-30000, -30000, 1200, 760)));

    [Fact] public void A_placement_far_to_the_right_of_every_screen_is_rejected()
        => Assert.False(WindowPlacementRestore.IsVisibleEnough(new Rect(SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + 500, 100, 1200, 760)));

    [Fact] public void A_placement_inside_the_virtual_desktop_is_accepted()
        => Assert.True(WindowPlacementRestore.IsVisibleEnough(new Rect(SystemParameters.VirtualScreenLeft + 40, SystemParameters.VirtualScreenTop + 40, 800, 600)));

    [Theory]
    [InlineData(0.0, 960.0, 5000.0, 960.0)]          // a zero/absent width falls back to the minimum
    [InlineData(double.NaN, 960.0, 5000.0, 960.0)]
    [InlineData(400.0, 960.0, 5000.0, 960.0)]        // narrower than MinWidth
    [InlineData(1200.0, 960.0, 5000.0, 1200.0)]
    [InlineData(9000.0, 960.0, 5000.0, 5000.0)]      // wider than the desktop
    public void Size_is_clamped(double value, double min, double max, double expected)
        => Assert.Equal(expected, WindowPlacementRestore.Clamp(value, min, max));
}
