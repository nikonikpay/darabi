using System.Numerics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// What is solid in the garden, as a grid of cells (a voxel occupancy volume), worked out once from the scene's own triangles: every wall, column,
/// roof, tile, leaf card and branch marks the cells it touches. It is what the weather collides with (<see cref="GardenWeather"/>): a raindrop stops
/// at the first cell it meets - so it does not fall through the hall's roof or a tree's crown - and a leaf blown against a wall slides along it.
/// Games do the same with distance fields or voxels for particles, because testing a particle against millions of triangles is out of the question and a
/// lookup in a grid is one read; the grid is tens of megabytes, larger than the processor's cache, so the reads are the memory's latency as much as the
/// processor's speed. Built from the scene's still instances (the moving ones - logo, mirror sphere, droplets, small life - are not obstacles) and the
/// water is not a solid (the pool's surface is a height the weather knows).
/// </summary>
internal sealed class GardenVoxels
{
    /// <summary>A cell's side, in metres: the leaf cards and the thinnest rails of the scene are a cell or less thick, so they mark one cell.</summary>
    public float Cell { get; }
    public Vector3 Origin { get; } public int X { get; } public int Y { get; } public int Z { get; }
    private readonly byte[] _cells; private readonly float _inverse;
    public long Bytes => _cells.LongLength;
    public long Filled { get; private set; }

    private GardenVoxels(Vector3 low, Vector3 high, float cell)
    {
        Cell = cell; _inverse = 1 / cell; Origin = low; X = (int)MathF.Ceiling((high.X - low.X) / Cell) + 1; Y = (int)MathF.Ceiling((high.Y - low.Y) / Cell) + 1; Z = (int)MathF.Ceiling((high.Z - low.Z) / Cell) + 1;
        _cells = new byte[(long)X * Y * Z];
    }

    /// <summary>Whether the point is inside something solid (false outside the grid, which covers the garden and the trees round it).</summary>
    public bool Solid(Vector3 p) => Solid(p.X, p.Y, p.Z);
    public bool Solid(float x, float y, float z)
    {
        float fx = (x - Origin.X) * _inverse, fy = (y - Origin.Y) * _inverse, fz = (z - Origin.Z) * _inverse;
        if (fx < 0 || fy < 0 || fz < 0) return false;
        int ix = (int)fx, iy = (int)fy, iz = (int)fz;
        return ix < X && iy < Y && iz < Z && _cells[((long)iz * Y + iy) * X + ix] != 0;
    }

    /// <summary>The way out of the solid at a point: away from the solid cells round it (a unit vector; straight up where it is boxed in evenly).</summary>
    public Vector3 Outward(Vector3 p)
    {
        float s = Cell * 1.5f;
        var n = new Vector3((Solid(p.X - s, p.Y, p.Z) ? 1 : 0) - (Solid(p.X + s, p.Y, p.Z) ? 1 : 0), (Solid(p.X, p.Y - s, p.Z) ? 1 : 0) - (Solid(p.X, p.Y + s, p.Z) ? 1 : 0), (Solid(p.X, p.Y, p.Z - s) ? 1 : 0) - (Solid(p.X, p.Y, p.Z + s) ? 1 : 0));
        return n.LengthSquared() < 1e-6f ? Vector3.UnitY : Vector3.Normalize(n);
    }

    /// <summary>What fills the cell at a point: 0 nothing, <see cref="Hard"/> a wall, floor, rail or other solid, <see cref="Foliage"/> only the cards of a crown (which air passes through).</summary>
    public byte Kind(int ix, int iy, int iz) => (uint)ix < (uint)X && (uint)iy < (uint)Y && (uint)iz < (uint)Z ? _cells[((long)iz * Y + iy) * X + ix] : (byte)0;
    public const byte Hard = 1, Foliage = 2;

    private void Mark(Vector3 p, byte kind)
    {
        int ix = (int)MathF.Floor((p.X - Origin.X) * _inverse), iy = (int)MathF.Floor((p.Y - Origin.Y) * _inverse), iz = (int)MathF.Floor((p.Z - Origin.Z) * _inverse);
        if ((uint)ix < (uint)X && (uint)iy < (uint)Y && (uint)iz < (uint)Z)
        {   // (threads may share a cell: a solid always wins over foliage, whichever came first, so the grid is the same every time)
            ref byte cell = ref _cells[((long)iz * Y + iy) * X + ix];
            if (kind == Hard) cell = Hard; else Interlocked.CompareExchange(ref cell, Foliage, 0);
        }
    }

