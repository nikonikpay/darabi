using Xunit; using Mazesta.Desktop.Localization; using Mazesta.Desktop.Services;
namespace Mazesta.Desktop.Tests;

public class TestDetailTextTests
{
    // The line of the owner's own run (RTX 4070, steady GPU load), as the executor wrote it.
    private const string Gpu = "GPU Steady compute stress on NVIDIA GeForce RTX 4070; dispatches=21360; 6813 Gop/s integer; verified 2,734,080 of 44,795,166,720 thread results through chained dispatches (a sample, not every thread); measured GPU load avg 93.5% (n=11); GPU core avg 68.3°C max 72.0°C (n=11); GPU hot spot avg 87.2°C max 94.6°C (n=11); GPU power avg 176.3 W max 196.2 W (n=11)";

    [Fact] public void A_known_detail_becomes_one_worded_line_per_part_with_the_same_numbers()
    {
        var lines = TestDetailText.Lines(Gpu);
        Assert.Equal(8, lines.Count); Assert.All(lines, l => Assert.False(l.Latin));
        Assert.Equal(Loc.Format("Detail_GpuStress", Loc.Get("Detail_Profile_Steady"), "NVIDIA GeForce RTX 4070"), lines[0].Text);
        Assert.Equal(Loc.Format("Detail_Stat", Loc.Get("Detail_L_GpuLoad"), "93.5", "%", "11"), lines[4].Text);
        Assert.Equal(Loc.Format("Detail_Stat_Max", Loc.Get("Detail_L_GpuHotSpot"), "87.2", "94.6", "°C", "11"), lines[6].Text);
        Assert.Contains("44,795,166,720", lines[3].Text);
    }

    [Fact] public void A_part_that_is_not_recognised_is_kept_as_written_on_its_own_line()
    {
        var lines = TestDetailText.Lines("radix-2 complex FFT, N=1024; threads=24; something new avg 3.0 kPa (n=2)");
        Assert.Equal([true, false, true], lines.Select(l => l.Latin)); Assert.Equal("radix-2 complex FFT, N=1024", lines[0].Text); Assert.Equal("something new avg 3.0 kPa (n=2)", lines[2].Text);
        Assert.Empty(TestDetailText.Lines(null)); Assert.Empty(TestDetailText.Lines("  "));
    }
}
