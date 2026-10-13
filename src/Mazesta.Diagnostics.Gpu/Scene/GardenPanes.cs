using System.Numerics; using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The orsi's stained panes are modelled a little smaller than the openings they sit in: each stands short of the wooden rail above and below it, and between two rows the
/// light and the view go through the gap. Every pane (a flat slab of a tinted glass material, much taller than it is thick) is lengthened at both ends, up and down, so it
/// reaches into the frame - the rails, being nearer the viewer, hide the overlap. Done on the scene as read, so the .blend and its exporter are as the owner made them.
/// </summary>
internal static class GardenPanes
{
    /// <summary>How far each end of a pane grows (metres): a little more than half of the 0.076 m gap between two rows, so neighbours overlap under the rail.</summary>
    internal const float Grow = 0.05f;

    /// <summary>And how far each side grows (the pane's width): the panes stand a finger short of the wooden stiles on both sides, and the gap showed the room behind.</summary>
    internal const float GrowSide = 0.03f;

    /// <summary>The mesh with its panes lengthened (the same mesh when it has none).</summary>
    internal static GardenMesh Close(GardenMesh mesh, GardenMaterial[] materials, out int panes)
    {
        panes = 0; int vc = mesh.VertexCount;
        var tinted = mesh.Submeshes.Where(s => materials[s.Material].LightTint > 0).ToArray(); if (tinted.Length == 0) return mesh;
        var pos = new Vector3[vc]; for (int i = 0; i < vc; i++) pos[i] = mesh.Position(i);
        // faces that touch belong together (a slab's six faces have their own vertices, so touching is by position, not by index)
        var triangles = tinted.SelectMany(s => Enumerable.Range((int)s.IndexStart / 3, (int)s.IndexCount / 3)).ToArray();
        var island = Enumerable.Range(0, vc).ToArray(); int Find(int a) { while (island[a] != a) a = island[a] = island[island[a]]; return a; }
        foreach (int t in triangles) { int a = (int)mesh.Indices[t * 3]; island[Find((int)mesh.Indices[t * 3 + 1])] = Find(a); island[Find((int)mesh.Indices[t * 3 + 2])] = Find(a); }
        var faces = new Dictionary<int, (Vector3 Lo, Vector3 Hi)>();
        foreach (int t in triangles)
            for (int k = 0; k < 3; k++)
            {
                int v = (int)mesh.Indices[t * 3 + k], root = Find(v); var p = pos[v];
                faces[root] = faces.TryGetValue(root, out var b) ? (Vector3.Min(b.Lo, p), Vector3.Max(b.Hi, p)) : (p, p);
            }
        // faces whose boxes overlap are one slab
        var list = faces.ToArray(); var group = Enumerable.Range(0, list.Length).ToArray(); int Top(int a) { while (group[a] != a) a = group[a] = group[group[a]]; return a; }
        var cells = new Dictionary<(int, int, int), List<int>>(); const float Cell = 1f, Slack = 1e-3f;
        for (int i = 0; i < list.Length; i++)
        {
            var (lo, hi) = list[i].Value; var mid = (lo + hi) / 2; var key = ((int)MathF.Floor(mid.X / Cell), (int)MathF.Floor(mid.Y / Cell), (int)MathF.Floor(mid.Z / Cell));
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                if (cells.TryGetValue((key.Item1 + dx, key.Item2 + dy, key.Item3 + dz), out var near))
                    foreach (int j in near) { var o = list[j].Value; if (lo.X <= o.Hi.X + Slack && hi.X >= o.Lo.X - Slack && lo.Y <= o.Hi.Y + Slack && hi.Y >= o.Lo.Y - Slack && lo.Z <= o.Hi.Z + Slack && hi.Z >= o.Lo.Z - Slack) group[Top(i)] = Top(j); }
            if (!cells.TryGetValue(key, out var own)) cells[key] = own = []; own.Add(i);
        }
        var slabs = new Dictionary<int, (Vector3 Lo, Vector3 Hi, List<int> Faces)>();
        for (int i = 0; i < list.Length; i++)
        {
            int g = Top(i); var (lo, hi) = list[i].Value;
            slabs[g] = slabs.TryGetValue(g, out var s) ? (Vector3.Min(s.Lo, lo), Vector3.Max(s.Hi, hi), s.Faces) : (lo, hi, []); slabs[g].Faces.Add(list[i].Key);
        }
        var faceVertices = new Dictionary<int, List<int>>(); foreach (int t in triangles) for (int k = 0; k < 3; k++) { int v = (int)mesh.Indices[t * 3 + k]; int r = Find(v); if (!faceVertices.TryGetValue(r, out var l)) faceVertices[r] = l = []; l.Add(v); }
        foreach (var (lo, hi, members) in slabs.Values)
        {
            var d = hi - lo;   // a pane: thin one way, tall (up) and as wide as a pane is; the stars and strips of the lattice are smaller than this
            if (!(d.Z < 0.03f && d.Y > 0.3f && d.Y < 2.5f && d.X > 0.15f && d.X < 2.5f) && !(d.X < 0.03f && d.Y > 0.3f && d.Y < 2.5f && d.Z > 0.15f && d.Z < 2.5f)) continue;
            panes++;
            bool thinZ = d.Z < 0.03f;   // the slab's thin way: its width runs along the other horizontal axis
            foreach (int root in members) foreach (int v in faceVertices[root])
            {
                if (MathF.Abs(pos[v].Y - lo.Y) < Slack) pos[v].Y = lo.Y - Grow; else if (MathF.Abs(pos[v].Y - hi.Y) < Slack) pos[v].Y = hi.Y + Grow;
                if (thinZ) { if (MathF.Abs(pos[v].X - lo.X) < Slack) pos[v].X = lo.X - GrowSide; else if (MathF.Abs(pos[v].X - hi.X) < Slack) pos[v].X = hi.X + GrowSide; }
                else { if (MathF.Abs(pos[v].Z - lo.Z) < Slack) pos[v].Z = lo.Z - GrowSide; else if (MathF.Abs(pos[v].Z - hi.Z) < Slack) pos[v].Z = hi.Z + GrowSide; }
            }
        }
        return panes == 0 ? mesh : Requantise(mesh, pos);
    }

    /// <summary>The mesh with new positions: its centre and extent are those of the new bounds, and every position is stored again within them.</summary>
    private static GardenMesh Requantise(GardenMesh mesh, Vector3[] pos)
    {
        var lo = pos[0]; var hi = pos[0]; foreach (var p in pos) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
        var centre = (lo + hi) / 2; var extent = (hi - lo) / 2; var vertices = (byte[])mesh.Vertices.Clone();
        for (int i = 0; i < pos.Length; i++)
        {
            var s = MemoryMarshal.Cast<byte, short>(vertices.AsSpan(i * 16, 6)); var q = pos[i] - centre;
            s[0] = Snorm(q.X, extent.X); s[1] = Snorm(q.Y, extent.Y); s[2] = Snorm(q.Z, extent.Z);
        }
        return mesh with { Centre = centre, Extent = extent, Vertices = vertices };
    }

    private static short Snorm(float v, float extent) => extent <= 0 ? (short)0 : (short)Math.Clamp((int)MathF.Round(v / extent * 32767f), -32767, 32767);
}
