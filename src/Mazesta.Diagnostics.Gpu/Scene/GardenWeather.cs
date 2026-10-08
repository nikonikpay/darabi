using System.Diagnostics; using System.Numerics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The garden's weather, simulated on the processor every frame the way a game's particle system is: rain that falls round the camera and is carried
/// by the wind, gusts that roll across the courtyard (a front with a swirl and an updraft behind it) and lift leaves and twigs out of the crowns and
/// off the ground, carry them, tumble them and let them settle on the paving, against the walls or on the water. Every body is stepped (drag toward the
/// wind, gravity, tumbling), tested against the garden's real solids (<see cref="GardenVoxels"/>, a grid of tens of megabytes read at random: the
/// work is the processor's speed and the memory's latency), and turned into the rows of a matrix for the card (the renderers' movers). The bodies are
/// independent, so the frame's step is cut into chunks and run on every core, as a game's job system does.
/// <para>A frame shown after another (<c>live</c>) steps the bodies one step of the frame's own length. A frame on its own (the check frames) is a pure
/// function of its time: each body is stepped from the moment its cycle began at a fixed 1/60 s, so the same time is the same picture whatever was drawn
/// before - and what the live frames hold is left as it was.</para>
/// <para>Rain stops where it meets the roof of the hall or a tree's crown; a drop that meets the pool or the ground leaves a splash. Rows are laid out
/// as the renderer's weather instances are: the drops, their splashes, the leaves (three colours, <see cref="LeavesPerKind"/> each), the twigs.</para>
/// </summary>
internal sealed class GardenWeather
{
    private readonly WeatherLevel _level;
    private int Rain => _level.Rain; private int Splashes => _level.Splashes; private int Leaves => _level.Leaves; private int Bodies => _level.Bodies; private int Count => _level.Count;
    /// <summary>The fixed step of a frame on its own, and the longest a body lives (a twig's cycle), which sets how far back a rebuild reaches.</summary>
    private const float Fixed = 1f / 60, LongestCycle = 18.5f;
    private const int Window = (int)(LongestCycle / Fixed) + 4;
    private const float RainPeriod = 1.6f, SplashLife = 0.3f;
    private enum Mode : byte { Flying, Resting, Gone, Floating }

    private readonly GardenVoxels _voxels; private readonly GardenWind? _wind; private readonly float _water; private readonly Vector4 _pool; private readonly Vector4[] _crowns;

    /// <summary>One set of bodies: the live frames' or the check frames'.</summary>
    private sealed class Sim(int bodies, int count)
    {
        public readonly Vector3[] P = new Vector3[bodies], V = new Vector3[bodies], Axis = new Vector3[bodies];
        public readonly Quaternion[] Q = new Quaternion[bodies];
        public readonly Vector4[] Impact = new Vector4[bodies];   // a drop's last place and the moment it met something (w: -1 while it falls)
        public readonly float[] Rate = new float[bodies];
        public readonly int[] Cycle = new int[bodies];
        public readonly Mode[] State = new Mode[bodies];
        public readonly Vector4[] Rows = new Vector4[count * 3];
        public float Time; public bool Valid;
    }
    private readonly Sim _live, _check;

    /// <param name="water">The pool's surface height; <paramref name="pool"/> its plan (x from, z from, x to, z to).</param>
    /// <param name="crowns">Where leaves and twigs come from: the centre and radius of each crown of foliage.</param>
    public GardenWeather(WeatherLevel level, GardenVoxels voxels, float water, Vector4 pool, Vector4[] crowns) { _level = level; _live = new(level.Bodies, level.Count); _check = new(level.Bodies, level.Count); _voxels = voxels; _wind = level.WindCell > 0 ? new GardenWind(voxels, level.WindCell, level.WindIterations) : null; _push = new Vector3[Math.Max(0, level.Bodies - level.Rain)]; _water = water; _pool = pool; _crowns = crowns.Length > 0 ? crowns : [new Vector4(0, 5, -14, 3)]; }

