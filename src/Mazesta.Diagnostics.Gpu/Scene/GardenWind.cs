using System.Diagnostics; using System.Numerics; using System.Runtime.CompilerServices;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The wind of the courtyard as air that moves: a grid of velocities (a "stable fluids" solver, the way games simulate wind and smoke) over the whole garden, stepped every frame on every
/// core. The gusts and the breeze push the air (<see cref="GardenWeather"/>'s fronts are the force); the air is carried along by itself (semi-Lagrangian advection), is slowed in the crowns
/// of trees, and is made to go round what is solid - the hall, its columns, the walls and the ground, found in <see cref="GardenVoxels"/> - by a pressure projection (red-black
/// Gauss-Seidel sweeps over the grid). What the weather and the plants feel is this field (<see cref="Sample"/>): the rain is blown round the hall's corners, a leaf is lifted in the lee of a
/// wall, a bush is rocked by the eddy behind a column.
/// The grid is some two million cells of 16 bytes (tens of megabytes, more than any processor's cache, read at neighbours' distances by the sweeps and at random by the bodies): the sweeps are
/// the memory's bandwidth and the processor's cores as much as arithmetic.
/// </summary>
internal sealed class GardenWind
{
    public float Cell { get; }
    public Vector3 Origin { get; }
    public int X { get; }
    public int Y { get; }
    public int Z { get; }
    private readonly float _inverse; private readonly int _iterations;
    private Vector4[] _v, _t; private readonly float[] _p, _d; private readonly byte[] _solid, _foliage; private readonly Vector2[] _breeze; private readonly float[] _scale; private readonly float[] _slow; private readonly Vector3[] _gusts;
    public long Bytes => (long)_v.Length * 32 + (long)_p.Length * 12 + _solid.Length + _foliage.Length;
    public long Cells => (long)X * Y * Z;
    public long SolidCells { get; }
    /// <summary>Seconds the last <see cref="Step"/> took, on every core.</summary>
    public double LastSeconds { get; private set; }
    /// <summary>The workers that step the air (all the processor's, unless the weather runs beside the renderer).</summary>
    public GardenCrew Crew { get; set; } = GardenCrew.Shared;
    public readonly double[] Phase = new double[8];
    public float Time { get; private set; }
    public bool Valid { get; private set; }

