using System.Diagnostics; using Mazesta.Diagnostics.Memory; using Mazesta.Diagnostics.Storage;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>Uncached drive speed on a scratch file: one sequential write pass, one sequential read pass (1 MiB blocks), then
/// random 4 KiB reads at queue depth 1 for the rest of the duration. Nothing is verified here - correctness is the storage tests' job.</summary>
public sealed class StorageBenchmark : IBenchmark
{
    public static readonly TestDefinition Spec = new(new TestId("bench.storage"), "Bench_Storage", 20,
        [new TestOption(StorageExecutor.DriveOption, "Test_Option_Drive", TestOptionKind.Choice, "", StorageFile.DriveChoices), new TestOption(StorageExecutor.FileMbOption, "Test_Option_FileMb", TestOptionKind.Integer, "1024")]);
    public TestDefinition Definition => Spec;

    public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(BenchmarkResult.Unsupported(Spec.Id, started, "Duration must be positive."));
        var options = request.Options ?? TestOptions.None(Spec);
        int fileMb = options.GetInt(StorageExecutor.FileMbOption);
        if (fileMb is < 16 or > 16384) return Task.FromResult(BenchmarkResult.Unsupported(Spec.Id, started, "The test file must be between 16 MiB and 16 GiB."));
        return Task.Run(() =>
        {
            try
            {
                using var file = StorageFile.Create(StorageFile.ResolveTarget(options.Get(StorageExecutor.DriveOption)), (long)fileMb << 20 & ~(long)(StorageFile.Block - 1));
                return Measure(file, request, started, ct);
            }
            catch (OperationCanceledException) { return new BenchmarkResult(Spec.Id, BenchmarkStatus.Cancelled, started, request.Clock.UtcNow, [], null); }
            catch (StorageUnavailableException ex) { return BenchmarkResult.Unsupported(Spec.Id, started, ex.Message); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new BenchmarkResult(Spec.Id, BenchmarkStatus.Failed, started, request.Clock.UtcNow, [], $"I/O error: {ex.Message}"); }
        }, CancellationToken.None);
    }

    private static BenchmarkResult Measure(StorageFile file, TestExecutionRequest request, DateTimeOffset started, CancellationToken ct)
    {
        using var buffer = new NativeBlock(StorageFile.Block);
        var overall = Stopwatch.StartNew();
        void Progress(double p) => request.Progress?.Invoke(new TestProgress(Math.Clamp(p, 0, 1), "Test_Status_Running"));
        double writeSeconds = 0, readSeconds = 0;
        for (long offset = 0, index = 0; offset < file.Length; offset += StorageFile.Block, index++)
        {
            ct.ThrowIfCancellationRequested();
            MemoryPatterns.Fill(buffer.Span, MemoryPatterns.Count - 1, (int)index);
            long t = Stopwatch.GetTimestamp(); file.Write(buffer.Span, offset); writeSeconds += Stopwatch.GetElapsedTime(t).TotalSeconds;
            Progress(offset / (double)file.Length / 3);
        }
        for (long offset = 0; offset < file.Length; offset += StorageFile.Block)
        {
            ct.ThrowIfCancellationRequested();
            long t = Stopwatch.GetTimestamp(); file.Read(buffer.Span, offset); readSeconds += Stopwatch.GetElapsedTime(t).TotalSeconds;
            Progress(1 / 3.0 + offset / (double)file.Length / 3);
        }
        var random = new Random(7421); long slots = file.Length / StorageFile.Sector, reads = 0; double randomSeconds = 0;
        var window = TimeSpan.FromSeconds(Math.Max(1, request.DurationSeconds - overall.Elapsed.TotalSeconds)); var phase = Stopwatch.StartNew();
        var small = buffer.Span[..StorageFile.Sector];
        while (phase.Elapsed < window)
        {
            ct.ThrowIfCancellationRequested();
            long t = Stopwatch.GetTimestamp(); file.Read(small, random.NextInt64(slots) * StorageFile.Sector); randomSeconds += Stopwatch.GetElapsedTime(t).TotalSeconds; reads++;
            if ((reads & 63) == 0) Progress(2 / 3.0 + phase.Elapsed / window / 3);
        }
        Progress(1);
        BenchmarkMetric[] metrics =
        [
            new("Bench_Storage_SeqWrite", file.Length / Math.Max(1e-6, writeSeconds) / 1e6, "MB/s"), new("Bench_Storage_SeqRead", file.Length / Math.Max(1e-6, readSeconds) / 1e6, "MB/s"),
            new("Bench_Storage_Rand4kIops", reads / Math.Max(1e-6, randomSeconds), "IOPS"), new("Bench_Storage_Rand4kLatency", randomSeconds / reads * 1000, "ms")
        ];
        return new(Spec.Id, BenchmarkStatus.Completed, started, request.Clock.UtcNow, metrics, $"unbuffered I/O; {file.Length >> 20} MiB file; 1 MiB sequential blocks; 4 KiB random reads at QD1 ({reads} reads)");
    }
}