    /// <summary>The rows (three float4s a body, the renderers' mover layout) of the last <see cref="Advance"/>.</summary>
    public ReadOnlySpan<Vector4> Rows => _rows;
    private Vector4[] _rows = [];
    /// <summary>Seconds the last live step took (the bodies' stepping and their rows, on every core).</summary>
    public double LastSeconds { get; private set; }
    public long VoxelBytes => _voxels.Bytes;
    /// <summary>The air the weather and the plants are blown by (null at a level without one, or before the first live frame).</summary>
    public GardenWind? Field => _wind;
    /// <summary>Seconds of the last live step that went on the air (the wind field), the bodies' stepping, and their collisions with one another.</summary>
    public double WindSeconds { get; private set; }
    public double CollideSeconds { get; private set; }
    public long FieldBytes => _wind?.Bytes ?? 0;
    public static int Threads => Environment.ProcessorCount;
    public WeatherLevel Level => _level;

    /// <summary>Steps the air for the frame after the one the card is drawing, while it draws; the next <see cref="Advance"/> (live) takes that step as its own.</summary>
    public void Prefetch(float time) { Advance(time, true); _ahead = true; }
    private bool _ahead;

    public void Advance(float time, bool live)
    {
        long begin = Stopwatch.GetTimestamp();
        if (live && _ahead) { _ahead = false; return; }   // (a frame's time differs from the guess by a few milliseconds: unseen)
        if (live)
        {
            if (!_live.Valid || time < _live.Time || time - _live.Time > 1f) { Rebuild(_live, time); _wind?.Reset(time); WindSeconds = 0; }
            else StepLive(_live, time);
            _rows = _live.Rows; LastSeconds = Stopwatch.GetElapsedTime(begin).TotalSeconds;
        }
        else
        {
            if (!_check.Valid || _check.Time != time) Rebuild(_check, time);   // (the same moment again is the same rows)
            _rows = _check.Rows;
        }
    }

    // ----- hashes and the cycle of a body -----

    private static float H(int body, int salt, int cycle = 0) => GardenGpu.H((uint)body * 0x9E3779B1u + (uint)salt * 0x85EBCA6Bu + (uint)cycle * 0xC2B2AE35u + 0x27D4EB2Fu);
    private float Period(int b) => b < Rain ? RainPeriod : b < Rain + Leaves ? 10f + 5f * H(b, 1) : 12f + 6f * H(b, 1);
    private static float Phase(int b) => H(b, 2);
    private int CycleOf(int b, float time) => (int)MathF.Floor(time / Period(b) - Phase(b));

    // ----- the wind -----

    /// <summary>A gust front: it travels across the courtyard along (<see cref="Dx"/>, <see cref="Dz"/>), <see cref="Place"/> metres from its middle, strongest at the middle of its life.</summary>
    internal struct Front { public float Dx, Dz, Place, Strength; }
    internal struct Fronts { public Front A, B, C; }
    private static readonly float[] FrontPeriod = [6.3f, 9.7f, 14.9f], FrontOffset = [1.1f, 4.3f, 7.9f];
    internal static Fronts FrontsAt(float t)
    {
        Front One(int k)
        {
            float x = (t + FrontOffset[k]) / FrontPeriod[k]; int e = (int)MathF.Floor(x); float u = x - e;
            float a = H(e, 20 + k) * 2 * MathF.PI;   // each front its own way
            return new Front { Dx = MathF.Cos(a), Dz = MathF.Sin(a), Place = -32 + 64 * u, Strength = MathF.Sin(MathF.PI * u) * (0.7f + 0.6f * H(e, 30 + k)) };
        }
        return new Fronts { A = One(0), B = One(1), C = One(2) };
    }
    private static void Add(ref Vector3 w, in Front f, Vector3 p)
    {
        float rz = p.Z + 14, a = p.X * f.Dx + rz * f.Dz - f.Place, l = -p.X * f.Dz + rz * f.Dx;
        float q = a * a * (1f / 36) + l * l * (1f / 200); if (q > 7) return;
        float env = f.Strength * MathF.Exp(-q);
        w += new Vector3(f.Dx, 0, f.Dz) * (9.5f * env) + new Vector3(-f.Dz, 0, f.Dx) * (4.5f * env * MathF.Sin(a * 0.7f + l * 0.25f)) + new Vector3(0, 3.4f * env * MathF.Cos(a * 0.55f + 1.3f), 0);
    }
    /// <summary>What blows over the ground at (<paramref name="x"/>, <paramref name="z"/>) whatever the height: the breeze (before the ground slows it) and the fronts' gusts. Neither depends on y,
    /// so a field of air works this out once for a column of cells.</summary>
    internal static void Column(float x, float z, in Fronts f, float t, out Vector2 breeze, out Vector3 gusts)
    {
        var amb = GardenGpu.Wind(new Vector3(x, 0, z), t); breeze = amb * 4.5f;
        var w = Vector3.Zero; var p = new Vector3(x, 0, z);
        Add(ref w, f.A, p); Add(ref w, f.B, p); Add(ref w, f.C, p);
        gusts = w;
    }
    /// <summary>How much of the breeze reaches a height: it is slower near the ground.</summary>
    internal static float Slowed(float y) => Math.Clamp(0.55f + 0.1125f * y, 0.55f, 1f);
    /// <summary>The wind at a point, in metres a second: the breeze the plants lean to (<see cref="GardenGpu.Wind"/>), slower near the ground, and the gust fronts passing.</summary>
    internal static Vector3 Wind(Vector3 p, in Fronts f, float t)
    {
        Column(p.X, p.Z, f, t, out var breeze, out var gusts); float height = Slowed(p.Y);
        return new Vector3(breeze.X * height, 0, breeze.Y * height) + gusts;
    }