    /// <param name="voxels">What is solid, at a finer grain than <paramref name="cell"/>.</param>
    /// <param name="cell">A cell's side in metres (a column of the hall is a few cells wide).</param>
    /// <param name="iterations">The pressure sweeps a step makes (each one is two passes over the grid).</param>
    public GardenWind(GardenVoxels voxels, float cell, int iterations)
    {
        Cell = cell; _inverse = 1 / cell; _iterations = iterations; Origin = voxels.Origin;
        X = (int)MathF.Ceiling(voxels.X * voxels.Cell / cell); Y = (int)MathF.Ceiling(voxels.Y * voxels.Cell / cell); Z = (int)MathF.Ceiling(voxels.Z * voxels.Cell / cell);
        int n = checked(X * Y * Z);
        _v = new Vector4[n]; _t = new Vector4[n]; _p = new float[n]; _d = new float[n]; _solid = new byte[n]; _foliage = new byte[n]; _breeze = new Vector2[X * Z]; _scale = new float[n]; _slow = new float[Y]; for (int y = 0; y < Y; y++) _slow[y] = GardenWeather.Slowed(Origin.Y + (y + 0.5f) * Cell); _gusts = new Vector3[X * Z];
        int sub = Math.Max(2, (int)MathF.Round(cell / voxels.Cell)); float step = cell / sub; long solid = 0;
        Parallel.For(0, Z, () => 0L, (z, _, count) =>
        {
            for (int y = 0; y < Y; y++)
                for (int x = 0; x < X; x++)
                {
                    int hard = 0, leaves = 0;
                    for (int k = 0; k < sub; k++) for (int j = 0; j < sub; j++) for (int i = 0; i < sub; i++)
                    {
                        float px = Origin.X + x * cell + (i + 0.5f) * step, py = Origin.Y + y * cell + (j + 0.5f) * step, pz = Origin.Z + z * cell + (k + 0.5f) * step;
                        byte kind = voxels.Kind((int)MathF.Floor((px - Origin.X) / voxels.Cell), (int)MathF.Floor((py - Origin.Y) / voxels.Cell), (int)MathF.Floor((pz - Origin.Z) / voxels.Cell));
                        if (kind == GardenVoxels.Hard) hard++; else if (kind == GardenVoxels.Foliage) leaves++;
                    }
                    int at = (z * Y + y) * X + x; bool ground = Origin.Y + (y + 1) * cell <= 0.05f;
                    if (hard > 0 || ground) { _solid[at] = 1; count++; }
                    else _foliage[at] = (byte)Math.Min(255, leaves * 1020 / (sub * sub * sub));   // (a quarter of the cell's volume in leaf is a full crown)
                }
            return count;
        }, count => Interlocked.Add(ref solid, count));
        SolidCells = solid;
        // the sweep's weight of each cell: one over the number of neighbours that are air or the open edge of the grid (0 for a solid cell, which keeps its pressure at 0)
        Parallel.For(0, Z, z =>
        {
            for (int y = 0; y < Y; y++) for (int x = 0; x < X; x++)
            {
                int i = (z * Y + y) * X + x; if (_solid[i] != 0) continue; int open = 0;
                open += x > 0 ? (_solid[i - 1] == 0 ? 1 : 0) : 1; open += x < X - 1 ? (_solid[i + 1] == 0 ? 1 : 0) : 1;
                open += y > 0 ? (_solid[i - X] == 0 ? 1 : 0) : 1; open += y < Y - 1 ? (_solid[i + X] == 0 ? 1 : 0) : 1;
                open += z > 0 ? (_solid[i - X * Y] == 0 ? 1 : 0) : 1; open += z < Z - 1 ? (_solid[i + X * Y] == 0 ? 1 : 0) : 1;
                _scale[i] = open == 0 ? 0 : 1f / open;
            }
        });
    }

    private const float Relax = 3f, CrownDrag = 2.5f, TopSpeed = 22f;

    /// <summary>Starts the air at the wind that blows at <paramref name="time"/>, where it is not blocked (the first frame, or after a jump in time).</summary>
    public void Reset(float time)
    {
        var fronts = GardenWeather.FrontsAt(time); var crew = Crew;
        crew.Run(w =>
        {
            var (z0, z1) = crew.Share(w, Z);
            Columns(z0, z1, fronts, time);
            for (int z = z0; z < z1; z++)
                for (int y = 0; y < Y; y++)
                {
                    float slowed = GardenWeather.Slowed(Origin.Y + (y + 0.5f) * Cell);
                    for (int x = 0; x < X; x++)
                    {
                        int i = (z * Y + y) * X + x, c = z * X + x;
                        _v[i] = _solid[i] != 0 ? default : new Vector4(_breeze[c].X * slowed + _gusts[c].X, _gusts[c].Y, _breeze[c].Y * slowed + _gusts[c].Z, 0);
                        _p[i] = 0;
                    }
                }
        });
        Time = time; Valid = true;
    }

    /// <summary>The wind the weather blows at each column of the garden's plan, for the plan's rows from <paramref name="z0"/> to <paramref name="z1"/> (the same at every height, apart from the ground slowing the breeze).</summary>
    private void Columns(int z0, int z1, in GardenWeather.Fronts fronts, float time)
    {
        for (int z = z0; z < z1; z++)
        {
            float pz = Origin.Z + (z + 0.5f) * Cell;
            for (int x = 0; x < X; x++) GardenWeather.Column(Origin.X + (x + 0.5f) * Cell, pz, fronts, time, out _breeze[z * X + x], out _gusts[z * X + x]);
        }
    }

