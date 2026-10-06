using System.IO.Compression; using System.Numerics; using Xunit;
using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class GardenLightVolumeTests
{
    [Fact] public void A_light_volume_written_and_read_again_holds_the_same_light_to_within_a_hundredth()
    {
        var light = new float[2 * 3 * 4 * 18]; for (int i = 0; i < light.Length; i++) light[i] = 0.002f * MathF.Pow(1.02f, i);   // from deep shade to a sunlit wall
        var made = new GardenLightVolume(0xFEEDUL, new(-1, 0.5f, 2), 0.75f, 2, 3, 4, light);
        var ms = new MemoryStream(); made.Write(ms); ms.Position = 0;
        var read = GardenLightVolume.Read(ms);
        Assert.Equal((0xFEEDUL, new Vector3(-1, 0.5f, 2), 0.75f, 2, 3, 4), (read.SceneStamp, read.Origin, read.Spacing, read.X, read.Y, read.Z));
        for (int i = 0; i < light.Length; i++) Assert.InRange(read.Light[i], light[i] * 0.99f, light[i] * 1.01f);
        // as a texture: six blocks side by side along x, one for each direction
        var t = read.Texels(); Assert.Equal(2 * 6 * 3 * 4 * 4, t.Length);
        int from = ((1 + 2 * (2 + 3 * 3)) * 6 + 4) * 3, to = ((3 * 3 + 2) * 12 + 4 * 2 + 1) * 4;   // point (1, 2, 3), direction +z
        Assert.Equal((float)(Half)read.Light[from + 1], (float)t[to + 1]);
        // a surface reads the direction it faces, or a mix of the three it leans toward
        Assert.Equal(new Vector3(read.Light[from], read.Light[from + 1], read.Light[from + 2]), read.At(new Vector3(-1, 0.5f, 2) + new Vector3(1, 2, 3) * 0.75f, Vector3.UnitZ));
    }

    [Fact] public void A_file_that_is_not_a_light_volume_is_refused()
    {
        static MemoryStream Gz(byte[] raw) { var ms = new MemoryStream(); using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(raw); ms.Position = 0; return ms; }
        Assert.Throws<InvalidDataException>(() => GardenLightVolume.Read(Gz(new byte[64])));
        var made = new GardenLightVolume(1, default, 1, 2, 2, 2, new float[2 * 2 * 2 * 18]); var ms = new MemoryStream(); made.Write(ms);
        using var whole = new GZipStream(new MemoryStream(ms.ToArray()), CompressionMode.Decompress); var raw = new MemoryStream(); whole.CopyTo(raw);
        Assert.Throws<InvalidDataException>(() => GardenLightVolume.Read(Gz(raw.ToArray()[..^5])));   // cut short
    }

    [Fact] public void The_embedded_garden_has_its_bounced_light_worked_out_for_this_very_scene()
    {
        // A scene exported again has another stamp: the light must then be baked again (tools/scene/export_garden.py says how),
        // or the Direct3D test falls back to the sky's light alone.
        var g = GardenScene.Embedded; var v = g.Light;
        Assert.True(v is not null, "Scene/garden.light is missing, or was baked for another garden.mzscene: bake it again.");
        Assert.Equal(g.Stamp, v.SceneStamp);
        // the grid holds the courtyard from wall to wall and the hall to over its roof
        Assert.True(v.Origin.X < -13 && v.Origin.X + (v.X - 1) * v.Spacing > 13 && v.Origin.Z < -32 && v.Origin.Z + (v.Z - 1) * v.Spacing > 6 && v.Origin.Y + (v.Y - 1) * v.Spacing > 8);
        Assert.All(v.Light, x => Assert.InRange(x, 0f, 8f));
        static float Lum(Vector3 c) => 0.3f * c.X + 0.59f * c.Y + 0.11f * c.Z;
        float open = Lum(v.At(new(9, 1.7f, -20), Vector3.UnitY)), hall = Lum(v.At(new(0, 2.6f, 3), Vector3.UnitY)), ceiling = Lum(v.At(new(0, 5.2f, 3), Vector3.UnitY));
        Assert.InRange(open, 0.03f, 1f);    // under the open sky: the sky's own light
        Assert.InRange(hall, 0.005f, 1f);   // on the hall's floor: what its lamps and its door give the room, bounced
        Assert.True(open > ceiling, "under the hall's roof no sky arrives from above");
    }
}
