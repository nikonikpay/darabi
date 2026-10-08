using System.Numerics; using System.Runtime.InteropServices; using Xunit; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class MeshSmootherTests
{
    private static byte[] Vertex(Vector3 p, Vector3 n, Vector3 centre, Vector3 extent)
    {
        var v = new byte[16]; var q = (p - centre) / extent * 32767f; var s = MemoryMarshal.Cast<byte, short>(v.AsSpan(0, 6));
        s[0] = (short)MathF.Round(q.X); s[1] = (short)MathF.Round(q.Y); s[2] = (short)MathF.Round(q.Z);
        v[8] = (byte)(sbyte)MathF.Round(n.X * 127); v[9] = (byte)(sbyte)MathF.Round(n.Y * 127); v[10] = (byte)(sbyte)MathF.Round(n.Z * 127);
        return v;
    }
    private static GardenMesh Octahedron(bool roundNormals)
    {
        Vector3[] p = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];
        uint[] idx = [0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5];
        var bytes = p.SelectMany(v => Vertex(v, roundNormals ? v : Vector3.UnitY, Vector3.Zero, new(1.2f))).ToArray();
        return new GardenMesh(Vector3.Zero, new(1.2f), bytes, idx, [new GardenSubmesh(0, 12, 0), new GardenSubmesh(12, 12, 1)]);
    }
    private static Vector3 At(GardenMesh m, int i) => m.Position(i);

    [Fact] public void Each_level_quadruples_the_triangles_and_keeps_every_submesh_in_its_place()
    {
        var one = MeshSmoother.Subdivide(Octahedron(true), 1); var two = MeshSmoother.Subdivide(Octahedron(true), 2);
        Assert.Equal(8 * 4, one.Indices.Length / 3); Assert.Equal(8 * 16, two.Indices.Length / 3);
        Assert.Equal([new GardenSubmesh(0, 48, 0), new GardenSubmesh(48, 48, 1)], one.Submeshes);   // 4 triangles (12 indices) of each become 16 (48)
        Assert.All(one.Indices, i => Assert.True(i < one.VertexCount));
        Assert.Equal(6 + 12, one.VertexCount);   // one new vertex for each shared edge, not one a triangle
    }
    /// <summary>A coarse unit sphere (8 around, 6 from pole to pole) whose vertex normals are the sphere's own.</summary>
    private static GardenMesh Sphere()
    {
        const int seg = 8, rings = 6; var verts = new List<byte>(); var idx = new List<uint>();
        for (int r = 0; r <= rings; r++)
            for (int k = 0; k < seg; k++)
            {
                float a = MathF.PI * r / rings, b = 2 * MathF.PI * k / seg; var p = new Vector3(MathF.Sin(a) * MathF.Cos(b), MathF.Cos(a), MathF.Sin(a) * MathF.Sin(b));
                verts.AddRange(Vertex(p, p, Vector3.Zero, new(1.2f)));
            }
        for (int r = 0; r < rings; r++)
            for (int k = 0; k < seg; k++)
            {
                uint a = (uint)(r * seg + k), b = (uint)(r * seg + (k + 1) % seg), c = a + seg, d = b + seg;
                idx.AddRange([a, c, b, b, c, d]);
            }
        return new GardenMesh(Vector3.Zero, new(1.2f), [.. verts], [.. idx], [new GardenSubmesh(0, (uint)idx.Count, 0)]);
    }

    [Fact] public void Round_things_are_pulled_out_onto_the_curve_and_the_original_corners_stay()
    {
        var sphere = Sphere(); var smooth = MeshSmoother.Subdivide(sphere, 1);
        for (int i = 0; i < sphere.VertexCount; i++) Assert.True(Vector3.Distance(At(sphere, i), At(smooth, i)) < 1e-3f);
        // a flat midpoint of two neighbours 45 degrees apart is 0.92 from the centre; pulled onto the curve it is within a few percent of 1
        for (int i = sphere.VertexCount; i < smooth.VertexCount; i++)
            if (At(smooth, i).Y is > -0.9f and < 0.9f) Assert.InRange(At(smooth, i).Length(), 0.97f, 1.04f);
    }
    [Fact] public void Where_every_normal_is_the_same_the_mesh_is_flat_and_stays_so()
    {
        // a flat square (normals up) cut into triangles: every new vertex lies in the plane of its ends, so none moves off it
        Vector3[] p = [new(-1, 0, -1), new(1, 0, -1), new(1, 0, 1), new(-1, 0, 1)];
        var square = new GardenMesh(Vector3.Zero, new(1.2f), p.SelectMany(v => Vertex(v, Vector3.UnitY, Vector3.Zero, new(1.2f))).ToArray(), [0, 1, 2, 0, 2, 3], [new GardenSubmesh(0, 6, 0)]);
        var smooth = MeshSmoother.Subdivide(square, 2);
        for (int i = 0; i < smooth.VertexCount; i++) Assert.Equal(0f, At(smooth, i).Y, 3);
    }
    [Fact] public void No_smoothing_leaves_the_mesh_as_it_is()
    {
        var m = Octahedron(true);
        Assert.Same(m, MeshSmoother.Subdivide(m, 0));
    }
}
