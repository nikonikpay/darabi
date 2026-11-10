using System.Numerics; using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Gpu.Render;

/// <summary>A node of a bounding volume hierarchy: its box, and either its two children (<see cref="Count"/> 0: <see cref="First"/> and the one
/// after it) or the run of <see cref="Count"/> things it holds, from <see cref="First"/> in the builder's order. 32 bytes, two to a cache line.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BvhNode { public Vector3 Min; public int First; public Vector3 Max; public int Count; }

/// <summary>
/// The tree a ray finds its way through the scene by, built the way renderers build theirs: top down, each box cut where the surface area
/// heuristic says a ray is cheapest to send on (the cost of a cut is each side's area times the things in it, tried at twelve places along
/// the box's longest axis). Used twice: over the triangles of each mesh, and over the placed meshes of the scene.
/// </summary>
internal static class Bvh
{
    private const int Bins = 12;

    /// <summary>The tree over boxes <paramref name="min"/>..<paramref name="max"/>, and the order it puts them in (a leaf holds a run of that order).</summary>
    public static (BvhNode[] Nodes, int[] Order) Build(Vector3[] min, Vector3[] max, int leafSize)
    {
        int n = min.Length; var order = new int[n]; for (int i = 0; i < n; i++) order[i] = i;
        var nodes = new BvhNode[Math.Max(1, 2 * n)]; int used = 1;
        if (n == 0) { nodes[0] = new BvhNode { Min = new(float.MaxValue), Max = new(float.MinValue) }; return (nodes[..1], order); }
        var centre = new Vector3[n]; for (int i = 0; i < n; i++) centre[i] = (min[i] + max[i]) * 0.5f;
        var stack = new Stack<(int Node, int From, int To)>(); stack.Push((0, 0, n));
        Span<int> count = stackalloc int[Bins]; Span<Vector3> lo = stackalloc Vector3[Bins], hi = stackalloc Vector3[Bins]; Span<float> rightArea = stackalloc float[Bins];
        while (stack.Count > 0)
        {
            var (at, from, to) = stack.Pop();
            Vector3 bmin = new(float.MaxValue), bmax = new(float.MinValue), cmin = bmin, cmax = bmax;
            for (int i = from; i < to; i++) { int k = order[i]; bmin = Vector3.Min(bmin, min[k]); bmax = Vector3.Max(bmax, max[k]); cmin = Vector3.Min(cmin, centre[k]); cmax = Vector3.Max(cmax, centre[k]); }
            ref var node = ref nodes[at]; node.Min = bmin; node.Max = bmax;
            int items = to - from; var size = cmax - cmin;
            int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2; float extent = size[axis];
            int mid = -1;
            if (items > leafSize && extent > 1e-9f)
            {
                count.Clear(); for (int b = 0; b < Bins; b++) { lo[b] = new(float.MaxValue); hi[b] = new(float.MinValue); }
                float scale = Bins / extent, start = cmin[axis];
                for (int i = from; i < to; i++)
                {
                    int k = order[i], b = Math.Min(Bins - 1, (int)((centre[k][axis] - start) * scale));
                    count[b]++; lo[b] = Vector3.Min(lo[b], min[k]); hi[b] = Vector3.Max(hi[b], max[k]);
                }
                Vector3 rmin = new(float.MaxValue), rmax = new(float.MinValue); int rightCount = 0;
                for (int b = Bins - 1; b > 0; b--) { rmin = Vector3.Min(rmin, lo[b]); rmax = Vector3.Max(rmax, hi[b]); rightCount += count[b]; rightArea[b] = rightCount == 0 ? 0 : Area(rmin, rmax) * rightCount; }
                Vector3 lmin = new(float.MaxValue), lmax = new(float.MinValue); int leftCount = 0, cut = -1; float best = float.MaxValue;
                for (int b = 0; b < Bins - 1; b++)
                {
                    lmin = Vector3.Min(lmin, lo[b]); lmax = Vector3.Max(lmax, hi[b]); leftCount += count[b];
                    if (leftCount == 0 || leftCount == items) continue;
                    float cost = Area(lmin, lmax) * leftCount + rightArea[b + 1];
                    if (cost < best) { best = cost; cut = b; }
                }
                // a leaf of everything costs its area times its count; cut only where that is dearer (a long run is always cut)
                if (cut >= 0 && (items > leafSize * 4 || best < Area(bmin, bmax) * items))
                {
                    int i = from, j = to - 1;
                    while (i <= j)
                    {
                        if (Math.Min(Bins - 1, (int)((centre[order[i]][axis] - start) * scale)) <= cut) i++;
                        else { (order[i], order[j]) = (order[j], order[i]); j--; }
                    }
                    mid = i;
                }
            }
            else if (items > leafSize) mid = from + items / 2;   // every centre in one place: halve the run as it stands
            if (mid <= from || mid >= to) { node.First = from; node.Count = items; continue; }
            node.First = used; node.Count = 0; used += 2;
            stack.Push((node.First, from, mid)); stack.Push((node.First + 1, mid, to));
        }
        return (nodes[..used], order);
    }

    private static float Area(Vector3 min, Vector3 max) { var d = max - min; return d.X * d.Y + d.Y * d.Z + d.Z * d.X; }

    /// <summary>Whether a ray is inside a box anywhere up to <paramref name="tMax"/>, with the distance it enters at.
    /// <paramref name="inv"/>: one over the ray's direction.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static bool Enters(in BvhNode node, Vector3 origin, Vector3 inv, float tMax, out float near)
    {
        Vector3 a = (node.Min - origin) * inv, b = (node.Max - origin) * inv, lo = Vector3.Min(a, b), hi = Vector3.Max(a, b);
        near = MathF.Max(MathF.Max(lo.X, lo.Y), MathF.Max(lo.Z, 0));
        return near <= MathF.Min(MathF.Min(hi.X, hi.Y), MathF.Min(hi.Z, tMax));
    }

    /// <summary>One over a direction, with a part that is nothing made a very small something (a box test then never multiplies nothing by infinity).</summary>
    public static Vector3 Inverse(Vector3 d)
    {
        static float One(float v) => 1 / (MathF.Abs(v) < 1e-12f ? (v < 0 ? -1e-12f : 1e-12f) : v);
        return new(One(d.X), One(d.Y), One(d.Z));
    }
}
