using System.ComponentModel; using System.Diagnostics; using System.Runtime.InteropServices; using Microsoft.Win32.SafeHandles; using Mazesta.Diagnostics.Memory;
namespace Mazesta.Diagnostics.Storage;

/// <summary>
/// The surface scan: every byte of a drive's volume read once, front to back, in 1 MiB blocks straight from the device (no cache), looking for sectors the drive cannot read.
/// The other storage tests write a scratch file and read it back, which proves the part of the drive that file landed on; this one covers all of it, free space and files alike,
/// and it only reads - nothing is written, so it is safe on a customer's data. A block that fails is read again sector by sector (4 KiB) to count the bad sectors and
/// say where they are; a block that took over a second to answer is counted as slow (a sector the drive is struggling to recover). It cannot repair a bad sector.
/// time set does not limit it: it reads the whole drive (hours on a big hard disk) and only Cancel stops it early, which is never a pass.
/// </summary>
public sealed class StorageSurfaceExecutor : ITestExecutor
{
    public static readonly TestDefinition Spec = new(new TestId("storage.surface"), "Test_Storage_Surface", 3600,
        [new TestOption(StorageExecutor.DriveOption, "Test_Option_Drive", TestOptionKind.Choice, "", StorageFile.DriveChoicesWithAll)]);
    public TestDefinition Definition => Spec;

    private const int StopAfterBad = 50, SlowMs = 1000, ListedBad = 8;

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        string chosen = (request.Options ?? TestOptions.None(Definition)).Get(StorageExecutor.DriveOption);
        return Task.Run(() =>
        {
            string[] targets; try { targets = [.. StorageFile.Targets(chosen)]; } catch (StorageUnavailableException ex) { return TestRunResult.Unsupported(Definition.Id, started, ex.Message); }
            long errors = 0; var details = new List<string>(); bool complete = true; int tested = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                int at = i; double share = (double)request.DurationSeconds / targets.Length;
                try
                {
                    var r = Scan(targets[i], share, p => request.Progress?.Invoke(new TestProgress((at + p) / targets.Length, "Test_Status_Running")), request, ct);
                    errors += r.BadSectors; complete &= r.Complete; tested++; details.Add(targets.Length > 1 ? $"{targets[i]} {r.Detail}" : r.Detail);
                }
                catch (OperationCanceledException) { return new TestRunResult(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, string.Join("; ", details)); }
                catch (StorageUnavailableException ex)
                {
                    if (targets.Length == 1) return TestRunResult.Unsupported(Definition.Id, started, ex.Message);
                    details.Add($"{targets[i]} not tested: {ex.Message}");
                }
            }
            var detail = string.Join("; ", details);
            if (tested == 0) return TestRunResult.Unsupported(Definition.Id, started, detail);
            // Bad sectors fail it. Without any, only a whole drive read is a pass: a scan the time ran out on is reported as stopped (Cancelled), never as a pass for what was not read.
            var outcome = errors > 0 ? TestOutcome.Failed : complete ? TestOutcome.Passed : TestOutcome.Cancelled;
            return new TestRunResult(Definition.Id, outcome, started, request.Clock.UtcNow, errors, detail);
        }, CancellationToken.None);
    }

    internal readonly record struct ScanResult(long BadSectors, bool Complete, string Detail);

    private static ScanResult Scan(string root, double seconds, Action<double> progress, TestExecutionRequest request, CancellationToken ct)
    {
        string volume = @"\\.\" + root.TrimEnd('\\');   // "\\.\C:"
        using var handle = CreateFile(volume, GenericRead, ShareReadWrite, 0, OpenExisting, NoBuffering, 0);
        if (handle.IsInvalid) throw new StorageUnavailableException($"{root} could not be opened for reading: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        if (!DeviceIoControl(handle, IoctlGetLengthInfo, 0, 0, out long length, sizeof(long), out _, 0) || length <= 0)
            throw new StorageUnavailableException($"{root}: the size of the volume could not be read: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        request.Note("Log_Surface_Open", @"\\.\X: opened read-only, FILE_FLAG_NO_BUFFERING; sequential 1 MiB reads, a failed block read again in 4 KiB sectors", root, length >> 20);
        using var buffer = new NativeBlock(StorageFile.Block);
        var clock = Stopwatch.StartNew(); long offset = 0, bad = 0, slow = 0; double slowest = 0; bool stoppedEarly = false; var spots = new List<long>();
        while (offset < length && !stoppedEarly)   // the whole drive, however long: the time set is not a limit here, only Cancel ends it early
        {
            ct.ThrowIfCancellationRequested();
            int want = (int)Math.Min(StorageFile.Block, length - offset); want -= want % StorageFile.Sector;
            if (want <= 0) break;
            long t0 = Stopwatch.GetTimestamp();
            try { if (RandomAccess.Read(handle, buffer.Span[..want], offset) < want) break; }
            catch (IOException)
            {
                long found = Probe(handle, buffer, offset, want, spots, ct);
                if (found == 0) { found = 1; spots.Add(offset); }   // it failed once and read fine in sectors: an unstable block still counts, once
                bad += found; request.NoteWarning("Log_Surface_Bad", root, offset >> 20, found);
                if (bad >= StopAfterBad) stoppedEarly = true;
            }
            double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            if (ms > SlowMs) { slow++; slowest = Math.Max(slowest, ms); }
            offset += want; progress((double)offset / length);
        }
        bool complete = offset >= length - StorageFile.Sector * 2 && !stoppedEarly;   // the last few KiB under a sector multiple cannot be read unbuffered
        double rate = offset / Math.Max(0.001, clock.Elapsed.TotalSeconds) / 1e6;
        string detail = $"read {offset / 1073741824.0:F1} of {length / 1073741824.0:F1} GiB ({100.0 * offset / length:F1} %) straight from the device, {rate:F0} MB/s; {bad} bad sector(s)"
            + (spots.Count > 0 ? $" at {string.Join(", ", spots.Take(ListedBad).Select(w => $"{w / 1048576.0:F0} MiB"))}{(spots.Count > ListedBad ? ", ..." : "")}" : "")
            + (slow > 0 ? $"; {slow} read(s) over {SlowMs} ms (slowest {slowest:F0} ms)" : "")
            + (stoppedEarly ? $"; stopped after {StopAfterBad} bad sectors" : !complete ? "; stopped at the time limit before the end of the drive" : "");
        return new(bad, complete, detail);
    }

    /// <summary>Reads a failed block again 4 KiB at a time and returns how many of its sectors cannot be read, listing where the first ones are.</summary>
    private static long Probe(SafeFileHandle handle, NativeBlock buffer, long blockOffset, int length, List<long> spots, CancellationToken ct)
    {
        long bad = 0;
        for (int at = 0; at < length; at += StorageFile.Sector)
        {
            ct.ThrowIfCancellationRequested();
            try { RandomAccess.Read(handle, buffer.Span[..StorageFile.Sector], blockOffset + at); }
            catch (IOException) { bad++; if (spots.Count < 64) spots.Add(blockOffset + at); }
        }
        return bad;
    }

    private const uint GenericRead = 0x80000000, ShareReadWrite = 3, OpenExisting = 3, NoBuffering = 0x20000000, IoctlGetLengthInfo = 0x0007405C;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, nint input, int inputSize, out long output, int outputSize, out int returned, nint overlapped);
}
