using System.Numerics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// Large solid meshes (the balustrade, the walls' carved faces, the hall's shell) are one instance of a hundred thousand triangles, which per-instance culling cannot thin: whatever of them
/// shows, all of them are drawn. Here the triangles of such a submesh are put in order of where they lie (a Morton curve) and cut into runs of a few thousand, each with a ball
/// round it: a camera draws only the runs it sees (<see cref="GardenRaster"/>). The submesh keeps its place and size in the index buffer; only the order within it changes.
/// </summary>
internal static class GardenChunks
{
    public sealed record Chunk(uint Start, uint Count, Vector3 Centre, float Radius);
    /// <summary>A submesh of at least this many triangles is cut up.</summary>
    public const int MinTriangles = 20000;
    private const int PerChunk = 2048;

    /// <summary>The mesh with the big solid submeshes' triangles put in order, and each such submesh's runs (by its place in <see cref="GardenMesh.Submeshes"/>; starts are within the submesh).</summary>
    public static (GardenMesh Mesh, Dictionary<int, Chunk[]> Chunks)? Split(GardenMesh mesh, Func<uint, bool> isSolid)
    {
        var chunks = new Dictionary<int, Chunk[]>(); var indices = (uint[])mesh.Indices.Clone();
        var pos = new Vector3[mesh.VertexCount]; for (int i = 0; i < pos.Length; i++) pos[i] = mesh.Position(i);
        for (int s = 0; s < mesh.Submeshes.Length; s++)
        {
            var sub = mesh.Submeshes[s]; int tris = (int)sub.IndexCount / 3;
            if (tris < MinTriangles || !isSolid(sub.Material)) continue;
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for (uint k = sub.IndexStart; k < sub.IndexStart + sub.IndexCount; k++) { lo = Vector3.Min(lo, pos[indices[k]]); hi = Vector3.Max(hi, pos[indices[k]]); }
            var span = Vector3.Max(hi - lo, new Vector3(1e-6f));
            var keyed = new (ulong Key, int Tri)[tris];
            for (int t = 0; t < tris; t++)
            {
                uint o = sub.IndexStart + (uint)t * 3; var c = (pos[indices[o]] + pos[indices[o + 1]] + pos[indices[o + 2]]) / 3 - lo;
                keyed[t] = (Morton((uint)Math.Clamp(c.X / span.X * 1023, 0, 1023), (uint)Math.Clamp(c.Y / span.Y * 1023, 0, 1023), (uint)Math.Clamp(c.Z / span.Z * 1023, 0, 1023)), t);
            }
            Array.Sort(keyed, (a, b) => a.Key.CompareTo(b.Key));
            var ordered = new uint[tris * 3];
            for (int t = 0; t < tris; t++) { uint o = sub.IndexStart + (uint)keyed[t].Tri * 3; ordered[t * 3] = indices[o]; ordered[t * 3 + 1] = indices[o + 1]; ordered[t * 3 + 2] = indices[o + 2]; }
            ordered.CopyTo(indices, (int)sub.IndexStart);
            var list = new List<Chunk>();
            for (int first = 0; first < tris; first += PerChunk)
            {
                int n = Math.Min(PerChunk, tris - first); Vector3 clo = new(float.MaxValue), chi = new(float.MinValue);
                for (int k = first * 3; k < (first + n) * 3; k++) { clo = Vector3.Min(clo, pos[ordered[k]]); chi = Vector3.Max(chi, pos[ordered[k]]); }
                var centre = (clo + chi) / 2; float radius = 0;
                for (int k = first * 3; k < (first + n) * 3; k++) radius = MathF.Max(radius, Vector3.Distance(pos[ordered[k]], centre));
                list.Add(new Chunk((uint)first * 3, (uint)n * 3, centre, radius * 1.001f + 1e-4f));
            }
            chunks[s] = [.. list];
        }
        return chunks.Count == 0 ? null : (mesh with { Indices = indices }, chunks);
    }

    private static ulong Morton(uint x, uint y, uint z)
    {
        static ulong Spread(uint v) { ulong a = v & 0x3FF; a = (a | a << 16) & 0x30000FF; a = (a | a << 8) & 0x300F00F; a = (a | a << 4) & 0x30C30C3; return (a | a << 2) & 0x9249249; }
        return Spread(x) | Spread(y) << 1 | Spread(z) << 2;
    }
}
