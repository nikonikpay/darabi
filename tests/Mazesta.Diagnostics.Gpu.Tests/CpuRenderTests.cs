using System.Numerics; using Xunit;
using Mazesta.Diagnostics.Gpu.Render; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class CpuRenderTests
{
    private static readonly Lazy<RenderScene> Scene = new(() => new RenderScene(GardenScene.Embedded));

    [Fact] public void The_tree_holds_every_box_once_inside_its_leaf()
    {
        var rng = new Random(7); int n = 500; var min = new Vector3[n]; var max = new Vector3[n];
        for (int i = 0; i < n; i++) { min[i] = new(rng.NextSingle() * 10, rng.NextSingle() * 10, rng.NextSingle() * 10); max[i] = min[i] + new Vector3(rng.NextSingle()); }
        var (nodes, order) = Bvh.Build(min, max, 4);
        Assert.Equal(Enumerable.Range(0, n), order.Order());
        int held = 0;
        foreach (var node in nodes.Where(x => x.Count > 0))
            for (int i = node.First; i < node.First + node.Count; i++, held++)
            { Assert.True(Vector3.Min(node.Min, min[order[i]]) == node.Min); Assert.True(Vector3.Max(node.Max, max[order[i]]) == node.Max); }
        Assert.Equal(n, held);
    }

    [Fact] public void The_camera_sees_the_garden_and_the_same_picture_twice()
    {
        var s = Scene.Value;
        Assert.True(s.Triangles > 1_000_000); Assert.True(s.UniqueTriangles <= s.Triangles);
        Assert.Equal(ProgressiveRender.Check(s, 4), ProgressiveRender.Check(s, 1));   // whichever threads drew it
        long rays = 0; uint seed = RenderScene.Seed(0, 0);
        var light = s.Sample(48, 40, 96, 54, ref seed, ref rays);
        Assert.True(rays >= 1); Assert.True(float.IsFinite(light.X) && light.X >= 0);
    }

    [Fact] public void A_run_counts_whole_tiles_and_ends_by_the_clock()
    {
        var r = new ProgressiveRender(Scene.Value, 160, 80);
        r.Run(2, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.True(r.Samples > 0); Assert.Equal(0, r.Samples % (ProgressiveRender.Tile * ProgressiveRender.Tile));
        Assert.True(r.Rays >= r.Samples); Assert.InRange(r.Elapsed.TotalSeconds, 1, 20);
        var pixels = new uint[160 * 80]; r.Show(pixels);
        Assert.Contains(pixels, p => (p & 0xFFFFFF) != 0);
    }

    [Fact] public void As_a_test_every_tile_is_drawn_twice_and_a_wrong_one_is_caught()
    {
        var sound = new ProgressiveRender(Scene.Value, 80, 80, twice: true);
        sound.Run(2, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.True(sound.Checked > 0); Assert.Equal(0, sound.Wrong);
        var spoiled = new ProgressiveRender(Scene.Value, 80, 80, twice: true) { SpoiledTurn = 1 };   // one bit of the second drawing of the first pass
        spoiled.Run(2, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.InRange(spoiled.Wrong, 1, 4);
    }
}
