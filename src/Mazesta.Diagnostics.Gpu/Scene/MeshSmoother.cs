using System.Numerics; using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// Smooth subdivision of a garden mesh, done once when the scene is loaded: every triangle becomes four (a level is a quadrupling of the triangles), the
/// new vertex on an edge is moved onto the curved surface the two ends' normals describe (Phong tessellation: the midpoint is pulled toward the tangent
/// planes of both ends), its normal and texture coordinates are the ends' average. A flat face stays flat (its midpoints already lie in the planes of its
/// vertices) and a hard edge stays sharp (each side moves by its own normals), so what changes is the faceting of round things - columns, domes, the
/// fountain, the leaves' edges - which gets smooth, and the work: triangles, vertex and index memory (and the ray tracer's acceleration structure) all
/// grow with it. This is the mesh refined in memory, not the GPU's hardware tessellation stage.
/// </summary>
internal static class MeshSmoother
{
    /// <summary>How far toward the curved surface a new vertex is moved (Phong tessellation's usual 3/4).</summary>
    private const float Alpha = 0.75f;
    /// <summary>The most levels offered (16 times the triangles).</summary>
    public const int MaxLevel = 2;

    public static GardenMesh Subdivide(GardenMesh mesh, int levels)
    {
        for (int level = 0; level < Math.Clamp(levels, 0, MaxLevel); level++) mesh = Once(mesh);
        return mesh;
    }

    private static GardenMesh Once(GardenMesh mesh)
    {
        var vertices = new List<byte>(mesh.Vertices.Length * 3 / 2); vertices.AddRange(mesh.Vertices);
        var indices = new List<uint>(mesh.Indices.Length * 4); var submeshes = new List<GardenSubmesh>(mesh.Submeshes.Length);
        var midpoints = new Dictionary<(uint, uint), uint>(mesh.Indices.Length); uint count = (uint)mesh.VertexCount;
        var record = new byte[16];

        uint Middle(uint a, uint b)
        {
            var key = a < b ? (a, b) : (b, a);   // (a tuple: its hash mixes both numbers; a ^ b of neighbouring indices would collide for whole runs of edges)
            if (midpoints.TryGetValue(key, out uint found)) return found;
            var va = mesh.Vertices.AsSpan((int)a * 16, 16); var vb = mesh.Vertices.AsSpan((int)b * 16, 16);
            Vector3 pa = Position(mesh, va), pb = Position(mesh, vb), na = Normal(va), nb = Normal(vb), mid = (pa + pb) / 2;
            if (Vector3.Dot(na, nb) > 0.3f)
            {
                // Phong: the midpoint's projections onto the two ends' tangent planes, averaged, and the point moved Alpha of the way there.
                Vector3 onA = mid - Vector3.Dot(mid - pa, na) * na, onB = mid - Vector3.Dot(mid - pb, nb) * nb;
                mid += Alpha * ((onA + onB) / 2 - mid);
            }
            Vector3 n = na + nb; n = n.LengthSquared() > 1e-8f ? Vector3.Normalize(n) : na;
            var uv = MemoryMarshal.Cast<byte, Half>(va[12..]); var uw = MemoryMarshal.Cast<byte, Half>(vb[12..]);
            va.CopyTo(record);   // keeps whatever the record's fourth position component holds
            Write(mesh, record, mid, n, (Half)(((float)uv[0] + (float)uw[0]) / 2), (Half)(((float)uv[1] + (float)uw[1]) / 2));
            vertices.AddRange(record);
            return midpoints[key] = count++;
        }

        foreach (var sub in mesh.Submeshes)
        {
            uint start = (uint)indices.Count;
            for (uint i = sub.IndexStart; i < sub.IndexStart + sub.IndexCount; i += 3)
            {
                uint a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2], ab = Middle(a, b), bc = Middle(b, c), ca = Middle(c, a);
                indices.AddRange([a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca]);
            }
            submeshes.Add(sub with { IndexStart = start, IndexCount = (uint)indices.Count - start });
        }
        return mesh with { Vertices = [.. vertices], Indices = [.. indices], Submeshes = [.. submeshes] };
    }

    private static Vector3 Position(GardenMesh mesh, ReadOnlySpan<byte> v)
    {
        var q = MemoryMarshal.Cast<byte, short>(v[..6]);
        return mesh.Centre + mesh.Extent * new Vector3(q[0], q[1], q[2]) / 32767f;
    }

    private static Vector3 Normal(ReadOnlySpan<byte> v)
    {
        var n = new Vector3((sbyte)v[8], (sbyte)v[9], (sbyte)v[10]) / 127f;
        return n.LengthSquared() > 1e-8f ? Vector3.Normalize(n) : Vector3.UnitY;
    }

    private static void Write(GardenMesh mesh, Span<byte> v, Vector3 position, Vector3 normal, Half u, Half w)
    {
        var q = (position - mesh.Centre) / new Vector3(Math.Max(mesh.Extent.X, 1e-9f), Math.Max(mesh.Extent.Y, 1e-9f), Math.Max(mesh.Extent.Z, 1e-9f)) * 32767f;
        var s = MemoryMarshal.Cast<byte, short>(v[..6]);
        s[0] = (short)Math.Clamp(MathF.Round(q.X), -32767, 32767); s[1] = (short)Math.Clamp(MathF.Round(q.Y), -32767, 32767); s[2] = (short)Math.Clamp(MathF.Round(q.Z), -32767, 32767);
        v[8] = (byte)(sbyte)Math.Clamp(MathF.Round(normal.X * 127), -127, 127); v[9] = (byte)(sbyte)Math.Clamp(MathF.Round(normal.Y * 127), -127, 127); v[10] = (byte)(sbyte)Math.Clamp(MathF.Round(normal.Z * 127), -127, 127);
        var t = MemoryMarshal.Cast<byte, Half>(v[12..]); t[0] = u; t[1] = w;
    }
}
