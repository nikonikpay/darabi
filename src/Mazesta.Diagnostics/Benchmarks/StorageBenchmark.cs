using System.Diagnostics; using Mazesta.Diagnostics.Memory; using Mazesta.Diagnostics.Storage;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>
/// Uncached drive speed measured the way CrystalDiskMark does, so the numbers are comparable with what technicians know:
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
    public static readonly TestDefinition Spec = new(new TestId("bench.storage"), "Bench_Storage", 20,
        [new TestOption(StorageExecutor.DriveOption, "Test_Option_Drive", TestOptionKind.Choice, "", StorageFile.DriveChoices), new TestOption(StorageExecutor.FileMbOption, "Test_Option_FileMb", TestOptionKind.Integer, "1024")]);
    public TestDefinition Definition => Spec;
    private const int SeqDepth = 8, RandomDepth = 32;
    // Share of the duration per timed phase: sequential write, sequential read, random read Q32, random read Q1, random write Q32.
    private static readonly double[] Shares = [0.25, 0.25, 0.2, 0.15, 0.15];
    private readonly TimeSpan _rest;

    public StorageBenchmark() : this(TimeSpan.FromSeconds(5)) { }
    internal StorageBenchmark(TimeSpan rest) => _rest = rest;

    public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(BenchmarkResult.Unsupported(Spec.Id, started, "Duration must be positive."));
        var options = request.Options ?? TestOptions.None(Spec);
        int fileMb = options.GetInt(StorageExecutor.FileMbOption);
        if (fileMb is < 16 or > 65536) return Task.FromResult(BenchmarkResult.Unsupported(Spec.Id, started, "The test file must be between 16 MiB and 64 GiB."));
        return Task.Run(async () =>
        {
            try
            {
                using var file = StorageFile.Create(StorageFile.ResolveTarget(options.Get(StorageExecutor.DriveOption)), (long)fileMb << 20 & ~(long)(StorageFile.Block - 1), overlapped: true);
                return await MeasureAsync(file, request, started, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return BenchmarkResult.Cancelled(Spec.Id, started, request.Clock.UtcNow); }
            catch (StorageUnavailableException ex) { return BenchmarkResult.Unsupported(Spec.Id, started, ex.Message); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return BenchmarkResult.Failed(Spec.Id, started, request.Clock.UtcNow, $"I/O error: {ex.Message}"); }
        }, CancellationToken.None);
    }

    private async Task<BenchmarkResult> MeasureAsync(StorageFile file, TestExecutionRequest request, DateTimeOffset started, CancellationToken ct)
    {
        using var buffers = new Buffers(SeqDepth, RandomDepth);
        long blocks = file.Length / StorageFile.Block, sectors = file.Length / StorageFile.Sector;
        var total = TimeSpan.FromSeconds(request.DurationSeconds) + 2 * _rest; var overall = Stopwatch.StartNew();
        TimeSpan Share(int phase) => TimeSpan.FromSeconds(request.DurationSeconds * Shares[phase]);
        void Progress() => request.Report(Math.Min(0.99, overall.Elapsed / total));

        long cursor = -1;   // the next sequential block; shared by the workers of one phase
        long NextBlock() => Interlocked.Increment(ref cursor) % blocks * StorageFile.Block;
        long RandomSector(Random r) => r.NextInt64(sectors) * StorageFile.Sector;

        // Preparation (untimed): one full pass, so every later read hits written data; then the first rest.
        for (long offset = 0; offset < file.Length; offset += StorageFile.Block) { ct.ThrowIfCancellationRequested(); await file.WriteAsync(buffers.Seq[0], offset, ct).ConfigureAwait(false); }
        await Task.Delay(_rest, ct).ConfigureAwait(false);

        cursor = -1; var seqWrite = await PhaseAsync(SeqDepth, Share(0), (w, _) => file.WriteAsync(buffers.Seq[w], NextBlock(), ct), Progress, ct).ConfigureAwait(false);
        await Task.Delay(_rest, ct).ConfigureAwait(false);
        cursor = -1; var seqRead = await PhaseAsync(SeqDepth, Share(1), async (w, _) => await file.ReadAsync(buffers.Seq[w], NextBlock(), ct).ConfigureAwait(false), Progress, ct).ConfigureAwait(false);
        var randQ32 = await PhaseAsync(RandomDepth, Share(2), async (w, r) => await file.ReadAsync(buffers.Small[w], RandomSector(r), ct).ConfigureAwait(false), Progress, ct).ConfigureAwait(false);
        var randQ1 = await PhaseAsync(1, Share(3), async (w, r) => await file.ReadAsync(buffers.Small[w], RandomSector(r), ct).ConfigureAwait(false), Progress, ct).ConfigureAwait(false);
        var randWrite = await PhaseAsync(RandomDepth, Share(4), (w, r) => file.WriteAsync(buffers.Small[w], RandomSector(r), ct), Progress, ct).ConfigureAwait(false);
        request.Report(1);

        BenchmarkMetric[] metrics =
        [
            new("Bench_Storage_SeqWrite", seqWrite.PerSecond * StorageFile.Block / 1e6, "MB/s"), new("Bench_Storage_SeqRead", seqRead.PerSecond * StorageFile.Block / 1e6, "MB/s"),
            new("Bench_Storage_Rand4kQ32Read", randQ32.PerSecond, "IOPS"), new("Bench_Storage_Rand4kQ1Read", randQ1.PerSecond, "IOPS"),
            new("Bench_Storage_Rand4kLatency", randQ1.Seconds / randQ1.Operations * 1e6, "µs"), new("Bench_Storage_Rand4kQ32Write", randWrite.PerSecond, "IOPS")
        ];
        return new(Spec.Id, BenchmarkStatus.Completed, started, request.Clock.UtcNow, metrics,
            $"unbuffered overlapped I/O; {file.Length >> 20} MiB file written once before timing and {_rest.TotalSeconds:0} s rest; SEQ1M Q{SeqDepth}T1 write, {_rest.TotalSeconds:0} s rest, SEQ1M Q{SeqDepth}T1 read, RND4K Q{RandomDepth}T1 read, RND4K Q1T1 read, RND4K Q{RandomDepth}T1 write");
    }

    private readonly record struct PhaseResult(long Operations, double Seconds) { public double PerSecond => Operations / Math.Max(1e-6, Seconds); }

    /// <summary>Keeps <paramref name="depth"/> requests in flight for <paramref name="length"/>; each worker has its own buffer
    /// (index) and its own seeded random stream, so no two requests in flight share memory.</summary>
    private static async Task<PhaseResult> PhaseAsync(int depth, TimeSpan length, Func<int, Random, ValueTask> io, Action progress, CancellationToken ct)
    {
        long operations = 0; var sw = Stopwatch.StartNew();
        async Task Worker(int w)
        {
            var random = new Random(7421 + w);
            for (long mine = 1; sw.Elapsed < length; mine++)
            {
                await io(w, random).ConfigureAwait(false); Interlocked.Increment(ref operations);
                if (w == 0 && (mine & 63) == 0) progress();
            }
        }
        await Task.WhenAll(Enumerable.Range(0, depth).Select(Worker)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return new(operations, sw.Elapsed.TotalSeconds);
    }

    /// <summary>Sector-aligned buffers for unbuffered I/O, filled with incompressible data once up front (generating it per
    /// request would make the CPU the limit at PCIe 5.0 speeds, and a drive that compresses would flatter a constant pattern).</summary>
    private sealed class Buffers : IDisposable
    {
        private readonly NativeBlock _seq, _small;
        public Memory<byte>[] Seq { get; }
        public Memory<byte>[] Small { get; }
        public Buffers(int seqCount, int smallCount)
        {
            _seq = new NativeBlock(seqCount * StorageFile.Block); _small = new NativeBlock(smallCount * StorageFile.Sector);
            new Random(0x5EED).NextBytes(_seq.Span); new Random(0xFEED).NextBytes(_small.Span);
            Seq = [.. Enumerable.Range(0, seqCount).Select(i => _seq.Memory.Slice(i * StorageFile.Block, StorageFile.Block))];
            Small = [.. Enumerable.Range(0, smallCount).Select(i => _small.Memory.Slice(i * StorageFile.Sector, StorageFile.Sector))];
        }
        public void Dispose() { _seq.Dispose(); _small.Dispose(); }
    }
}