    // ----- spawning and stepping -----

    private bool InPool(Vector3 p) => p.X >= _pool.X && p.X <= _pool.Z && p.Z >= _pool.Y && p.Z <= _pool.W;

    private void Spawn(Sim s, int b, int cycle, float t0)
    {
        s.Cycle[b] = cycle; s.State[b] = Mode.Flying; s.Impact[b] = new(0, 0, 0, -1);
        var eye = GardenCamera.At(MathF.Max(t0, 0)).Eye;
        if (b < Rain)
        {   // round the camera, from over the roofs
            float r = 2 + 17 * MathF.Sqrt(H(b, 3, cycle)), a = 2 * MathF.PI * H(b, 4, cycle);
            s.P[b] = new(eye.X + r * MathF.Cos(a), 13.6f + H(b, 5, cycle), eye.Z + r * MathF.Sin(a)); s.V[b] = new(0, -(8.2f + 1.6f * H(b, 6, cycle)), 0);
            return;
        }
        // a leaf or a twig leaves a crown near the camera (of a few at random, the nearest to the eye)
        Vector4 crown = default; float best = float.MaxValue;
        for (int k = 0; k < 6; k++)
        {
            var c = _crowns[(int)(H(b, 10 + k, cycle) * _crowns.Length) % _crowns.Length]; float dx = c.X - eye.X, dz = c.Z - eye.Z, d = dx * dx + dz * dz;
            if (d < best) { best = d; crown = c; }
            if (d < 26 * 26) break;
        }
        float u = H(b, 40, cycle) * 2 - 1, phi = 2 * MathF.PI * H(b, 41, cycle), ring = MathF.Sqrt(1 - u * u), reach = crown.W * 0.9f * MathF.Cbrt(H(b, 42, cycle));
        var p = new Vector3(crown.X, crown.Y, crown.Z) + new Vector3(ring * MathF.Cos(phi), u, ring * MathF.Sin(phi)) * reach;
        for (int k = 0; k < 24 && _voxels.Solid(p); k++) p.Y += _voxels.Cell;   // (a card of the crown it started in: up to its surface)
        if (_voxels.Solid(p)) s.State[b] = Mode.Gone;
        s.P[b] = p; s.V[b] = Vector3.Zero;
        float ax = H(b, 43, cycle) * 2 - 1, az = H(b, 44, cycle) * 2 - 1, ay = H(b, 45, cycle) * 2 - 1; var axis = new Vector3(ax, ay, az);
        s.Axis[b] = axis.LengthSquared() > 1e-4f ? Vector3.Normalize(axis) : Vector3.UnitX;
        s.Rate[b] = (2.5f + 5f * H(b, 46, cycle)) * (H(b, 47, cycle) < 0.5f ? -1 : 1);
        s.Q[b] = Quaternion.Normalize(new Quaternion(H(b, 48, cycle) - 0.5f, H(b, 49, cycle) - 0.5f, H(b, 50, cycle) - 0.5f, 0.3f + H(b, 51, cycle)));
    }

