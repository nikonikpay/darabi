using System.Diagnostics; using System.Numerics; using Xunit; using Xunit.Abstractions; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

/// <summary>The garden's weather on the processor: the grid of solids it collides with, and the rain, leaves and twigs stepped through it. No card is needed.</summary>
public class GardenWeatherTests(ITestOutputHelper output)
{
    private static GardenWeather Weather()
    {
        var scene = GardenScene.Embedded; var voxels = GardenVoxels.For(scene);
        var raster = scene.Instances.Where(i => (i.Mask & (uint)GardenScene.Mode.Raster) != 0 && (i.Flags & (GardenScene.LogoFlag | GardenScene.SphereFlag)) == 0).ToArray();
        (Vector3 Centre, float Radius) Ball(GardenInstance i) { var m = scene.Meshes[(int)i.Mesh]; return (i.Apply(m.Centre), m.Extent.Length() * MathF.Sqrt(MathF.Max(new Vector3(i.Row0.X, i.Row1.X, i.Row2.X).LengthSquared(), MathF.Max(new Vector3(i.Row0.Y, i.Row1.Y, i.Row2.Y).LengthSquared(), new Vector3(i.Row0.Z, i.Row1.Z, i.Row2.Z).LengthSquared())))); }
        bool Has(GardenInstance i, GardenMaterialKind kind) => scene.Meshes[(int)i.Mesh].Submeshes.Any(s => scene.Materials[s.Material].Kind == kind);
        var water = raster.Where(i => Has(i, GardenMaterialKind.Water) && (i.Flags & GardenScene.DropletFlag) == 0).Select(Ball).ToArray();
        var pool = new Vector4(water.Min(b => b.Centre.X - b.Radius), water.Min(b => b.Centre.Z - b.Radius), water.Max(b => b.Centre.X + b.Radius), water.Max(b => b.Centre.Z + b.Radius));
        var crowns = raster.Where(i => Has(i, GardenMaterialKind.Cutout)).Select(Ball).Where(b => b.Centre.Y > 1.6f && b.Radius is > 0.25f and < 7f).Select(b => new Vector4(b.Centre, b.Radius)).ToArray();
        return new GardenWeather(voxels, scene.WaterLevel, pool, crowns);
    }

    [Fact] public void The_grid_holds_the_garden_s_walls_and_roof_but_not_its_air()
    {
        var v = GardenVoxels.For(GardenScene.Embedded);
        output.WriteLine($"grid {v.X} x {v.Y} x {v.Z} cells of {GardenVoxels.Cell} m, {v.Bytes / 1048576.0:F1} MB, {v.Filled:N0} filled ({100.0 * v.Filled / v.Bytes:F2} %)");
        Assert.True(v.Bytes > 4_000_000);                                  // larger than a processor's cache: its reads are the memory's
        Assert.InRange(100.0 * v.Filled / v.Bytes, 0.2, 40);
        Assert.False(v.Solid(0, 12.9f, -14));                              // the open sky over the courtyard
        Assert.False(v.Solid(1000, 1, 1000));                              // outside the grid
        // straight up from the hall's floor there is a roof somewhere (the rain must stop on it)
        bool roof = false; for (float y = 1.5f; y < 13; y += GardenVoxels.Cell) roof |= v.Solid(0, y, 3);
        Assert.True(roof);
    }

    [Fact] public void No_drop_falls_through_the_hall_s_roof_or_ends_inside_a_wall()
    {
        var w = Weather(); var v = GardenVoxels.For(GardenScene.Embedded);
        foreach (float t in new[] { 2f, 9.5f }) w.Advance(t, live: false);
        var rows = w.Rows; int flying = 0, inside = 0, underRoof = 0;
        for (int b = 0; b < GardenWeather.Rain; b++)
        {
            if (rows[b * 3 + 1].W < -50) continue;                                // spent (put far under the ground)
            var at = new Vector3(rows[b * 3].W, rows[b * 3 + 1].W, rows[b * 3 + 2].W); flying++;
            if (v.Solid(at)) inside++;
            if (at.X is > -8 and < 8 && at.Z is > 0.5f and < 6 && at.Y is > 1.5f and < 5) underRoof++;   // in the hall's rooms
        }
        output.WriteLine($"{flying} drops falling, {inside} in a solid, {underRoof} in the hall's rooms");
        Assert.True(flying > GardenWeather.Rain / 3);
        Assert.True(inside < flying / 200);                                   // (a step's last point may touch the surface it meets)
        Assert.Equal(0, underRoof);
    }

    [Fact] public void A_frame_on_its_own_is_a_pure_function_of_its_time()
    {
        var a = Weather(); var b = Weather();
        a.Advance(3.3f, false); a.Advance(40f, false); b.Advance(40f, false);
        Assert.True(a.Rows.SequenceEqual(b.Rows));
        a.Advance(3.3f, true); a.Advance(3.34f, true); a.Advance(40f, false);   // the frames shown one after another do not touch it
        Assert.True(a.Rows.SequenceEqual(b.Rows));
    }

    [Fact] public void Leaves_are_carried_by_the_gusts_and_come_to_rest()
    {
        var w = Weather(); const int first = GardenWeather.Rain + GardenWeather.Splashes, n = GardenWeather.Leaves;
        var t0 = Stopwatch.GetTimestamp(); double slowest = 0;
        w.Advance(0, true); var before = w.Rows.Slice(first * 3, n * 3).ToArray();
        for (float t = 1f / 60; t <= 8; t += 1f / 60) { w.Advance(t, true); slowest = Math.Max(slowest, w.LastSeconds); }
        var after = w.Rows.Slice(first * 3, n * 3).ToArray();
        int moved = 0; for (int i = 0; i < n; i++) if (Math.Abs(after[i * 3].W - before[i * 3].W) + Math.Abs(after[i * 3 + 2].W - before[i * 3 + 2].W) > 1f) moved++;
        output.WriteLine($"8 s in {Stopwatch.GetElapsedTime(t0).TotalSeconds:F1} s on {GardenWeather.Threads} threads, slowest step {slowest * 1000:F2} ms; {moved} of {n} leaves moved more than a metre sideways");
        Assert.True(moved > n / 10);
        Assert.All(after, r => Assert.True(float.IsFinite(r.X) && float.IsFinite(r.W)));
    }
}
