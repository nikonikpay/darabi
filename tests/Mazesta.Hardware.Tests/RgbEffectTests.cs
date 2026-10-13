using Mazesta.Core.Rgb; using Mazesta.Hardware.Rgb; using Xunit;
namespace Mazesta.Hardware.Tests;

public class RgbEffectTests
{
    [Fact] public void A_universal_effect_finds_the_name_each_maker_gives_it()
    {
        Assert.Equal("Rainbow Wave", RgbEffects.NativeName(["Direct", "Static", "Rainbow Wave"], RgbEffects.Rainbow));
        Assert.Equal("Pulse", RgbEffects.NativeName(["Direct", "pulse"], RgbEffects.Breathing) is "pulse" ? "Pulse" : null);
        Assert.Null(RgbEffects.NativeName(["Direct", "Static"], RgbEffects.Cycle)); Assert.Null(RgbEffects.NativeName(["Rainbow"], RgbEffects.Static));
        Assert.Equal(RgbEffects.Rainbow, RgbEffects.Universal("rainbow")); Assert.Null(RgbEffects.Universal("Comet"));
        Assert.True(RgbEffects.Covered("Spectrum Cycle")); Assert.False(RgbEffects.Covered("Comet"));
    }

    [Fact] public void A_rainbow_spreads_over_the_strip_and_moves_with_time()
    {
        var a = RgbFrames.Frame(RgbEffects.Rainbow, 0, 12, new RgbColor(255, 0, 0), null, 50, 100); var b = RgbFrames.Frame(RgbEffects.Rainbow, 1, 12, new RgbColor(255, 0, 0), null, 50, 100);
        Assert.Equal(12, a.Length); Assert.True(a.Distinct().Count() > 6); Assert.NotEqual(a, b);
        Assert.Equal(new RgbColor(255, 0, 0), a[0]);   // hue 0 is red
    }

    [Fact] public void Breathing_goes_dark_and_back_and_a_flash_is_on_or_off()
    {
        var c = new RgbColor(200, 100, 50);
        Assert.Equal(new RgbColor(0, 0, 0), RgbFrames.Frame(RgbEffects.Breathing, 0, 3, c, null, 50, 100)[0]);
        var peak = RgbFrames.Frame(RgbEffects.Breathing, 1 / (0.2 + 0.9) / 0.5 * 0.5, 3, c, null, 50, 100)[0]; Assert.True(peak.R > 190);
        Assert.Equal(c, RgbFrames.Frame(RgbEffects.Flash, 0, 2, c, null, 50, 100)[1]);
        Assert.Equal(new RgbColor(0, 0, 0), RgbFrames.Frame(RgbEffects.Flash, 0.6 / (0.2 + 0.9), 2, c, null, 50, 100)[1]);
    }

    [Fact] public void Brightness_dims_every_frame()
        => Assert.Equal(new RgbColor(100, 50, 25), RgbFrames.Frame(RgbEffects.Static, 0, 1, new RgbColor(200, 100, 50), null, 50, 50)[0]);

    [Fact] public void A_gradient_runs_from_the_first_colour_to_the_last_and_a_palette_replaces_the_colour_wheel()
    {
        var palette = RgbPalettes.Find("sunset")!.Colors.Select(RgbColor.Parse).OfType<RgbColor>().ToList();
        var g = RgbFrames.Gradient(palette, 9); Assert.Equal(palette[0], g[0]); Assert.Equal(palette[^1], g[^1]); Assert.Equal(palette[1], g[4]);
        var r = RgbFrames.Frame(RgbEffects.Rainbow, 0, 3, palette[0], palette, 50, 100); Assert.Equal(palette[0], r[0]);
        Assert.Single(RgbFrames.Gradient([new RgbColor(1, 2, 3)], 1)); Assert.Equal(3, RgbFrames.Gradient(palette, 3).Length);
    }

    [Fact] public void Every_palette_has_two_or_more_valid_colours()
    {
        foreach (var p in RgbPalettes.All) { Assert.True(p.Colors.Count >= 2, p.Id); Assert.All(p.Colors, c => Assert.NotNull(RgbColor.Parse(c))); }
        Assert.Equal(RgbPalettes.All.Count, RgbPalettes.All.Select(p => p.Id).Distinct().Count());
    }
}
