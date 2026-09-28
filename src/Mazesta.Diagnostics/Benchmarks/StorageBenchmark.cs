using Mazesta.Core.Hardware; using System.Diagnostics; using Mazesta.Diagnostics.Memory; using Mazesta.Diagnostics.Storage;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>
/// Uncached drive speed at the queue depths the industry quotes, so the numbers are comparable with a drive's data sheet:
/// sequential 1 MiB at queue depth 8 (write, then read), random 4 KiB reads at queue depth 32 and 1, and random 4 KiB
/// writes at queue depth 32. Requests are overlapped - one request at a time (queue depth 1) cannot keep an NVMe drive
/// busy, and reads suffer most because a write is acknowledged from the drive's cache while a read has to wait for the
/// flash, which is how reads came out slower than writes. The drive rests after the preparation write and again between
/// writing and reading, so each timed phase starts with its write cache flushed (on a busy SLC cache the next phase is slow). Depth 8 of 1 MiB is enough to saturate a PCIe 5.0 x4 drive (~14 GB/s).
/// The file is written once before anything is timed: reading space that was never written returns zeros without
/// touching the drive, and extending a file serialises the writes. Nothing is verified here - that is the storage tests' job.
/// </summary>
public sealed class StorageBenchmark : IBenchmark
{
    public static readonly TestDefinition Spec = new(new TestId("bench.storage"), "Bench_Storage", 20, StorageExecutor.CommonOptions("1024"));
    public TestDefinition Definition => Spec;
    public HardwareKind Component => HardwareKind.Storage;
    private const int SeqDepth = 8, RandomDepth = 32;
    private readonly TimeSpan _rest;

    public StorageBenchmark() : this(TimeSpan.FromSeconds(5)) { }
    internal StorageBenchmark(TimeSpan rest) => _rest = rest;

    public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(BenchmarkResult.Unsupported(Spec.Id, started, "Duration must be positive."));
        var options = request.Options ?? TestOptions.None(Spec);
        int fileMb = options.GetInt(StorageExecutor.FileMbOption);
        if (StorageFile.CheckSizeMb(fileMb) is { } invalid) return Task.FromResult(BenchmarkResult.Unsupported(Spec.Id, started, invalid));
        return Task.Run(async () =>
        {
            try
            {
                using var file = StorageFile.Create(StorageFile.ResolveTarget(options.Get(StorageExecutor.DriveOption)), StorageFile.LengthOf(fileMb), asynchronous: true, writeThrough: false);
                return await MeasureAsync(file, request, started, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return BenchmarkResult.Cancelled(Spec.Id, started, request.Clock.UtcNow); }
            catch (StorageUnavailableException ex) { return BenchmarkResult.Unsupported(Spec.Id, started, ex.Message); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return BenchmarkResult.Failed(Spec.Id, started, request.Clock.UtcNow, $"I/O error: {ex.Message}"); }
        }, CancellationToken.None);
    }

