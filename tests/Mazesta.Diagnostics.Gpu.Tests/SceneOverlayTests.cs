using Xunit;
using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class SceneOverlayTests
{
    [Fact] public void The_readout_text_is_drawn_in_its_colour_on_black()
    {
        using var canvas = new Canvas(400, 60);
        canvas.Text(4, 4, 400, 60, "128 FPS", 0x00FF00, 20, 400); canvas.Flush();
        var px = canvas.Pixels; int lit = 0, off = 0;
        for (int i = 0; i < px.Length; i += 4) { if (px[i + 1] > 128) lit++; if (px[i] > 8 || px[i + 2] > 8) off++; }   // BGRA: only green may be set
        Assert.True(lit > 50, $"{lit} lit pixels"); Assert.Equal(0, off);
    }

    [Fact] public void Clearing_removes_the_old_drawing()
    {
        using var canvas = new Canvas(200, 40);
        canvas.Card(0, 0, 200, 40, 0x5A4C00, 0x1C1800, 4); canvas.Text(0, 0, 200, 40, "old", 0xFFFFFF, 16, 700); canvas.Flush(); canvas.Clear();
        Assert.True(canvas.Pixels.IndexOfAnyExcept((byte)0) < 0);
    }

    [Fact] public void A_card_tint_is_dim_so_the_panel_reads_it_as_a_tint()
    {
        using var canvas = new Canvas(100, 100);
        canvas.Card(0, 0, 100, 100, 0x5A4C00, 0x1C1800, 6); canvas.Flush();
        var px = canvas.Pixels; int i = (50 * 100 + 50) * 4;
        Assert.Equal((0x00, 0x18, 0x1C), (px[i], px[i + 1], px[i + 2]));
    }
}