    /// <summary>A triangle's cells: its corners and, where it is larger than a cell, points across it no further apart than half of one.</summary>
    private void Mark(Vector3 a, Vector3 b, Vector3 c, byte kind)
    {
        float longest = MathF.Sqrt(MathF.Max(MathF.Max((b - a).LengthSquared(), (c - b).LengthSquared()), (a - c).LengthSquared()));
        int n = (int)MathF.Ceiling(longest / (Cell * 0.5f));
        if (n <= 1) { Mark(a, kind); Mark(b, kind); Mark(c, kind); Mark((a + b + c) / 3, kind); return; }
        var ab = (b - a) / n; var ac = (c - a) / n;
        for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n - i; j++) Mark(a + ab * i + ac * j, kind);
    }

    private static readonly Dictionary<(ulong, float), GardenVoxels> Kept = []; private static readonly object Lock = new();
    /// <summary>The grid of <paramref name="scene"/> at cells of <paramref name="cell"/> metres, built once and kept (the scene is one for the whole run).</summary>
    public static GardenVoxels For(GardenScene scene, float cell = 0.125f)
    {
        lock (Lock)
        {
            if (Kept.TryGetValue((scene.Stamp, cell), out var kept)) return kept;
            Kept.Clear(); return Kept[(scene.Stamp, cell)] = Build(scene, cell);   // (one at a time: a finer grid is hundreds of megabytes)
        }
    }

    /// <summary>Which of the scene's materials are things in the way of the weather: not the water (a surface, not an obstacle) nor smoke.</summary>
    private static bool Obstacle(GardenMaterialKind kind) => kind is GardenMaterialKind.Flat or GardenMaterialKind.Cutout or GardenMaterialKind.Brick or GardenMaterialKind.Glass or GardenMaterialKind.Emissive;

    public static GardenVoxels Build(GardenScene scene, float cell = 0.125f)
    {
        const uint Moving = GardenScene.LogoFlag | GardenScene.SphereFlag | GardenScene.DropletFlag | GardenScene.MoverFlag;
        var instances = scene.Instances.Where(i => (i.Mask & (uint)GardenScene.Mode.Raster) != 0 && (i.Flags & Moving) == 0).ToArray();
        // each mesh's vertices in its own space, decoded once
        var vertices = new Vector3[scene.Meshes.Count][];
        Parallel.For(0, vertices.Length, m => { var mesh = scene.Meshes[m]; var v = new Vector3[mesh.VertexCount]; for (int k = 0; k < v.Length; k++) v[k] = mesh.Position(k); vertices[m] = v; });
        // the grid covers where the scene stands (the courtyard and the trees round it), capped: what is further is no obstacle to anything the weather does near the garden
        Vector3 low = new(float.MaxValue), high = new(float.MinValue);
        foreach (var inst in instances)
        {
            var mesh = scene.Meshes[(int)inst.Mesh]; var c = inst.Apply(mesh.Centre); float r = mesh.Extent.Length() * MathF.Max(new Vector3(inst.Row0.X, inst.Row1.X, inst.Row2.X).Length(), MathF.Max(new Vector3(inst.Row0.Y, inst.Row1.Y, inst.Row2.Y).Length(), new Vector3(inst.Row0.Z, inst.Row1.Z, inst.Row2.Z).Length()));
            low = Vector3.Min(low, c - new Vector3(r)); high = Vector3.Max(high, c + new Vector3(r));
        }
        low = Vector3.Max(low, new Vector3(-30, -0.5f, -48)); high = Vector3.Min(high, new Vector3(30, 14, 20));
        var grid = new GardenVoxels(low, high, cell);
        Parallel.For(0, instances.Length, k =>
        {
            var inst = instances[k]; var mesh = scene.Meshes[(int)inst.Mesh]; var local = vertices[(int)inst.Mesh];
            var world = new Vector3[local.Length]; for (int v = 0; v < world.Length; v++) world[v] = inst.Apply(local[v]);
            foreach (var sub in mesh.Submeshes)
            {
                var material = scene.Materials[sub.Material].Kind; if (!Obstacle(material)) continue;
                byte kind = material == GardenMaterialKind.Cutout ? Foliage : Hard;
                for (uint i = sub.IndexStart; i < sub.IndexStart + sub.IndexCount; i += 3) grid.Mark(world[mesh.Indices[i]], world[mesh.Indices[i + 1]], world[mesh.Indices[i + 2]], kind);
            }
        });
        long filled = 0; foreach (byte b in grid._cells) if (b != 0) filled++; grid.Filled = filled;
        return grid;
    }
}
