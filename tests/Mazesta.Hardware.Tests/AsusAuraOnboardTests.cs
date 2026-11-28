using Xunit; using Mazesta.Hardware.Rgb;
namespace Mazesta.Hardware.Tests;

public class AsusAuraOnboardTests
{
    [Fact] public void Report_applies_on_the_channel_and_samples_the_colours_along_their_length()
    {
        var colours = Enumerable.Range(0, 240).Select(i => new RgbColor((byte)i, 2, 3)).ToList();
        var r = AsusAuraOnboard.Report(6, colours);
        Assert.Equal(65, r.Length); Assert.Equal(new byte[] { 0xEC, 0x40, 0x86, 0, 20, 0, 2, 3, 12, 2, 3 }, r[..11]);
        Assert.Equal(228, r[5 + 19 * 3]);
    }
    [Fact] public void Report_stretches_a_short_list() => Assert.Equal(9, AsusAuraOnboard.Report(6, [new RgbColor(9, 9, 9)])[5 + 19 * 3]);
}
