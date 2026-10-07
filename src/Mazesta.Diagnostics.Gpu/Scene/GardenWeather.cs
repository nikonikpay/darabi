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
    public const int Rain = 14000, Splashes = 1600, LeafKinds = 3, LeavesPerKind = 1200, Twigs = 700;
    public const int Leaves = LeafKinds * LeavesPerKind, Bodies = Rain + Leaves + Twigs, Count = Rain + Splashes + Leaves + Twigs;
    /// <summary>The fixed step of a frame on its own, and the longest a body lives (a twig's cycle), which sets how far back a rebuild reaches.</summary>
    private const float Fixed = 1f / 60, LongestCycle = 18.5f;
    private const int Window = (int)(LongestCycle / Fixed) + 4;
    private const float RainPeriod = 1.6f, SplashLife = 0.3f;
    private enum Mode : byte { Flying, Resting, Gone, Floating }

    private readonly GardenVoxels _voxels; private readonly float _water; private readonly Vector4 _pool; private readonly Vector4[] _crowns;

    /// <summary>One set of bodies: the live frames' or the check frames'.</summary>
    private sealed class Sim
    {
        public readonly Vector3[] P = new Vector3[Bodies], V = new Vector3[Bodies], Axis = new Vector3[Bodies];
        public readonly Quaternion[] Q = new Quaternion[Bodies];
        public readonly Vector4[] Impact = new Vector4[Bodies];   // a drop's last place and the moment it met something (w: -1 while it falls)
        public readonly float[] Rate = new float[Bodies];
        public readonly int[] Cycle = new int[Bodies];
        public readonly Mode[] State = new Mode[Bodies];
        public readonly Vector4[] Rows = new Vector4[Count * 3];
        public float Time; public bool Valid;
    }
    private readonly Sim _live = new(), _check = new();

    /// <param name="water">The pool's surface height; <paramref name="pool"/> its plan (x from, z from, x to, z to).</param>
    /// <param name="crowns">Where leaves and twigs come from: the centre and radius of each crown of foliage.</param>
    public GardenWeather(GardenVoxels voxels, float water, Vector4 pool, Vector4[] crowns) { _voxels = voxels; _water = water; _pool = pool; _crowns = crowns.Length > 0 ? crowns : [new Vector4(0, 5, -14, 3)]; }

    /// <summary>The rows (three float4s a body, the renderers' mover layout) of the last <see cref="Advance"/>.</summary>
    public ReadOnlySpan<Vector4> Rows => _rows;
    private Vector4[] _rows = [];
    /// <summary>Seconds the last live step took (the bodies' stepping and their rows, on every core).</summary>
    public double LastSeconds { get; private set; }
    public long VoxelBytes => _voxels.Bytes;
    public static int Threads => Environment.ProcessorCount;

    public void Advance(float time, bool live)
    {
        long begin = Stopwatch.GetTimestamp();
        if (live)
        {
            if (!_live.Valid || time < _live.Time || time - _live.Time > 1f) Rebuild(_live, time);
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
    private static float Period(int b) => b < Rain ? RainPeriod : b < Rain + Leaves ? 10f + 5f * H(b, 1) : 12f + 6f * H(b, 1);
    private static float Phase(int b) => H(b, 2);
    private static int CycleOf(int b, float time) => (int)MathF.Floor(time / Period(b) - Phase(b));

    // ----- the wind -----

    /// <summary>A gust front: it travels across the courtyard along (<see cref="Dx"/>, <see cref="Dz"/>), <see cref="Place"/> metres from its middle, strongest at the middle of its life.</summary>
    private struct Front { public float Dx, Dz, Place, Strength; }
    private struct Fronts { public Front A, B, C; }
    private static readonly float[] FrontPeriod = [6.3f, 9.7f, 14.9f], FrontOffset = [1.1f, 4.3f, 7.9f];
    private static Fronts FrontsAt(float t)
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
    /// <summary>The wind at a point, in metres a second: the breeze the plants lean to (<see cref="GardenGpu.Wind"/>), slower near the ground, and the gust fronts passing.</summary>
    private static Vector3 Wind(Vector3 p, in Fronts f, float t)
    {
        var amb = GardenGpu.Wind(p, t); var w = new Vector3(amb.X, 0, amb.Y) * 1.4f;
        float height = Math.Clamp(p.Y * 0.5f, 0.35f, 1f); w.X *= height; w.Z *= height;
        Add(ref w, f.A, p); Add(ref w, f.B, p); Add(ref w, f.C, p);
        return w;
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
        for (int k = 0; k < 24 && _voxels.Solid(p); k++) p.Y += GardenVoxels.Cell;   // (a card of the crown it started in: up to its surface)
        if (_voxels.Solid(p)) s.State[b] = Mode.Gone;
        s.P[b] = p; s.V[b] = Vector3.Zero;
        float ax = H(b, 43, cycle) * 2 - 1, az = H(b, 44, cycle) * 2 - 1, ay = H(b, 45, cycle) * 2 - 1; var axis = new Vector3(ax, ay, az);
        s.Axis[b] = axis.LengthSquared() > 1e-4f ? Vector3.Normalize(axis) : Vector3.UnitX;
        s.Rate[b] = (2.5f + 5f * H(b, 46, cycle)) * (H(b, 47, cycle) < 0.5f ? -1 : 1);
        s.Q[b] = Quaternion.Normalize(new Quaternion(H(b, 48, cycle) - 0.5f, H(b, 49, cycle) - 0.5f, H(b, 50, cycle) - 0.5f, 0.3f + H(b, 51, cycle)));
    }

    private static Quaternion Lying(Vector3 up, float yaw)
    {
        var axis = Vector3.Cross(Vector3.UnitY, up); float s = axis.Length(), angle = MathF.Atan2(s, up.Y);
        var align = s < 1e-4f ? (up.Y > 0 ? Quaternion.Identity : new Quaternion(1, 0, 0, 0)) : Quaternion.CreateFromAxisAngle(axis / s, angle);
        return Quaternion.Normalize(align * Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
    }

    private void Step(Sim s, int b, float dt, float t, in Fronts fronts)
    {
        if (s.State[b] == Mode.Gone) return;
        var p = s.P[b]; var v = s.V[b];
        if (b < Rain)
        {   // a drop: pulled toward the wind's sideways speed and its fall speed, then marched along its step, a cell at a time at most, so no wall is stepped through
            var w = Wind(p, fronts, t); float k = MathF.Min(1, dt * 4);
            v += (new Vector3(w.X * 0.6f, -9.2f, w.Z * 0.6f) - v) * k;
            var from = p; p += v * dt;
            int n = (int)MathF.Ceiling((p - from).Length() / (GardenVoxels.Cell * 0.6f));
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
                    var w = Wind(p, fronts, t);
                    if (w.X * w.X + w.Z * w.Z > (leaf ? 6f : 9f) * (leaf ? 6f : 9f) * (1 + H(b, 8))) { s.State[b] = Mode.Flying; v = new Vector3(w.X * 0.3f, 1.4f + H(b, 9), w.Z * 0.3f); p.Y += 0.04f; s.V[b] = v; s.P[b] = p; }
                    return;
                }
            case Mode.Floating:
                {   // on the pool, drifting before the wind
                    var w = Wind(p, fronts, t); p.X += w.X * 0.15f * dt; p.Z += w.Z * 0.15f * dt; p.Y = _water + 0.004f;
                    if (!InPool(p)) { p.Y = _water + 0.4f; s.State[b] = Mode.Flying; }
                    s.P[b] = p; return;
                }
        }
        {
            var w = Wind(p, fronts, t);
            // a leaf flutters across its fall: the air round it is swirled, faster the faster it falls
            float flutter = leaf ? 0.9f * MathF.Sin(t * (3f + 3f * H(b, 11)) + 6.28f * H(b, 12)) : 0.2f * MathF.Sin(t * 1.5f + b);
            w.X += flutter * (-v.Z * 0.3f + 0.4f); w.Z += flutter * (v.X * 0.3f + 0.3f);
            v = (v + dt * (drag * w + new Vector3(0, -9.81f, 0))) / (1 + dt * drag);   // (the implicit form: stable however large drag * dt)
            var from = p; p += v * dt;
            if (_voxels.Solid(p))
            {
                var n = _voxels.Outward(p); p = from;
                float vn = Vector3.Dot(v, n);
                if (vn < 0) v -= (1.2f) * vn * n;
                v.X *= 1 - MathF.Min(1, dt * 6); v.Z *= 1 - MathF.Min(1, dt * 6);
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
        RunChunks(b =>
        {
            int cycle = CycleOf(b, time);
            if (cycle != s.Cycle[b]) Spawn(s, b, cycle, time);
            Step(s, b, dt, time, fronts); WriteRows(s, b);
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
            for (int n = n0; n < now; n++) Step(s, b, Fixed, (n + 1) * Fixed, table[n + 1 - first]);
            if (rest > 1e-5f) Step(s, b, rest, time, last);
            WriteRows(s, b);
        });
        s.Valid = true;
    }

    private static void RunChunks(Action<int> body)
    {
        const int Chunk = 512;
        Parallel.For(0, (Bodies + Chunk - 1) / Chunk, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, c =>
        {
            for (int b = c * Chunk, end = Math.Min(Bodies, b + Chunk); b < end; b++) body(b);
        });
    }
}