    public bool Blocked(int x, int y, int z) => _solid[(z * Y + y) * X + x] != 0;
    public Vector3 Velocity(int x, int y, int z) { var v = _v[(z * Y + y) * X + x]; return new(v.X, v.Y, v.Z); }
    /// <summary>The root mean square, over the cells of air, of the divergence the weather does not mean the air to have (what the projection takes out: the smaller, the more the air goes round what is solid instead of into it).</summary>
    public double Divergence()
    {
        double sum = 0; long n = 0; int slab = X * Y;
        for (int z = 1; z < Z - 1; z++) for (int y = 1; y < Y - 1; y++) for (int x = 1; x < X - 1; x++)
        {
            int i = z * slab + y * X + x, c = z * X + x; if (_solid[i] != 0) continue; float sy = _slow[y];
            float weather = (_breeze[c + 1].X - _breeze[c - 1].X) * sy + _gusts[c + 1].X - _gusts[c - 1].X + (_breeze[c + X].Y - _breeze[c - X].Y) * sy + _gusts[c + X].Z - _gusts[c - X].Z;
            float d = 0.5f * (_v[i + 1].X - _v[i - 1].X + _v[i + X].Y - _v[i - X].Y + _v[i + slab].Z - _v[i - slab].Z - weather); sum += d * d; n++;
        }
        return Math.Sqrt(sum / Math.Max(1, n));
    }

