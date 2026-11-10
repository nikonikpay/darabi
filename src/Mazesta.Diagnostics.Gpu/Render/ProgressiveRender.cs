using System.Diagnostics; using System.Numerics; using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Gpu.Render;

/// <summary>
/// A picture rendered progressively, as V-Ray's and Corona's benchmarks do it: every pass gives each pixel one more path, the whole picture
/// is there (grainy) after the first pass and clears with each one after, and the work is cut off by the clock, not by the picture being
/// done - so a slow and a fast processor both run the same length and the count of what they got through is the result.
/// The picture is cut into tiles the threads take one after another from a shared counter (tile n of pass n / tiles), so every thread always
/// has work and none waits on another. A pixel's path in a pass starts from a number made of the pixel and the pass alone
/// (<see cref="RenderScene.Seed"/>): which thread drew it changes nothing.
/// <para>As a test (<c>twice</c>) every pass of a tile is drawn two times, one after the other and as a rule by two different threads, and
/// the two must be the same bits: a processor that computes wrongly under this load is caught at the tile it got wrong
/// (<see cref="Wrong"/> of <see cref="Checked"/>). The picture then clears half as fast, which a test does not mind.</para>
/// </summary>
internal sealed class ProgressiveRender
{
    public const int Tile = 40;
    private readonly RenderScene _scene; private readonly int _width, _height, _across, _tiles;
    private readonly float[] _sum; private readonly int[] _passes; private readonly object[] _locks;
    private readonly bool _twice; private readonly ulong[] _mark; private readonly long[] _markTurn;
    private long _next, _rays, _samples, _wrong, _checked;
    /// <summary>For the unit test of the check itself: the turn whose result is spoiled by one bit (a healthy processor never gets one wrong).</summary>
    internal long SpoiledTurn = -1;

    public ProgressiveRender(RenderScene scene, int width, int height, bool twice = false)
    {
        _scene = scene; _width = width; _height = height; _twice = twice; _across = (width + Tile - 1) / Tile; _tiles = _across * ((height + Tile - 1) / Tile);
        _sum = new float[width * height * 3]; _passes = new int[_tiles]; _locks = [.. Enumerable.Range(0, _tiles).Select(_ => new object())];
        _mark = new ulong[_tiles]; _markTurn = new long[_tiles]; Array.Fill(_markTurn, -1L);
    }

    /// <summary>Tiles drawn a second time and compared with the first, and how many of them differed.</summary>
    public long Checked => Interlocked.Read(ref _checked);
    public long Wrong => Interlocked.Read(ref _wrong);

