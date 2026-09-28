using Xunit;
using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class SceneOverlayTests
{
    [Fact] public void The_readout_text_is_drawn_in_its_colour_on_black()
    {
        using var bitmap = new TextBitmap(400, 60, 20);
        bitmap.Draw([("128 FPS", 0x00FF00, 4, 4, false)]);
        var px = bitmap.Pixels; int lit = 0, off = 0;
        for (int i = 0; i < px.Length; i += 4) { if (px[i + 1] > 128) lit++; if (px[i] > 8 || px[i + 2] > 8) off++; }   // BGRA: only green may be set
        Assert.True(lit > 50, $"{lit} lit pixels"); Assert.Equal(0, off);
    }

    [Fact] public void Redrawing_clears_the_old_text()
    {
        using var bitmap = new TextBitmap(200, 40, 16);
        bitmap.Draw([("old", 0xFFFFFF, 0, 0, true)]); bitmap.Draw([]);
        Assert.True(bitmap.Pixels.IndexOfAnyExcept((byte)0) < 0);
    }
}
