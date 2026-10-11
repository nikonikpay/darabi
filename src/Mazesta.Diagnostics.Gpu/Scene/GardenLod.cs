using System.Numerics; using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// Levels of detail for plants, made when the scene is loaded: a mesh is cut into its loose pieces (leaves, petals, blades: triangles that share vertices), and of the small ones among its cut-out leaf cards
/// a level keeps a share (a half, a quarter, an eighth, picked by a hash so each level holds the one before), each grown about its own centre by the square root of
/// what was dropped - the foliage keeps its coverage, with a half, a quarter, an eighth of the triangles. Large pieces (a trunk, a branch, a pot) are kept whole.
/// What a camera draws of a plant far from it is such a level, chosen by how large a leaf is on the screen (<see cref="GardenRaster"/>); the ray tracer and the shadow
/// cubes use the whole mesh.
/// </summary>
internal static class GardenLod
{
    /// <summary>The share of the small pieces each level keeps, from the nearest to the farthest.</summary>
    public static readonly float[] Keep = [0.5f, 0.25f, 0.125f];
    /// <summary>A piece is small (may be dropped) when its box is under this share of the mesh's.</summary>
    private const float Small = 0.35f;

    public sealed record Result(GardenMesh[] Levels, float LeafSize);

    /// <summary>The levels of <paramref name="mesh"/> (only what <paramref name="isFoliage"/> says of a submesh's material is thinned: stonework and trunks are not), or null when it has too few small pieces for them to be worth having.</summary>
    public static Result? Build(GardenMesh mesh, Func<uint, bool> isFoliage)
    {
        int vc = mesh.VertexCount, tc = mesh.Indices.Length / 3;
        if (tc < 600) return null;
        var parent = new int[vc]; for (int i = 0; i < vc; i++) parent[i] = i;
        int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
        var idx = mesh.Indices;
        for (int k = 0; k < idx.Length; k += 3) { int a = Find((int)idx[k]); parent[Find((int)idx[k + 1])] = a; parent[Find((int)idx[k + 2])] = Find(a); }
        // each piece's box, its vertices' sum (for the centre it grows about) and count, by its root
        var lo = new Dictionary<int, Vector3>(); var hi = new Dictionary<int, Vector3>(); var sum = new Dictionary<int, Vector3>(); var n = new Dictionary<int, int>();
        var pos = new Vector3[vc]; for (int i = 0; i < vc; i++) pos[i] = mesh.Position(i);
        var used = new bool[vc];
        foreach (uint u in idx) used[u] = true;
        for (int i = 0; i < vc; i++)
        {
            if (!used[i]) continue;
            int r = Find(i); var p = pos[i];
            if (!lo.TryGetValue(r, out var l)) { lo[r] = p; hi[r] = p; sum[r] = p; n[r] = 1; continue; }
            lo[r] = Vector3.Min(l, p); hi[r] = Vector3.Max(hi[r], p); sum[r] += p; n[r]++;
        }
        float whole = mesh.Extent.Length() * 2, limit = whole * Small;
        var foliage = new HashSet<int>();   // the pieces made of leaf cards
        foreach (var sub in mesh.Submeshes) if (isFoliage(sub.Material)) for (uint k = sub.IndexStart; k < sub.IndexStart + sub.IndexCount; k += 3) foliage.Add(Find((int)idx[k]));
        var small = lo.Keys.Where(r => foliage.Contains(r) && (hi[r] - lo[r]).Length() < limit).ToHashSet();
        long smallTris = 0; for (int k = 0; k < idx.Length; k += 3) if (small.Contains(Find((int)idx[k]))) smallTris++;
        if (small.Count < 24 || smallTris < tc * 0.3) return null;
        float leaf = small.Select(r => (hi[r] - lo[r]).Length()).Order().ElementAt(small.Count / 2);

        var levels = new GardenMesh[Keep.Length];
        for (int level = 0; level < Keep.Length; level++)
        {
            float keep = Keep[level], grow = 1 / MathF.Sqrt(keep);
            var newPos = new List<Vector3>(); var raw = new List<byte[]>(); var map = new int[vc]; Array.Fill(map, -1);
            var indices = new List<uint>(); var subs = new List<GardenSubmesh>();
            foreach (var sub in mesh.Submeshes)
            {
                uint start = (uint)indices.Count;
                for (uint k = sub.IndexStart; k < sub.IndexStart + sub.IndexCount; k += 3)
                {
                    int root = Find((int)idx[k]);
                    bool dropped = small.Contains(root) && Hash(root) >= keep;
                    if (dropped) continue;
                    bool grown = small.Contains(root);
                    for (int c = 0; c < 3; c++)
                    {
                        int v = (int)idx[k + c];
                        // a vertex of a grown piece is moved, so it is written for its own level; one of a kept whole piece is shared by the triangles round it
                        if (map[v] < 0)
                        {
                            map[v] = newPos.Count; var p = pos[v];
                            if (grown) { var centre = sum[root] / n[root]; p = centre + (p - centre) * grow; }
                            newPos.Add(p); raw.Add(mesh.Vertices.AsSpan(v * 16, 16).ToArray());
                        }
                        indices.Add((uint)map[v]);
                    }
                }
                subs.Add(new GardenSubmesh(start, (uint)indices.Count - start, sub.Material));
            }
            levels[level] = Encode(newPos, raw, indices, subs);
        }
        return new Result(levels, leaf);
    }

    /// <summary>A piece's hash as a number from 0 up to (not including) 1: a level keeps the pieces under its share, so each holds the one before.</summary>
    private static float Hash(int root) { uint h = (uint)root * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; return (h & 0xFFFFFF) / 16777216f; }

    private static GardenMesh Encode(List<Vector3> pos, List<byte[]> raw, List<uint> indices, List<GardenSubmesh> subs)
    {
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        foreach (var p in pos) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
        if (pos.Count == 0) { lo = hi = Vector3.Zero; }
        var centre = (lo + hi) / 2; var extent = Vector3.Max((hi - lo) / 2 * 1.0001f, new Vector3(1e-4f));
        var bytes = new byte[pos.Count * 16];
        for (int i = 0; i < pos.Count; i++)
        {
            var o = bytes.AsSpan(i * 16, 16); raw[i].CopyTo(o);
            var q = (pos[i] - centre) / extent * 32767f;
            var s = MemoryMarshal.Cast<byte, short>(o[..6]);
            s[0] = (short)Math.Clamp(MathF.Round(q.X), -32767, 32767); s[1] = (short)Math.Clamp(MathF.Round(q.Y), -32767, 32767); s[2] = (short)Math.Clamp(MathF.Round(q.Z), -32767, 32767);
        }
        return new GardenMesh(centre, extent, bytes, [.. indices], [.. subs]);
    }
}