    /// <summary>Rays sent and paths finished (one a pixel a pass) so far, counted as each tile is done.</summary>
    public long Rays => Interlocked.Read(ref _rays);
    public long Samples => Interlocked.Read(ref _samples);
    /// <summary>How many times over the whole picture has been drawn (2.5: two passes and half of the third).</summary>
    public double Passes => Samples / (double)(_width * _height);
    /// <summary>How long <see cref="Run"/> took, from its threads' start to the last one's end.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>Renders on <paramref name="threads"/> threads until <paramref name="length"/> is up or <paramref name="ct"/> is cancelled; a tile
    /// begun is finished, so every counted sample is a whole one.</summary>
    public void Run(int threads, TimeSpan length, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        void Work()
        {
            while (!ct.IsCancellationRequested && sw.Elapsed < length)
            {
                long n = Interlocked.Increment(ref _next) - 1, turn = n / _tiles; int tile = (int)(n % _tiles), pass = (int)(_twice ? turn / 2 : turn);
                int x0 = tile % _across * Tile, y0 = tile / _across * Tile, x1 = Math.Min(x0 + Tile, _width), y1 = Math.Min(y0 + Tile, _height); long rays = 0; ulong bits = 1469598103934665603UL;
                lock (_locks[tile])   // a tile is one thread's at a time (two passes of it never meet unless a thread was held up for a whole pass)
                {
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            int pixel = y * _width + x; uint seed = RenderScene.Seed(pixel, pass);
                            var c = _scene.Sample(x, y, _width, _height, ref seed, ref rays);
                            _sum[pixel * 3] += c.X; _sum[pixel * 3 + 1] += c.Y; _sum[pixel * 3 + 2] += c.Z;
                            bits = (((bits ^ BitConverter.SingleToUInt32Bits(c.X)) * 1099511628211UL ^ BitConverter.SingleToUInt32Bits(c.Y)) * 1099511628211UL ^ BitConverter.SingleToUInt32Bits(c.Z)) * 1099511628211UL;
                        }
                    _passes[tile]++;
                    if (_twice)
                    {
                        if (turn == SpoiledTurn) bits ^= 1;
                        if ((turn & 1) == 0) { _mark[tile] = bits; _markTurn[tile] = turn; }
                        else if (_markTurn[tile] == turn - 1) { Interlocked.Increment(ref _checked); if (_mark[tile] != bits) Interlocked.Increment(ref _wrong); }   // (its first drawing is always there, unless a thread was held up for a whole pass)
                    }
                }
                Interlocked.Add(ref _rays, rays); Interlocked.Add(ref _samples, (x1 - x0) * (y1 - y0));
            }
        }
        var workers = Enumerable.Range(0, threads).Select(i => new Thread(Work, 1 << 20) { IsBackground = true, Name = $"Mazesta render {i}" }).ToArray();
        foreach (var w in workers) w.Start();
        foreach (var w in workers) w.Join();
        Elapsed = sw.Elapsed;
    }

    private static readonly byte[] Gamma = [.. Enumerable.Range(0, 4097).Select(i => (byte)(MathF.Pow(i / 4096f, 1 / 2.2f) * 255 + 0.5f))];
    /// <summary>The film's curve (ACES, Narkowicz's fit - the Direct3D test's own): deep shadows held, the brights rolled off; 0 to 4096.</summary>
    private static int Film(float c) => (int)(Math.Clamp(c * (2.51f * c + 0.03f) / (c * (2.43f * c + 0.59f) + 0.14f), 0, 1) * 4096);

    /// <summary>The picture as it stands, for the screen: each pixel's light so far averaged, exposed, passed through the film's curve and
    /// encoded; 32 bits a pixel, blue first, the top row first. A tile no pass has reached yet is black.</summary>
    public void Show(Span<uint> pixels)
    {
        float exposure = _scene.Exposure;
        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
            {
                int p = y * _width + x, done = Volatile.Read(ref _passes[y / Tile * _across + x / Tile]);
                if (done == 0) { pixels[p] = 0xFF000000; continue; }
                float k = exposure / done;
                pixels[p] = 0xFF000000 | (uint)Gamma[Film(_sum[p * 3] * k)] << 16 | (uint)Gamma[Film(_sum[p * 3 + 1] * k)] << 8 | Gamma[Film(_sum[p * 3 + 2] * k)];
            }
    }

    /// <summary>
    /// A small picture of the scene (96 by 54, two paths a pixel) drawn on <paramref name="threads"/> threads, as one number made of every bit
    /// of it. The same processor must make the same number every time: a run is checked with one before and one after, and a difference
    /// means the processor computed wrongly during it (an unstable overclock or undervolt), whatever speed it reached.
    /// </summary>
    public static ulong Check(RenderScene scene, int threads)
    {
        const int w = 96, h = 54; var light = new Vector3[w * h];
        Parallel.For(0, h, new ParallelOptions { MaxDegreeOfParallelism = threads }, y =>
        {
            long rays = 0;
            for (int x = 0; x < w; x++)
                for (int pass = 0; pass < 2; pass++) { uint seed = RenderScene.Seed(y * w + x, pass); light[y * w + x] += scene.Sample(x, y, w, h, ref seed, ref rays); }
        });
        ulong hash = 1469598103934665603UL;
        foreach (byte b in MemoryMarshal.AsBytes(light.AsSpan())) hash = (hash ^ b) * 1099511628211UL;
        return hash;
    }
}
