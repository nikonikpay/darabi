using System.Diagnostics; using ComputeSharp; using Mazesta.Core.Tuning; using Mazesta.Diagnostics.Tuning;
namespace Mazesta.Diagnostics.Gpu.Tuning;

/// <summary>
/// The loads the automatic tuner judges a setting by. <b>Compute</b> is the stress test's integer hash chain, every batch spot-checked against
/// the CPU, scored in Gop/s - it scales with the core clock. <b>Memory</b> writes and verifies a 1 GiB pattern over and over on the GPU itself,
/// scored in GB/s - it scales with the memory clock and catches corrupted VRAM. Either way a result the GPU got wrong is an error, and a driver
/// reset or a removed device ends the run as lost: both are the instability the search is looking for.
/// </summary>
public sealed class ComputeGpuLoad(string gpuName) : IGpuLoad
{
    private const int Threads = 1 << 21, Rounds = 512, SamplesPerBatch = 512, MemoryChunks = 4;
    /// <summary>How long one submission should keep the GPU busy (see <see cref="BatchSizer"/>): long enough that the readback between
    /// submissions is a few percent of the time, short enough that cancelling answers within a quarter second.</summary>
    private static readonly TimeSpan BatchTarget = TimeSpan.FromMilliseconds(250);

    /// <summary>The DirectX adapter with the tuned card's name; the largest one when no name matches (a renamed card, a driver that words it differently).</summary>
    private GraphicsDevice? Device()
    {
        var adapters = GraphicsDevice.EnumerateDevices().Where(d => d.IsHardwareAccelerated).ToList();
        return adapters.FirstOrDefault(d => string.Equals(d.Name.Trim(), gpuName.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? adapters.OrderByDescending(d => d.DedicatedMemorySize).FirstOrDefault();
    }

    public LoadRunResult Run(GpuLoadKind kind, TimeSpan duration, TimeSpan settle, CancellationToken ct) => Run(kind, duration, settle, false, ct);

    public LoadRunResult Run(GpuLoadKind kind, TimeSpan duration, TimeSpan settle, bool rayTraced, CancellationToken ct)
    {
        var device = Device();
        if (device is null) return new(0, 0, true, GpuDevices.NoGpu);
        try { return kind switch { GpuLoadKind.Compute => Compute(device, duration, settle, ct), GpuLoadKind.Scene => SceneGpuLoad.Run(device, duration, settle, rayTraced, ct), _ => Memory(device, duration, settle, ct) }; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new(0, 0, true, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    private static LoadRunResult Compute(GraphicsDevice device, TimeSpan duration, TimeSpan settle, CancellationToken ct)
    {
        long errors = 0, counted = 0, batches = 0; var clock = Stopwatch.StartNew(); var random = new Random(0x5EED); TimeSpan countedFrom = default; var sizer = new BatchSizer();
        using var buffer = device.AllocateReadWriteBuffer<uint>(Threads);
        var host = new uint[Threads];
        while (clock.Elapsed < duration)
        {
            ct.ThrowIfCancellationRequested();
            uint seed = unchecked((uint)(++batches * 2654435761u)); int dispatches = sizer.Count; var batchStart = clock.Elapsed;
            using (var context = device.CreateComputeContext())
                for (int d = 0; d < dispatches; d++) { context.For(Threads, new HashStressShader(buffer, Rounds, seed, 0)); if (d + 1 < dispatches) context.Barrier(buffer); }
            buffer.CopyTo(host);
            for (int s = 0; s < SamplesPerBatch; s++) { int i = random.Next(Threads); if (host[i] != GpuHash.Reference((uint)i, Rounds, seed)) errors++; }
            sizer.Record(clock.Elapsed - batchStart, BatchTarget);
            if (clock.Elapsed >= settle) { if (countedFrom == TimeSpan.Zero) countedFrom = clock.Elapsed; else counted += dispatches; }   // counting starts after the first settled batch, so time and work cover the same span
        }
        double seconds = Math.Max(0.001, (clock.Elapsed - countedFrom).TotalSeconds);
        return new(counted * (double)Threads * Rounds * 6 / seconds / 1e9, errors, false, null);
    }

    private static LoadRunResult Memory(GraphicsDevice device, TimeSpan duration, TimeSpan settle, CancellationToken ct)
    {
        var buffers = new List<ReadWriteBuffer<uint>>(); long errors = 0, bytes = 0; uint pass = 0; var clock = Stopwatch.StartNew(); TimeSpan countedFrom = default;
        try
        {
            using var counter = device.AllocateReadWriteBuffer<int>(1);
            for (int b = 0; b < MemoryChunks; b++) { buffers.Add(device.AllocateReadWriteBuffer<uint>(GpuVramExecutor.ChunkElements)); Write(device, buffers[b], counter, b, 0); }
            // Several verify-and-rewrite passes per submission, the error count read back once: waiting after every chunk left the memory idle between them.
            var sizer = new BatchSizer(initial: 1, max: 256); var bad = new int[1]; const int W = GpuVramExecutor.Width, H = GpuVramExecutor.ChunkElements / GpuVramExecutor.Width;
            while (clock.Elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                int passes = sizer.Count; var batchStart = clock.Elapsed;
                counter.CopyFrom([0]);
                using (var context = device.CreateComputeContext())
                    for (int p = 0; p < passes; p++, pass++)
                        for (int b = 0; b < buffers.Count; b++)
                        {
                            context.For(W, H, new VramPatternShader(buffers[b], counter, W, pass, Base(b), 1, Mask)); context.Barrier(buffers[b]);
                            context.For(W, H, new VramPatternShader(buffers[b], counter, W, pass + 1, Base(b), 0, Mask)); context.Barrier(buffers[b]);
                        }
                counter.CopyTo(bad); errors += bad[0];
                sizer.Record(clock.Elapsed - batchStart, BatchTarget);
                if (clock.Elapsed >= settle) { if (countedFrom == TimeSpan.Zero) countedFrom = clock.Elapsed; else bytes += 2L * passes * buffers.Count * GpuVramExecutor.ChunkElements * 4; }
            }
        }
        finally { foreach (var b in buffers) b.Dispose(); }
        double seconds = Math.Max(0.001, (clock.Elapsed - countedFrom).TotalSeconds);
        return new(bytes / seconds / 1e9, errors, false, null);
    }

    private static uint Base(int chunk) => (uint)chunk * (uint)GpuVramExecutor.ChunkElements;
    private const uint Mask = GpuVramExecutor.ChunkElements - 1;
    private static void Write(GraphicsDevice device, ReadWriteBuffer<uint> buffer, ReadWriteBuffer<int> counter, int chunk, uint pass)
        => device.For(GpuVramExecutor.Width, GpuVramExecutor.ChunkElements / GpuVramExecutor.Width, new VramPatternShader(buffer, counter, GpuVramExecutor.Width, pass, Base(chunk), 0, Mask));
}
