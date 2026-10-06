using System.IO.Compression; using System.Numerics; using Xunit;
using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class GardenLightVolumeTests
{
    /// <summary>A volume of 2 x 3 x 4 points in five parts (the sky, the garden's lamps, the hall's, the sun at two points of its arc), each part its own light.</summary>
    private static GardenLightVolume Made()
    {
        var parts = new float[5][];
        for (int p = 0; p < 5; p++) { parts[p] = new float[2 * 3 * 4 * 18]; for (int i = 0; i < parts[p].Length; i++) parts[p][i] = 0.002f * (p + 1) * MathF.Pow(1.02f, i); }   // from deep shade to a sunlit wall
        return new GardenLightVolume(0xFEEDUL, new(-1, 0.5f, 2), 0.75f, 2, 3, 4, parts);
    }

    [Fact] public void A_light_volume_written_and_read_again_holds_the_same_light_to_within_a_fiftieth()
    {
        var made = Made(); var ms = new MemoryStream(); made.Write(ms); ms.Position = 0;
        var read = GardenLightVolume.Read(ms);
        Assert.Equal((0xFEEDUL, new Vector3(-1, 0.5f, 2), 0.75f, 2, 3, 4, 5, 2), (read.SceneStamp, read.Origin, read.Spacing, read.X, read.Y, read.Z, read.Parts.Count, read.Suns));
        for (int p = 0; p < 5; p++) for (int i = 0; i < made.Parts[p].Length; i++) Assert.InRange(read.Parts[p][i], made.Parts[p][i] * 0.98f, made.Parts[p][i] * 1.02f);
        // a surface reads the direction it faces, or a mix of the three it leans toward
        int from = ((1 + 2 * (2 + 3 * 3)) * 6 + 4) * 3; var lamps = read.Parts[GardenLightVolume.Lamps];   // point (1, 2, 3), direction +z
        Assert.Equal(new Vector3(lamps[from], lamps[from + 1], lamps[from + 2]), read.At(GardenLightVolume.Lamps, new Vector3(-1, 0.5f, 2) + new Vector3(1, 2, 3) * 0.75f, Vector3.UnitZ));
    }

    [Fact] public void A_moment_s_light_is_the_parts_added_up_each_by_how_much_of_its_source_shines()
    {
        var v = Made(); var t = new float[v.TexelFloats]; Assert.Equal(2 * 6 * 3 * 4 * 4, t.Length);
        int from = ((1 + 2 * (2 + 3 * 3)) * 6 + 4) * 3, to = ((3 * 3 + 2) * 12 + 4 * 2 + 1) * 4;   // point (1, 2, 3), direction +z: six blocks side by side along x, one for each direction
        float Part(int p, int c) => v.Parts[p][from + c];
        // the sky alone, in its colour
        v.Mix(t, new(0.5f, 1, 2), 0, 0, Vector3.Zero, 0.5f);
        for (int c = 0; c < 3; c++) Assert.Equal(Part(0, c) * new[] { 0.5f, 1, 2 }[c], t[to + c], 5);
        Assert.Equal(0, t[to + 3]);
        // the garden's lamps half lit over it, the hall's a quarter, and the sun a quarter of the way between its two points (which stand at 1/4 and 3/4 of the arc)
        v.Mix(t, Vector3.One, 0.5f, 0.25f, new(2, 2, 2), 0.375f);
        for (int c = 0; c < 3; c++) { float sum = Part(0, c) + 0.5f * Part(1, c) + 0.25f * Part(2, c) + 2 * (0.75f * Part(3, c) + 0.25f * Part(4, c)); Assert.InRange(t[to + c], sum * 0.99999f, sum * 1.00001f); }
        // before its first point and after its last the sun's light is that point's
        Assert.Equal((0, 1, 0f), v.SunParts(0.1f)); Assert.Equal(1f, v.SunParts(0.9f).T + v.SunParts(0.9f).A);
        // every texel is done, whatever the width the processor adds them up at
        v.Mix(t, Vector3.One, 0, 0, Vector3.Zero, 0);
        for (int i = 0; i < 2 * 3 * 4 * 6; i++) Assert.True(t[i * 4] > 0 && t[i * 4 + 3] == 0);
    }

    [Fact] public void A_file_that_is_not_a_light_volume_is_refused()
    {
        static MemoryStream Gz(byte[] raw) { var ms = new MemoryStream(); using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(raw); ms.Position = 0; return ms; }
        Assert.Throws<InvalidDataException>(() => GardenLightVolume.Read(Gz(new byte[64])));
        var ms = new MemoryStream(); Made().Write(ms);
        using var whole = new GZipStream(new MemoryStream(ms.ToArray()), CompressionMode.Decompress); var raw = new MemoryStream(); whole.CopyTo(raw);
        Assert.Throws<InvalidDataException>(() => GardenLightVolume.Read(Gz(raw.ToArray()[..^5])));   // cut short
        var old = raw.ToArray(); old[4] = 2;                                                              // the version before this one: no part for the hall's lamps
        Assert.Throws<InvalidDataException>(() => GardenLightVolume.Read(Gz(old)));
        Assert.Throws<ArgumentException>(() => new GardenLightVolume(1, default, 1, 2, 2, 2, [new float[2 * 2 * 2 * 18], new float[2 * 2 * 2 * 18], new float[2 * 2 * 2 * 18]]));   // no sun
    }

    [Fact] public void The_embedded_garden_has_its_bounced_light_worked_out_for_this_very_scene()
    {
        // A scene exported again has another stamp: the light must then be baked again (tools/scene/export_garden.py says how),
        // or the Direct3D test falls back to the sky's light alone.
        var g = GardenScene.Embedded; var v = g.Light;
        Assert.True(v is not null, "Scene/garden.light is missing, of an earlier build's making, or was baked for another garden.mzscene: bake it again.");
        Assert.Equal(g.Stamp, v.SceneStamp);
        // the grid holds the courtyard from wall to wall and the hall to over its roof
        Assert.True(v.Origin.X < -13 && v.Origin.X + (v.X - 1) * v.Spacing > 13 && v.Origin.Z < -32 && v.Origin.Z + (v.Z - 1) * v.Spacing > 6 && v.Origin.Y + (v.Y - 1) * v.Spacing > 8);
        Assert.InRange(v.Suns, 4, 16);
        Assert.All(v.Parts, part => Assert.All(part, x => Assert.InRange(x, 0f, 8f)));
        static float Lum(Vector3 c) => 0.3f * c.X + 0.59f * c.Y + 0.11f * c.Z;
        Vector3 walk = new(9, 1.7f, -20), floor = new(0, 2.6f, 3);
        // the sky's part, of one unit of light all over: nearly all of it arrives under the open sky, a little on the hall's floor (through its door and windows)
        float open = Lum(v.At(GardenLightVolume.Sky, walk, Vector3.UnitY)), hall = Lum(v.At(GardenLightVolume.Sky, floor, Vector3.UnitY));
        Assert.InRange(open, 0.5f, 1.3f); Assert.InRange(hall, 0.002f, open / 4);
        // the lamps' parts: the walk is lit by the garden's lamps, bounced, the hall's floor by its own
        Assert.True(Lum(v.At(GardenLightVolume.Lamps, walk, Vector3.UnitY)) > 0.0005f); Assert.True(Lum(v.At(GardenLightVolume.Hall, floor, Vector3.UnitY)) > 0.005f);
        // the sun's parts: with the sun before the hall its light comes in at the windows and is given back to the ceiling; no part is all dark
        for (int k = 0; k < v.Suns; k++) Assert.True(v.Parts[GardenLightVolume.FirstSun + k].Max() > 0.05f, $"sun part {k}");
        Assert.True(Lum(v.At(GardenLightVolume.FirstSun + v.Suns / 2, floor + new Vector3(0, 2.5f, -2), -Vector3.UnitY)) > 0.002f);
        // and a part is not one light everywhere: the brightest place has many times the light of the dim ones
        var sky = v.Parts[GardenLightVolume.Sky]; var up = Enumerable.Range(0, v.X * v.Y * v.Z).Select(p => Lum(new(sky[(p * 6 + 2) * 3], sky[(p * 6 + 2) * 3 + 1], sky[(p * 6 + 2) * 3 + 2]))).ToArray();
        Assert.True(up.Max() > 5 * up.Order().ElementAt(up.Length / 10), "the light volume is nearly uniform");
    }
}