    /// <summary>The air at a point, in metres a second (trilinear between the centres of the eight cells round it); false outside the grid.</summary>
    public bool Sample(Vector3 p, out Vector3 wind)
    {
        float fx = (p.X - Origin.X) * _inverse - 0.5f, fy = (p.Y - Origin.Y) * _inverse - 0.5f, fz = (p.Z - Origin.Z) * _inverse - 0.5f;
        if (!Valid || fx < 0 || fy < 0 || fz < 0 || fx >= X - 1 || fy >= Y - 1 || fz >= Z - 1) { wind = default; return false; }
        wind = Trilinear(_v, fx, fy, fz); return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3 Trilinear(Vector4[] field, float fx, float fy, float fz)
    {
        int x0 = (int)fx, y0 = (int)fy, z0 = (int)fz; float tx = fx - x0, ty = fy - y0, tz = fz - z0;
        int a = (z0 * Y + y0) * X + x0, row = X, slab = X * Y;
        var c00 = Vector4.Lerp(field[a], field[a + 1], tx); var c10 = Vector4.Lerp(field[a + row], field[a + row + 1], tx);
        var c01 = Vector4.Lerp(field[a + slab], field[a + slab + 1], tx); var c11 = Vector4.Lerp(field[a + slab + row], field[a + slab + row + 1], tx);
        var r = Vector4.Lerp(Vector4.Lerp(c00, c10, ty), Vector4.Lerp(c01, c11, ty), tz);
        return new(r.X, r.Y, r.Z);
    }

    /// <summary>One step of <paramref name="dt"/> seconds to <paramref name="time"/>, all on the crew: the weather's push and the crowns' drag, advection, then the projection that keeps the air from going through solids.
    /// Each worker has a run of the grid's slices (a whole plan row of cells lives in one slice, so the weather's columns and the force need no barrier between them); the workers meet between passes
    /// that read their neighbours' cells.</summary>
    public void Step(float dt, float time)
    {
        long begin = Stopwatch.GetTimestamp();
        if (!Valid || time < Time || time - Time > 1f) Reset(time);
        var fronts = GardenWeather.FrontsAt(time); float pull = MathF.Min(1, Relax * dt), inverseDt = dt * _inverse; var crew = Crew;
        int nx = X, ny = Y, nz = Z, slab = nx * ny; var solid = _solid; var scale = _scale; var p = _p; var d = _d;
        crew.Run(w =>
        {
            var (z0, z1) = crew.Share(w, nz); long t = Stopwatch.GetTimestamp();
            // 1. the weather pushes the air (it is drawn toward the wind the fronts say blows), the crowns slow it, solids hold it still
            Columns(z0, z1, fronts, time);
            var v = _v;
            for (int z = z0; z < z1; z++)
                for (int y = 0; y < ny; y++)
                {
                    float slowed = GardenWeather.Slowed(Origin.Y + (y + 0.5f) * Cell);
                    for (int x = 0; x < nx; x++)
                    {
                        int i = z * slab + y * nx + x, c = z * nx + x;
                        if (solid[i] != 0) { v[i] = default; continue; }
                        var target = new Vector3(_breeze[c].X * slowed + _gusts[c].X, _gusts[c].Y, _breeze[c].Y * slowed + _gusts[c].Z); var u = v[i]; var current = new Vector3(u.X, u.Y, u.Z);
                        float keep = 1 / (1 + dt * CrownDrag * (_foliage[i] * (1f / 255)));
                        v[i] = new Vector4((current + (target - current) * pull) * keep, 0);
                    }
                }
            crew.Barrier(); if (w == 0) { Phase[1] = Stopwatch.GetElapsedTime(t).TotalMilliseconds; t = Stopwatch.GetTimestamp(); }
            // 2. the air carries itself: each cell takes the velocity of where its air came from (reads its neighbours', writes the other buffer)
            var into = _t;
            for (int z = z0; z < z1; z++)
                for (int y = 0; y < ny; y++)
                    for (int x = 0; x < nx; x++)
                    {
                        int i = z * slab + y * nx + x;
                        if (solid[i] != 0) { into[i] = default; continue; }
                        var u = v[i];
                        float fx = Math.Clamp(x - u.X * inverseDt, 0, nx - 1.001f), fy = Math.Clamp(y - u.Y * inverseDt, 0, ny - 1.001f), fz = Math.Clamp(z - u.Z * inverseDt, 0, nz - 1.001f);
                        into[i] = new Vector4(Trilinear(v, fx, fy, fz), 0);
                    }
            crew.Barrier();
            if (w == 0) { (_v, _t) = (_t, _v); Phase[2] = Stopwatch.GetElapsedTime(t).TotalMilliseconds; t = Stopwatch.GetTimestamp(); }
            crew.Barrier(); v = _v;
            // 3. the divergence the air has that the weather does not mean it to have: the wind the fronts blow is not itself free of divergence (a gust that arrives gathers air), and the projection
            // must not flatten it - only take out what the walls and the crowns do, the air that would pass into a solid. So the divergence of the weather's own wind, as if there were no
            // solids, is taken off the air's (a solid's cell holds no air, but the weather's wind is worked out for it all the same): where nothing is in the way the two cancel exactly
            for (int z = z0; z < z1; z++)
                for (int y = 0; y < ny; y++)
                {
                    float sy = _slow[y];
                    for (int x = 0; x < nx; x++)
                    {
                        int i = z * slab + y * nx + x, c = z * nx + x;
                        if (solid[i] != 0) { d[i] = 0; continue; }
                        int cxm = x > 0 ? c - 1 : c, cxp = x < nx - 1 ? c + 1 : c, czm = z > 0 ? c - nx : c, czp = z < nz - 1 ? c + nx : c;
                        float xm = x > 0 ? v[i - 1].X : v[i].X, xp = x < nx - 1 ? v[i + 1].X : v[i].X, ym = y > 0 ? v[i - nx].Y : v[i].Y, yp = y < ny - 1 ? v[i + nx].Y : v[i].Y, zm = z > 0 ? v[i - slab].Z : v[i].Z, zp = z < nz - 1 ? v[i + slab].Z : v[i].Z;
                        float weather = (_breeze[cxp].X - _breeze[cxm].X) * sy + _gusts[cxp].X - _gusts[cxm].X + (_breeze[czp].Y - _breeze[czm].Y) * sy + _gusts[czp].Z - _gusts[czm].Z;   // (what it blows is the same at every height, the breeze only slowed by the ground: nothing in y)
                        d[i] = 0.5f * (xp - xm + yp - ym + zp - zm - weather);
                    }
                }
            crew.Barrier(); if (w == 0) { Phase[3] = Stopwatch.GetElapsedTime(t).TotalMilliseconds; t = Stopwatch.GetTimestamp(); }
            // 4. the pressure that takes it out again: red-black Gauss-Seidel sweeps, warm from the last step's. p of a solid cell is always 0 and its scale 0, so the six neighbours are simply
            // added, with no test for what is solid in the sweep itself (only at the grid's edges, where the air is open: pressure 0 beyond)
            for (int sweep = 0; sweep < _iterations; sweep++)
                for (int colour = 0; colour < 2; colour++)
                {
                    for (int z = z0; z < z1; z++)
                    {
                        bool zEdge = z == 0 || z == nz - 1;
                        for (int y = 0; y < ny; y++)
                        {
                            bool edge = zEdge || y == 0 || y == ny - 1; int row = z * slab + y * nx;
                            for (int x = (y + z + colour) & 1; x < nx; x += 2)
                            {
                                int i = row + x; float sum;
                                if (edge || x == 0 || x == nx - 1)
                                    sum = (x > 0 ? p[i - 1] : 0) + (x < nx - 1 ? p[i + 1] : 0) + (y > 0 ? p[i - nx] : 0) + (y < ny - 1 ? p[i + nx] : 0) + (z > 0 ? p[i - slab] : 0) + (z < nz - 1 ? p[i + slab] : 0);
                                else sum = p[i - 1] + p[i + 1] + p[i - nx] + p[i + nx] + p[i - slab] + p[i + slab];
                                p[i] = (sum - d[i]) * scale[i];
                            }
                        }
                    }
                    crew.Barrier();
                }
            if (w == 0) { Phase[4] = Stopwatch.GetElapsedTime(t).TotalMilliseconds; t = Stopwatch.GetTimestamp(); }
            // 5. the air loses the pressure's gradient (a solid's own cell counts as the pressure of the cell beside it, so nothing is pushed into it)
            for (int z = z0; z < z1; z++)
                for (int y = 0; y < ny; y++)
                    for (int x = 0; x < nx; x++)
                    {
                        int i = z * slab + y * nx + x;
                        if (solid[i] != 0) continue;
                        float pc = p[i];
                        float xm = x > 0 ? (solid[i - 1] != 0 ? pc : p[i - 1]) : 0, xp = x < nx - 1 ? (solid[i + 1] != 0 ? pc : p[i + 1]) : 0;
                        float ym = y > 0 ? (solid[i - nx] != 0 ? pc : p[i - nx]) : 0, yp = y < ny - 1 ? (solid[i + nx] != 0 ? pc : p[i + nx]) : 0;
                        float zm = z > 0 ? (solid[i - slab] != 0 ? pc : p[i - slab]) : 0, zp = z < nz - 1 ? (solid[i + slab] != 0 ? pc : p[i + slab]) : 0;
                        var u = v[i] - new Vector4(0.5f * (xp - xm), 0.5f * (yp - ym), 0.5f * (zp - zm), 0);
                        float speed2 = u.X * u.X + u.Y * u.Y + u.Z * u.Z;
                        v[i] = speed2 > TopSpeed * TopSpeed ? u * (TopSpeed / MathF.Sqrt(speed2)) : u;   // (no air goes faster than a storm: round a thin edge the projection can ask for more)
                    }
            if (w == 0) Phase[5] = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        });
        Time = time; LastSeconds = Stopwatch.GetElapsedTime(begin).TotalSeconds;
    }
}
