using System.Numerics; using System.Runtime.InteropServices; using Xunit;
using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class GardenPanesTests
{
    private static readonly GardenMaterial[] Tinted = [new() { Kind = GardenMaterialKind.Glass, LightTint = 1 }];

    /// <summary>Slabs as the exporter writes them: every face its own four vertices.</summary>
    private static GardenMesh Slabs(params (Vector3 Lo, Vector3 Hi)[] boxes)
    {
        var pos = new List<Vector3>(); var idx = new List<uint>();
        foreach (var (lo, hi) in boxes)
            for (int axis = 0; axis < 3; axis++)
                foreach (bool far in new[] { false, true })
                {
                    uint at = (uint)pos.Count; int u = (axis + 1) % 3, v = (axis + 2) % 3;
                    foreach (var (a, b) in new[] { (0, 0), (1, 0), (1, 1), (0, 1) })
                    {
                        var p = new float[3]; p[axis] = far ? hi[axis] : lo[axis]; p[u] = a == 0 ? lo[u] : hi[u]; p[v] = b == 0 ? lo[v] : hi[v]; pos.Add(new(p[0], p[1], p[2]));
                    }
                    idx.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
                }
        var min = pos.Aggregate(Vector3.Min); var max = pos.Aggregate(Vector3.Max); var centre = (min + max) / 2; var extent = (max - min) / 2; var bytes = new byte[pos.Count * 16];
        for (int i = 0; i < pos.Count; i++)
        {
            var s = MemoryMarshal.Cast<byte, short>(bytes.AsSpan(i * 16, 6)); var q = (pos[i] - centre) / extent * 32767f;
            s[0] = (short)MathF.Round(q.X); s[1] = (short)MathF.Round(q.Y); s[2] = (short)MathF.Round(q.Z);
        }
        return new GardenMesh(centre, extent, bytes, [.. idx], [new GardenSubmesh(0, (uint)idx.Count, 0)]);
    }

    private static (float Lo, float Hi) Height(GardenMesh m) { var ys = Enumerable.Range(0, m.VertexCount).Select(i => m.Position(i).Y).ToArray(); return (ys.Min(), ys.Max()); }

    [Fact] public void A_pane_grows_at_both_ends_so_it_reaches_its_frame()
    {
        var mesh = Slabs((new(0, 2.128f, -3.115f), new(0.385f, 2.765f, -3.107f)));
        var closed = GardenPanes.Close(mesh, Tinted, out int panes); var (lo, hi) = Height(closed);
        Assert.Equal(1, panes); Assert.Equal(2.128f - GardenPanes.Grow, lo, 3); Assert.Equal(2.765f + GardenPanes.Grow, hi, 3);
        // the width grows at both sides by GrowSide; the thickness is as it was
        var xs = Enumerable.Range(0, closed.VertexCount).Select(i => closed.Position(i).X).ToArray(); Assert.Equal(0.385f + 2 * GardenPanes.GrowSide, xs.Max() - xs.Min(), 3);
    }

    [Fact] public void Two_rows_with_a_gap_between_them_meet_under_the_rail_and_a_small_star_is_left_alone()
    {
        var rows = Slabs((new(0, 2.128f, -3.115f), new(0.385f, 2.765f, -3.107f)), (new(0, 2.84f, -3.115f), new(0.385f, 3.48f, -3.107f)));
        var closed = GardenPanes.Close(rows, Tinted, out int panes); Assert.Equal(2, panes);
        float lowTop = 2.765f + GardenPanes.Grow, highBottom = 2.84f - GardenPanes.Grow; Assert.True(lowTop > highBottom);   // overlap: nothing to see through
        var star = Slabs((new(0, 0, 0), new(0.23f, 0.23f, 0.07f)));
        Assert.Same(star, GardenPanes.Close(star, Tinted, out int none)); Assert.Equal(0, none);
    }

    [Fact] public void A_mesh_with_no_tinted_glass_is_untouched()
    {
        var mesh = Slabs((new(0, 2.128f, -3.115f), new(0.385f, 2.765f, -3.107f)));
        Assert.Same(mesh, GardenPanes.Close(mesh, [new() { Kind = GardenMaterialKind.Flat }], out _));
    }

    [Fact] public void The_embedded_garden_has_panes_that_were_lengthened()
    {
        // a pane of the hall's wall was 0.637 m tall; after the garden is read it is that and a hand more at each end
        var tall = GardenScene.Embedded.Meshes.SelectMany(m => m.Submeshes.Where(s => GardenScene.Embedded.Materials[s.Material].LightTint > 0).Select(_ => m)).Distinct()
            .Any(m => Enumerable.Range(0, m.VertexCount).Select(i => m.Position(i).Y).Distinct().Any(y => MathF.Abs(y - (2.128f - GardenPanes.Grow)) < 0.003f));
        Assert.True(tall);
    }
}
