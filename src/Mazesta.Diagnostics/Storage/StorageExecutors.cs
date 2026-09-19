using System.Diagnostics; using Mazesta.Diagnostics.Memory;
namespace Mazesta.Diagnostics.Storage;

/// <summary>
/// Shared shape of the two storage tests: pick a drive, make the scratch file, measure, verify every block
/// read back against what was written (a wrong block is a counted error), and always clean up. Real I/O
/// errors are a result too - a drive that throws while being exercised has failed the test.
/// </summary>
public abstract class StorageExecutor : ITestExecutor
{
    public const string DriveOption = "drive", FileMbOption = "fileMb";
    protected static TestOption[] CommonOptions(string fileMbDefault) =>
    [
        new TestOption(DriveOption, "Test_Option_Drive", TestOptionKind.Choice, "", StorageFile.DriveChoices),
        new TestOption(FileMbOption, "Test_Option_FileMb", TestOptionKind.Integer, fileMbDefault)
    ];

    public abstract TestDefinition Definition { get; }

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        var options = request.Options ?? TestOptions.None(Definition);
        int fileMb = options.GetInt(FileMbOption);
        if (fileMb is < 16 or > 16384) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "The test file must be between 16 MiB and 16 GiB."));
        return Task.Run(() =>
        {
            long errors = 0; string detail = "";
            try
            {
                using var file = StorageFile.Create(StorageFile.ResolveTarget(options.Get(DriveOption)), (long)fileMb << 20 & ~(long)(StorageFile.Block - 1));
                detail = Exercise(file, request, ct, ref errors);
            }
            catch (OperationCanceledException) { return new TestRunResult(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, detail); }
            catch (StorageUnavailableException ex)
            { return TestRunResult.Unsupported(Definition.Id, started, ex.Message); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return new TestRunResult(Definition.Id, TestOutcome.Failed, started, request.Clock.UtcNow, errors + 1, $"I/O error while exercising the drive: {ex.Message}"); }
            return new TestRunResult(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, errors, detail);
        }, CancellationToken.None);
    }

    /// <summary>Runs the measurement for the request's duration; returns the human-readable evidence and adds mismatches to <paramref name="errors"/>.</summary>
    private protected abstract string Exercise(StorageFile file, TestExecutionRequest request, CancellationToken ct, ref long errors);

    protected static void Report(TestExecutionRequest request, Stopwatch clock) =>
        request.Progress?.Invoke(new TestProgress(Math.Clamp(clock.Elapsed.TotalSeconds / request.DurationSeconds, 0, 1), "Test_Status_Running"));
}

/// <summary>Whole-file sequential write then read-back, repeated for the duration. Uncached, so the speeds are the drive's.</summary>
public sealed class StorageSequentialExecutor : StorageExecutor
{
    public static readonly TestDefinition Spec = new(new TestId("storage.sequential"), "Test_Storage_Sequential", 30, CommonOptions("1024"));
    public override TestDefinition Definition => Spec;

    private protected override string Exercise(StorageFile file, TestExecutionRequest request, CancellationToken ct, ref long errors)
    {
        using var buffer = new NativeBlock(StorageFile.Block);
        var clock = Stopwatch.StartNew(); double writeSeconds = 0, readSeconds = 0; long bytes = 0; int passes = 0;
        do
        {
            var phase = Stopwatch.StartNew();
            for (long offset = 0, index = 0; offset < file.Length; offset += StorageFile.Block, index++)
            {
                ct.ThrowIfCancellationRequested();
                MemoryPatterns.Fill(buffer.Span, MemoryPatterns.Count - 1 + passes * MemoryPatterns.Count, (int)index);   // the seeded random pattern, different every pass
                file.Write(buffer.Span, offset);
                Report(request, clock);
            }
            writeSeconds += phase.Elapsed.TotalSeconds; phase.Restart();
            for (long offset = 0, index = 0; offset < file.Length; offset += StorageFile.Block, index++)
            {
                ct.ThrowIfCancellationRequested();
                int read = file.Read(buffer.Span, offset);
                errors += read < StorageFile.Block ? StorageFile.Block - read : MemoryPatterns.CountMismatches(buffer.Span, MemoryPatterns.Count - 1 + passes * MemoryPatterns.Count, (int)index);
                Report(request, clock);
            }
            readSeconds += phase.Elapsed.TotalSeconds; bytes += file.Length; passes++;
        }
        while (clock.Elapsed.TotalSeconds < request.DurationSeconds);
        return $"sequential unbuffered I/O; write {bytes / Math.Max(0.001, writeSeconds) / 1e6:F0} MB/s; read {bytes / Math.Max(0.001, readSeconds) / 1e6:F0} MB/s; verified {bytes >> 20} MiB in {passes} pass(es)";
    }
}

/// <summary>Random 4 KiB write-then-read at queue depth 1, every block verified; reports IOPS and latency percentiles.</summary>
public sealed class StorageRandom4kExecutor : StorageExecutor
{
    public static readonly TestDefinition Spec = new(new TestId("storage.random4k"), "Test_Storage_Random4k", 30, CommonOptions("256"));
    public override TestDefinition Definition => Spec;
    private const int Latencies = 4096;

    private protected override string Exercise(StorageFile file, TestExecutionRequest request, CancellationToken ct, ref long errors)
    {
        using var buffer = new NativeBlock(StorageFile.Block);
        // Materialise the whole file first: blocks that were never written are not tested and read back as zero without touching the media.
        for (long offset = 0, index = 0; offset < file.Length; offset += StorageFile.Block, index++) { ct.ThrowIfCancellationRequested(); MemoryPatterns.Fill(buffer.Span, MemoryPatterns.Count - 1, (int)index); file.Write(buffer.Span, offset); }
        var random = new Random(7421); var recent = new Queue<double>(Latencies + 1);
        long slots = file.Length / StorageFile.Sector, operations = 0; double writeMs = 0, readMs = 0;
        var clock = Stopwatch.StartNew();
        do
        {
            ct.ThrowIfCancellationRequested();
            long offset = random.NextInt64(slots) * StorageFile.Sector;
            var block = buffer.Span[..StorageFile.Sector];
            MemoryPatterns.Fill(block, MemoryPatterns.Count - 1, (int)(offset / StorageFile.Sector));
            long stamp = Stopwatch.GetTimestamp(); file.Write(block, offset); double w = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
            var back = buffer.Span.Slice(StorageFile.Block - StorageFile.Sector, StorageFile.Sector);
            stamp = Stopwatch.GetTimestamp(); int read = file.Read(back, offset); double r = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
            errors += read < StorageFile.Sector ? StorageFile.Sector - read : StorageFile.Sector - CountEqual(block, back);
            writeMs += w; readMs += r; operations++;
            recent.Enqueue(w + r); if (recent.Count > Latencies) recent.Dequeue();
            if ((operations & 31) == 0) Report(request, clock);
        }
        while (clock.Elapsed.TotalSeconds < request.DurationSeconds);
        var ordered = recent.Order().ToArray();
        return $"random 4K QD1 unbuffered; {operations} verified blocks; write {operations / Math.Max(0.000001, writeMs / 1000):F0} IOPS ({writeMs / operations:F3} ms); read {operations / Math.Max(0.000001, readMs / 1000):F0} IOPS ({readMs / operations:F3} ms); P95 cycle {ordered[Math.Max(0, (int)Math.Ceiling(ordered.Length * 0.95) - 1)]:F3} ms";
    }

    private static int CountEqual(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) { int equal = 0; for (int i = 0; i < a.Length; i++) if (a[i] == b[i]) equal++; return equal; }
}