    /// <summary>The points of a leaf (tip, stalk, edges) and of a twig (its ends) that are tested against the grid, in the body's own space at size 1.</summary>
    private static readonly Vector3[] LeafPoints = [new(0, 0, 0.075f), new(0, 0, -0.075f), new(0.035f, 0, 0), new(-0.035f, 0, 0)], TwigPoints = [new(0, 0, 0.11f), new(0, 0, -0.11f)];

    private static Quaternion Lying(Vector3 up, float yaw)
    {
        var axis = Vector3.Cross(Vector3.UnitY, up); float s = axis.Length(), angle = MathF.Atan2(s, up.Y);
        var align = s < 1e-4f ? (up.Y > 0 ? Quaternion.Identity : new Quaternion(1, 0, 0, 0)) : Quaternion.CreateFromAxisAngle(axis / s, angle);
        return Quaternion.Normalize(align * Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
    }

    /// <summary>The air at a point: the field's where the frame is a live one and the point is inside the grid, the analytic wind of the fronts otherwise.</summary>
    private Vector3 Air(Vector3 p, in Fronts fronts, float t, bool field) => field && _wind is { } f && f.Sample(p, out var w) ? w : Wind(p, fronts, t);

    private void Step(Sim s, int b, float dt, float t, in Fronts fronts, bool field)
    {
        if (s.State[b] == Mode.Gone) return;
        var p = s.P[b]; var v = s.V[b];
        if (b < Rain)
        {   // a drop: pulled toward the wind's sideways speed and its fall speed, then marched along its step, a cell at a time at most, so no wall is stepped through
            var w = Air(p, fronts, t, field); float k = MathF.Min(1, dt * 4);
            v += (new Vector3(w.X * 0.6f, -9.2f, w.Z * 0.6f) - v) * k;
            var from = p; p += v * dt;
            int n = (int)MathF.Ceiling((p - from).Length() / (_voxels.Cell * 0.6f));
            for (int i = 1; i <= n; i++)
            {
                var q = from + (p - from) * ((float)i / n);
                bool water = q.Y <= _water && InPool(q), hit = water || q.Y <= -0.2f || (q.Y < 13.5f && _voxels.Solid(q));
                if (!hit) continue;
                s.Impact[b] = new(q.X, water ? _water : q.Y, q.Z, t); s.State[b] = Mode.Gone; p = q; break;
            }
            s.P[b] = p; s.V[b] = v; return;
        }
        bool leaf = b < Rain + Leaves; float drag = leaf ? 6f + 4f * H(b, 7) : 2.2f + H(b, 7);
        switch (s.State[b])
        {
            case Mode.Resting:
                {   // lying where it came to rest, until a gust strong enough comes by
                    var w = Air(p, fronts, t, field);
                    if (w.X * w.X + w.Z * w.Z > (leaf ? 6f : 9f) * (leaf ? 6f : 9f) * (1 + H(b, 8))) { s.State[b] = Mode.Flying; v = new Vector3(w.X * 0.3f, 1.4f + H(b, 9), w.Z * 0.3f); p.Y += 0.04f; s.V[b] = v; s.P[b] = p; }
                    return;
                }
            case Mode.Floating:
                {   // on the pool, drifting before the wind
                    var w = Air(p, fronts, t, field); p.X += w.X * 0.15f * dt; p.Z += w.Z * 0.15f * dt; p.Y = _water + 0.004f;
                    if (!InPool(p)) { p.Y = _water + 0.4f; s.State[b] = Mode.Flying; }
                    s.P[b] = p; return;
                }
        }
        {
            var w = Air(p, fronts, t, field);
            // a leaf flutters across its fall: the air round it is swirled, faster the faster it falls
            float flutter = leaf ? 0.9f * MathF.Sin(t * (3f + 3f * H(b, 11)) + 6.28f * H(b, 12)) : 0.2f * MathF.Sin(t * 1.5f + b);
            w.X += flutter * (-v.Z * 0.3f + 0.4f); w.Z += flutter * (v.X * 0.3f + 0.3f);
            v = (v + dt * (drag * w + new Vector3(0, -9.81f, 0))) / (1 + dt * drag);   // (the implicit form: stable however large drag * dt)
            var from = p; var move = v * dt; var q = s.Q[b];
            // a body is not a point: a leaf is its tip, its stalk and its two edges, a twig its two ends; each is tested against the grid along the step (a cell and a half at a
            // time at most, so a thin wall is not stepped through), and the one that strikes first turns the body about the axis its lever and the wall make
            var points = leaf ? LeafPoints : TwigPoints; float size = leaf ? 0.8f + 0.5f * H(b, 15) : 0.9f + 0.4f * H(b, 15);
            int steps = Math.Clamp((int)MathF.Ceiling(move.Length() / (_voxels.Cell * 1.5f)), 1, 8), touching = 0; Vector3 normal = default, lever = default;
            for (int k = 1; k <= steps && touching == 0; k++)
            {
                var at = from + move * ((float)k / steps);
                if (_voxels.Solid(at)) { normal += _voxels.Outward(at); touching++; }
                for (int c = 0; c < points.Length; c++)
                {
                    var offset = Vector3.Transform(points[c] * size, q); var corner = at + offset;
                    if (_voxels.Solid(corner)) { normal += _voxels.Outward(corner); lever += offset; touching++; }
                }
                p = touching > 0 ? from + move * ((float)(k - 1) / steps) : at;
            }
            if (touching > 0)
            {
                var n = normal.LengthSquared() > 1e-6f ? Vector3.Normalize(normal) : Vector3.UnitY;
                float vn = Vector3.Dot(v, n);
                if (vn < 0) v -= 1.2f * vn * n;
                v.X *= 1 - MathF.Min(1, dt * 6); v.Z *= 1 - MathF.Min(1, dt * 6);
                var torque = Vector3.Cross(lever / touching, n);
                if (torque.LengthSquared() > 1e-8f) { s.Axis[b] = Vector3.Normalize(torque); s.Rate[b] = (s.Rate[b] < 0 ? -1 : 1) * Math.Clamp(MathF.Abs(vn) * 8f + 2f, 2f, 12f); }
                if (v.LengthSquared() < 0.16f && n.Y > 0.4f) { s.State[b] = Mode.Resting; s.Q[b] = Lying(n, 6.28f * H(b, 13, s.Cycle[b])); v = Vector3.Zero; }
            }
            else if (p.Y <= _water && InPool(p)) { p.Y = _water + 0.004f; v = Vector3.Zero; s.State[b] = Mode.Floating; s.Q[b] = Lying(Vector3.UnitY, 6.28f * H(b, 13, s.Cycle[b])); }
            else if (p.Y < 0) { p.Y = 0.004f; v = Vector3.Zero; s.State[b] = Mode.Resting; s.Q[b] = Lying(Vector3.UnitY, 6.28f * H(b, 13, s.Cycle[b])); }
            if (s.State[b] == Mode.Flying)
            {   // tumbling about its own axis, faster in a stronger flow
                float angle = s.Rate[b] * dt * (1 + 0.25f * (v - w).Length()) * 0.5f;
                var dq = new Quaternion(s.Axis[b] * angle, 1);
                s.Q[b] = Quaternion.Normalize(s.Q[b] * dq);
            }
            s.P[b] = p; s.V[b] = v;
        }
    }

    // ----- rows -----

    private static readonly Vector4 Gone0 = new(1e-4f, 0, 0, 0), Gone1 = new(0, 1e-4f, 0, -100), Gone2 = new(0, 0, 1e-4f, 0);
    private void Put(Sim s, int row) { s.Rows[row * 3] = Gone0; s.Rows[row * 3 + 1] = Gone1; s.Rows[row * 3 + 2] = Gone2; }
    private void Rows3(Sim s, int row, Vector3 x, Vector3 y, Vector3 z, Vector3 at)
    {
        s.Rows[row * 3] = new(x.X, y.X, z.X, at.X); s.Rows[row * 3 + 1] = new(x.Y, y.Y, z.Y, at.Y); s.Rows[row * 3 + 2] = new(x.Z, y.Z, z.Z, at.Z);
    }

    private void WriteRows(Sim s, int b)
    {
        if (b < Rain)
        {
            int row = b;
            if (s.State[b] == Mode.Flying)
            {   // a streak along the way it is going, its head at the drop
                var v = s.V[b]; float speed = v.Length(); var dir = speed > 1e-3f ? v / speed : -Vector3.UnitY;
                var x = Vector3.Cross(dir, MathF.Abs(dir.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX); x = Vector3.Normalize(x); var z = Vector3.Cross(x, dir);
                float length = 0.8f + 0.5f * H(b, 14);
                Rows3(s, row, x, dir * length, z, s.P[b] - dir * (0.2f * length));
            }
            else Put(s, row);
            if (b < Splashes)
            {   // and where it met the pool or the ground, a ring that spreads and thins for a moment
                int sr = Rain + b; var im = s.Impact[b]; float age = s.Time - im.W;
                if (s.State[b] == Mode.Gone && im.W >= 0 && age >= 0 && age < SplashLife)
                {
                    float u = age / SplashLife, radius = 0.012f + 0.11f * u, thick = 0.006f * (1 - u * u);
                    Rows3(s, sr, new(radius, 0, 0), new(0, thick, 0), new(0, 0, radius), new(im.X, im.Y + 0.004f, im.Z));
                }
                else Put(s, sr);
            }
            return;
        }
        int r = b + Splashes;
        if (s.State[b] == Mode.Gone) { Put(s, r); return; }
        {
            bool leaf = b < Rain + Leaves; float size = leaf ? 0.8f + 0.5f * H(b, 15) : 0.9f + 0.4f * H(b, 15);
            var q = s.Q[b]; float xx = q.X * q.X, yy = q.Y * q.Y, zz = q.Z * q.Z, xy = q.X * q.Y, xz = q.X * q.Z, yz = q.Y * q.Z, wx = q.W * q.X, wy = q.W * q.Y, wz = q.W * q.Z;
            // the columns of the rotation (the images of x, y and z), scaled
            var cx = new Vector3(1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy)) * size;
            var cy = new Vector3(2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx)) * size;
            var cz = new Vector3(2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy)) * size;
            Rows3(s, r, cx, cy, cz, s.P[b]);
        }
    }

    // ----- advancing -----

    private void StepLive(Sim s, float time)
    {
        float dt = Math.Clamp(time - s.Time, 1f / 240, 1f / 12); var fronts = FrontsAt(time); s.Time = time;
        long begin = Stopwatch.GetTimestamp();
        _wind?.Step(dt, time); WindSeconds = Stopwatch.GetElapsedTime(begin).TotalSeconds;
        RunChunks(b =>
        {
            int cycle = CycleOf(b, time);
            if (cycle != s.Cycle[b]) Spawn(s, b, cycle, time);
            Step(s, b, dt, time, fronts, true);
        });
        begin = Stopwatch.GetTimestamp(); Collide(s); CollideSeconds = Stopwatch.GetElapsedTime(begin).TotalSeconds;
        RunChunks(b => WriteRows(s, b));
    }

    // ----- bodies against one another -----

    private int[] _starts = [], _cursor = [], _items = [];
    private readonly Vector3[] _push;
    private const float HashCell = 0.3f, BodyRadius = 0.07f;
    private static int CellKey(int x, int y, int z, int mask) => (int)(((uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791)) & (uint)mask);

    /// <summary>Leaves and twigs are not ghosts to one another: a spatial hash (a counting sort of the bodies into cells of 30 cm) finds each flying body's neighbours in the 27 cells round it, and
    /// those that touch are pushed apart, so a gust drives a drift of leaves against a wall into a heap and not into one point. The way a game finds what a particle meets.</summary>
    private void Collide(Sim s)
    {
        int first = Rain, count = Bodies - Rain; if (count <= 0) return;
        int table = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(64, count * 2)), mask = table - 1;
        if (_starts.Length != table + 1) { _starts = new int[table + 1]; _cursor = new int[table]; _items = new int[count]; }
        Array.Clear(_starts); var starts = _starts; var cursor = _cursor; var items = _items; var push = _push; const float Inverse = 1f / HashCell;
        int KeyOf(Vector3 p) => CellKey((int)MathF.Floor(p.X * Inverse), (int)MathF.Floor(p.Y * Inverse), (int)MathF.Floor(p.Z * Inverse), mask);
        RunChunks(count, k => { int b = first + k; if (s.State[b] != Mode.Gone) Interlocked.Increment(ref starts[KeyOf(s.P[b]) + 1]); });
        for (int k = 0; k < table; k++) starts[k + 1] += starts[k];
        Array.Copy(starts, cursor, table);
        RunChunks(count, k => { int b = first + k; if (s.State[b] != Mode.Gone) items[Interlocked.Increment(ref cursor[KeyOf(s.P[b])]) - 1] = k; });
        RunChunks(count, k =>
        {
            int b = first + k; push[k] = default;
            if (s.State[b] != Mode.Flying) return;
            var p = s.P[b]; int cx = (int)MathF.Floor(p.X * Inverse), cy = (int)MathF.Floor(p.Y * Inverse), cz = (int)MathF.Floor(p.Z * Inverse); Vector3 sum = default;
            for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int key = CellKey(cx + dx, cy + dy, cz + dz, mask);
                for (int i = starts[key]; i < starts[key + 1]; i++)
                {
                    int j = first + items[i]; if (j == b) continue;
                    var d = p - s.P[j]; float r2 = d.LengthSquared();
                    if (r2 < 4 * BodyRadius * BodyRadius && r2 > 1e-8f) { float r = MathF.Sqrt(r2); sum += d * ((2 * BodyRadius - r) * 0.5f / r); }
                }
            }
            push[k] = sum;
        });
        RunChunks(count, k =>
        {
            int b = first + k; var d = push[k];
            if (d == default) return;
            var np = s.P[b] + d; if (!_voxels.Solid(np)) { s.P[b] = np; s.V[b] += d * 3f; }
        });
    }

    /// <summary>The bodies as a frame on its own has them: each from the start of its cycle, stepped at the fixed rate to <paramref name="time"/>.</summary>
    private void Rebuild(Sim s, float time)
    {
        int now = (int)MathF.Floor(time / Fixed), first = now - Window + 1; var table = new Fronts[Window];
        for (int k = 0; k < Window; k++) table[k] = FrontsAt((first + k) * Fixed);
        float rest = time - now * Fixed; var last = rest > 1e-5f ? FrontsAt(time) : default;
        s.Time = time;
        RunChunks(b =>
        {
            int cycle = CycleOf(b, time); float born = (cycle + Phase(b)) * Period(b);
            int n0 = Math.Min((int)MathF.Ceiling(born / Fixed), now);
            Spawn(s, b, cycle, n0 * Fixed);
            for (int n = n0; n < now; n++) Step(s, b, Fixed, (n + 1) * Fixed, table[n + 1 - first], false);
            if (rest > 1e-5f) Step(s, b, rest, time, last, false);
            WriteRows(s, b);
        });
        s.Valid = true;
    }

    private void RunChunks(Action<int> body) => RunChunks(Bodies, body);
    private static void RunChunks(int count, Action<int> body)
    {
        const int Chunk = 512;
        Parallel.For(0, (count + Chunk - 1) / Chunk, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, c =>
        {
            for (int b = c * Chunk, end = Math.Min(count, b + Chunk); b < end; b++) body(b);
        });
    }
}

/// <summary>How much weather there is: the numbers of drops, splashes, leaves (of each of three colours) and twigs, and how fine the grid of solids they collide with is. The standard level is about
/// 20,000 bodies against a 29 MB grid; the high level three times the bodies against a 235 MB one - a working set far larger than any processor's cache, so it is the memory's latency that sets the speed.</summary>
internal readonly record struct WeatherLevel(string Name, int Rain, int Splashes, int LeavesPerKind, int Twigs, float Cell, float WindCell, int WindIterations)
{
    public int Leaves => LeavesPerKind * 3; public int Bodies => Rain + Leaves + Twigs; public int Count => Rain + Splashes + Leaves + Twigs;
    public static readonly WeatherLevel Standard = new("on", 14000, 1600, 1200, 700, 0.125f, 0.45f, 10), High = new("high", 42000, 4800, 3600, 2100, 0.0625f, 0.3f, 16);
    /// <summary>The level an option names: null for off.</summary>
    public static WeatherLevel? Parse(string? name) => name switch { "off" => null, "high" => High, _ => Standard };
}
