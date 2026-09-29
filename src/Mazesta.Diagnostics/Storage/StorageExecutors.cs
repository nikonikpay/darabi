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
    internal static TestOption[] CommonOptions(string fileMbDefault) =>
    [
        new TestOption(DriveOption, "Test_Option_Drive", TestOptionKind.Choice, "", StorageFile.DriveChoices),
        new TestOption(FileMbOption, "Test_Option_FileMb", TestOptionKind.Integer, fileMbDefault)
    ];

    /// <summary>The folder a run with this drive option used (the first fixed drive when none was chosen), or null when there is none.</summary>
    public static string? TargetOf(string chosen) { try { return StorageFile.ResolveTarget(chosen); } catch (StorageUnavailableException) { return null; } }

    public abstract TestDefinition Definition { get; }

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        var options = request.Options ?? TestOptions.None(Definition);
        int fileMb = options.GetInt(FileMbOption);
        if (StorageFile.CheckSizeMb(fileMb) is { } invalid) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, invalid));
        return Task.Run(() =>
        {
            long errors = 0; string detail = "";
            try
            {
                using var file = StorageFile.Create(StorageFile.ResolveTarget(options.Get(DriveOption)), StorageFile.LengthOf(fileMb));
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
        var clock = Stopwatch.StartNew(); double writeSeconds = 0, readSeconds = 0; long bytes = 0, shortReads = 0, wrongBytes = 0; int passes = 0;
        do
        {
            var phase = Stopwatch.StartNew();
            for (long offset = 0, index = 0; offset < file.Length; offset += StorageFile.Block, index++)
            {
                ct.ThrowIfCancellationRequested();
                MemoryPatterns.Fill(buffer.Span, MemoryPatterns.RandomPass + passes * MemoryPatterns.Count, (int)index);   // the seeded random pattern, different every pass
                file.Write(buffer.Span, offset);
                Report(request, clock);
            }
            writeSeconds += phase.Elapsed.TotalSeconds; phase.Restart();
            for (long offset = 0, index = 0; offset < file.Length; offset += StorageFile.Block, index++)
            {
                ct.ThrowIfCancellationRequested();
                int read = file.Read(buffer.Span, offset);
                // A short read is counted as one error of its own kind, never as a whole block read; a full block is compared byte by byte.
                if (read < StorageFile.Block) { shortReads++; errors++; }
                else { long bad = MemoryPatterns.CountMismatches(buffer.Span, MemoryPatterns.RandomPass + passes * MemoryPatterns.Count, (int)index); wrongBytes += bad; errors += bad; }
                Report(request, clock);
            }
            readSeconds += phase.Elapsed.TotalSeconds; bytes += file.Length; passes++;
        }
        while (clock.Elapsed.TotalSeconds < request.DurationSeconds);
        return $"sequential unbuffered write-through I/O; write {bytes / Math.Max(0.001, writeSeconds) / 1e6:F0} MB/s; read {bytes / Math.Max(0.001, readSeconds) / 1e6:F0} MB/s; verified {bytes >> 20} MiB in {passes} pass(es)"
             + (shortReads > 0 ? $"; {shortReads} short read(s)" : "") + (wrongBytes > 0 ? $"; {wrongBytes} byte(s) read back wrong" : "");
    }
}

/// <summary>Random 4 KiB write-then-read at queue depth 1, every block verified; reports IOPS and the latency percentiles of every read and
/// every write of the run (not 1/IOPS, and not only the last requests). Short reads and wrong bytes are counted apart.</summary>
public sealed class StorageRandom4kExecutor : StorageExecutor
{
    public static readonly TestDefinition Spec = new(new TestId("storage.random4k"), "Test_Storage_Random4k", 30, CommonOptions("256"));
    public override TestDefinition Definition => Spec;

    private protected override string Exercise(StorageFile file, TestExecutionRequest request, CancellationToken ct, ref long errors)
    {
        using var buffer = new NativeBlock(StorageFile.Block);
        // Materialise the whole file first: blocks that were never written are not tested and read back as zero without touching the media.
        for (long offset = 0, index = 0; offset < file.Length; offset += StorageFile.Block, index++) { ct.ThrowIfCancellationRequested(); MemoryPatterns.Fill(buffer.Span, MemoryPatterns.RandomPass, (int)index); file.Write(buffer.Span, offset); }
        var random = new Random(7421); var reads = new LatencyHistogram(); var writes = new LatencyHistogram();
        long slots = file.Length / StorageFile.Sector, operations = 0, shortReads = 0, wrongBytes = 0; double writeMs = 0, readMs = 0;
        var clock = Stopwatch.StartNew();
        do
        {
            ct.ThrowIfCancellationRequested();
            long offset = random.NextInt64(slots) * StorageFile.Sector;
            var block = buffer.Span[..StorageFile.Sector];
            MemoryPatterns.Fill(block, MemoryPatterns.RandomPass, (int)(offset / StorageFile.Sector));
            long stamp = Stopwatch.GetTimestamp(); file.Write(block, offset); double w = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
            var back = buffer.Span.Slice(StorageFile.Block - StorageFile.Sector, StorageFile.Sector);
            stamp = Stopwatch.GetTimestamp(); int read = file.Read(back, offset); double r = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
            if (read < StorageFile.Sector) { shortReads++; errors++; }
            else { int bad = StorageFile.Sector - CountEqual(block, back); wrongBytes += bad; errors += bad; }
            writeMs += w; readMs += r; operations++; writes.Add(w); reads.Add(r);
            if ((operations & 31) == 0) Report(request, clock);
        }
        while (clock.Elapsed.TotalSeconds < request.DurationSeconds);
        return $"random 4K QD1 unbuffered write-through; {operations} verified blocks; write {operations / Math.Max(0.000001, writeMs / 1000):F0} IOPS, latency {writes.Describe()}; "
             + $"read {operations / Math.Max(0.000001, readMs / 1000):F0} IOPS, latency {reads.Describe()}"
             + (shortReads > 0 ? $"; {shortReads} short read(s)" : "") + (wrongBytes > 0 ? $"; {wrongBytes} byte(s) read back wrong" : "");
    }

    private static int CountEqual(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) { int equal = 0; for (int i = 0; i < a.Length; i++) if (a[i] == b[i]) equal++; return equal; }
}