    private async Task<BenchmarkResult> MeasureAsync(StorageFile file, TestExecutionRequest request, DateTimeOffset started, CancellationToken ct)
    {
        using var buffers = new Buffers();
        long blocks = file.Length / StorageFile.Block, sectors = file.Length / StorageFile.Sector;
        var total = TimeSpan.FromSeconds(request.DurationSeconds) + 2 * _rest; var overall = Stopwatch.StartNew();
        TimeSpan Share(double fraction) => TimeSpan.FromSeconds(request.DurationSeconds * fraction);
        void Progress() => request.Report(Math.Min(0.99, overall.Elapsed / total));

        long cursor = -1;   // the next sequential block; shared by the workers of one phase
        long NextBlock() => Interlocked.Increment(ref cursor) % blocks * StorageFile.Block;
        long RandomSector(Random r) => r.NextInt64(sectors) * StorageFile.Sector;

        // Preparation (untimed): one full pass, so every later read hits written data; then the first rest.
        await Task.WhenAll(Enumerable.Range(0, SeqDepth).Select(async w =>
        {
            for (long b; (b = Interlocked.Increment(ref cursor)) < blocks;) await file.WriteAsync(buffers.Seq[w], b * StorageFile.Block, ct).ConfigureAwait(false);
        })).ConfigureAwait(false);
        await Task.Delay(_rest, ct).ConfigureAwait(false);

        cursor = -1; double seqWrite = await PhaseAsync(SeqDepth, Share(0.25), (w, _) => file.WriteAsync(buffers.Seq[w], NextBlock(), ct), Progress, ct).ConfigureAwait(false);
        await Task.Delay(_rest, ct).ConfigureAwait(false);
        cursor = -1; double seqRead = await PhaseAsync(SeqDepth, Share(0.25), async (w, _) => await file.ReadAsync(buffers.Seq[w], NextBlock(), ct).ConfigureAwait(false), Progress, ct).ConfigureAwait(false);
        double randQ32 = await PhaseAsync(RandomDepth, Share(0.2), async (w, r) => await file.ReadAsync(buffers.Small[w], RandomSector(r), ct).ConfigureAwait(false), Progress, ct).ConfigureAwait(false);
        double randQ1 = await PhaseAsync(1, Share(0.15), async (w, r) => await file.ReadAsync(buffers.Small[w], RandomSector(r), ct).ConfigureAwait(false), Progress, ct).ConfigureAwait(false);
        double randWrite = await PhaseAsync(RandomDepth, Share(0.15), (w, r) => file.WriteAsync(buffers.Small[w], RandomSector(r), ct), Progress, ct).ConfigureAwait(false);
        request.Report(1);

        BenchmarkMetric[] metrics =
        [
            new("Bench_Storage_SeqWrite", seqWrite * StorageFile.Block / 1e6, "MB/s"), new("Bench_Storage_SeqRead", seqRead * StorageFile.Block / 1e6, "MB/s"),
            new("Bench_Storage_Rand4kQ32Read", randQ32, "IOPS"), new("Bench_Storage_Rand4kQ1Read", randQ1, "IOPS"),
            new("Bench_Storage_Rand4kLatency", 1e6 / randQ1, "µs"), new("Bench_Storage_Rand4kQ32Write", randWrite, "IOPS")   // one request in flight: latency is 1 / IOPS
        ];
        return new(Spec.Id, BenchmarkStatus.Completed, started, request.Clock.UtcNow, metrics,
            $"unbuffered overlapped I/O; {file.Length >> 20} MiB file written once before timing and {_rest.TotalSeconds:0} s rest; SEQ1M Q{SeqDepth}T1 write, {_rest.TotalSeconds:0} s rest, SEQ1M Q{SeqDepth}T1 read, RND4K Q{RandomDepth}T1 read, RND4K Q1T1 read, RND4K Q{RandomDepth}T1 write");
    }

    /// <summary>Keeps <paramref name="depth"/> requests in flight for <paramref name="length"/> and returns requests per second. Each worker
    /// has its own buffer (index) and its own seeded random stream, so no two requests in flight share memory; workers count on their own
    /// and progress is reported by time, so the timed loop does no shared or UI work per request.</summary>
    private static async Task<double> PhaseAsync(int depth, TimeSpan length, Func<int, Random, ValueTask> io, Action progress, CancellationToken ct)
    {
        long operations = 0; var sw = Stopwatch.StartNew();
        async Task Worker(int w)
        {
            var random = new Random(7421 + w); long mine = 0; var nextReport = TimeSpan.Zero;
            while (sw.Elapsed < length)
            {
                await io(w, random).ConfigureAwait(false); mine++;
                if (w == 0 && sw.Elapsed >= nextReport) { progress(); nextReport = sw.Elapsed + TimeSpan.FromMilliseconds(250); }
            }
            Interlocked.Add(ref operations, mine);
        }
        await Task.WhenAll(Enumerable.Range(0, depth).Select(Worker)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return operations / Math.Max(1e-6, sw.Elapsed.TotalSeconds);
    }

    /// <summary>Sector-aligned buffers for unbuffered I/O (one per request in flight), filled with the storage tests' seeded random
    /// pattern once up front: generating data per request would make the CPU the limit at PCIe 5.0 speeds, and a drive that compresses
    /// would flatter a constant pattern.</summary>
    private sealed class Buffers : IDisposable
    {
        private readonly NativeBlock _seq = new(SeqDepth * StorageFile.Block), _small = new(RandomDepth * StorageFile.Sector);
        public Memory<byte>[] Seq { get; }
        public Memory<byte>[] Small { get; }
        public Buffers()
        {
            MemoryPatterns.Fill(_seq.Span, MemoryPatterns.Count - 1, 0); MemoryPatterns.Fill(_small.Span, MemoryPatterns.Count - 1, 1);
            Seq = [.. Enumerable.Range(0, SeqDepth).Select(i => _seq.Memory.Slice(i * StorageFile.Block, StorageFile.Block))];
            Small = [.. Enumerable.Range(0, RandomDepth).Select(i => _small.Memory.Slice(i * StorageFile.Sector, StorageFile.Sector))];
        }
        public void Dispose() { _seq.Dispose(); _small.Dispose(); }
    }
}
