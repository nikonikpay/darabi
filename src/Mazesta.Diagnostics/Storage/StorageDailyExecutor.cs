using System.Diagnostics; using Mazesta.Diagnostics.Memory;
namespace Mazesta.Diagnostics.Storage;

/// <summary>
/// The drive as Windows uses it in a working day, every byte checked: a drive that passes a long sequential run can still fail in the middle of
/// ordinary work - a request after the drive went to a low-power state, a burst of small mixed requests, a copy through the file system, many
/// small files. One round goes through:
///  - mixed requests: bursts of 5-200 requests of 4 KiB to 1 MiB, 70 % reads and 30 % writes at random places, with pauses of 20 ms to 3 s
///    between bursts (long enough for the drive and its link to drop into their power-saving states); every read is checked against what was
///    last written to each 4 KiB of the file;
///  - a copy: a 128 MiB file written the ordinary (cached) way, copied with Windows' own copy, and the copy read back from the drive uncached;
///  - small files: 200 files of 1-64 KiB written, read back and deleted (the file system's metadata, not only data);
///  - idle: 10-60 s of nothing, then one checked read whose latency is the drive's wake-up time.
/// Rounds repeat until the time is up. A wrong byte, a short read or an I/O error fails the test; requests slower than a second are reported
/// as stalls (with the slowest wake-up) but are evidence, not the verdict. Windows' storage events during the run are added by the engine.
/// </summary>
public sealed class StorageDailyExecutor : StorageExecutor
{
    public static readonly TestDefinition Spec = new(new TestId("storage.daily"), "Test_Storage_Daily", 1800, CommonOptions("1024"));
    public override TestDefinition Definition => Spec;

    private const int CopyMb = 128, SmallFiles = 200, Sector = StorageFile.Sector;
    private const double StallMs = 1000;

    private protected override string Exercise(StorageFile file, TestExecutionRequest request, CancellationToken ct, ref long errors)
    {
        using var buffer = new NativeBlock(StorageFile.Block);
        var clock = Stopwatch.StartNew(); var rng = new Random(Environment.TickCount);
        long slots = file.Length / Sector; var version = new int[slots];
        // Every 4 KiB of the file written first: a place never written reads as zero without touching the media.
        for (long offset = 0; offset < file.Length; offset += StorageFile.Block) { ct.ThrowIfCancellationRequested(); FillSlots(buffer.Span, offset / Sector, version); file.Write(buffer.Span, offset); Report(request, clock); }
        var latency = new LatencyHistogram(); long requests = 0, wrong = 0, shortReads = 0, stalls = 0, copied = 0, smallFiles = 0; int rounds = 0; double wakeMax = 0;
        bool More() => clock.Elapsed.TotalSeconds < request.DurationSeconds && !ct.IsCancellationRequested;
        double Took(long since) { double ms = Stopwatch.GetElapsedTime(since).TotalMilliseconds; latency.Add(ms); if (ms > StallMs) stalls++; return ms; }
        void Pause(int ms) { ct.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(Math.Min(ms, Math.Max(0, (request.DurationSeconds - clock.Elapsed.TotalSeconds) * 1000)))); ct.ThrowIfCancellationRequested(); }

        while (More())
        {
            request.Note("Log_StorageDaily_Round", null, rounds + 1, "@Test_StorageDaily_Mixed");
            // ——— mixed requests in bursts, with pauses that let the drive go to sleep ———
            for (var phase = Stopwatch.StartNew(); phase.Elapsed.TotalSeconds < 30 && More();)
            {
                int burst = rng.Next(5, 201);
                for (int r = 0; r < burst && More(); r++)
                {
                    int sectors = rng.Next(100) switch { < 55 => 1, < 80 => 4, < 95 => 16, _ => 256 };
                    long first = rng.NextInt64(slots - sectors + 1); var span = buffer.Span[..(sectors * Sector)];
                    if (rng.Next(100) < 30)
                    {
                        for (int s = 0; s < sectors; s++) version[first + s]++;
                        FillSlots(span, first, version);
                        long t = Stopwatch.GetTimestamp(); file.Write(span, first * Sector); Took(t);
                    }
                    else
                    {
                        long t = Stopwatch.GetTimestamp(); int n = file.Read(span, first * Sector); Took(t);
                        if (n < span.Length) { shortReads++; errors++; continue; }
                        long bad = CheckSlots(span, first, version); wrong += bad; errors += bad;
                    }
                    requests++;
                }
                Report(request, clock);
                Pause(rng.Next(20, 3001));
            }
            // ——— a copy through the file system, read back from the drive ———
            if (More()) { request.Note("Log_StorageDaily_Round", null, rounds + 1, "@Test_StorageDaily_Copy"); long bad = Copy(file.Folder, buffer, rounds, ct); wrong += bad; errors += bad; copied += CopyMb; Report(request, clock); }
            // ——— many small files ———
            if (More()) { request.Note("Log_StorageDaily_Round", null, rounds + 1, "@Test_StorageDaily_Small"); long bad = Small(file.Folder, rng, ct); wrong += bad; errors += bad; smallFiles += SmallFiles; Report(request, clock); }
            // ——— idle, then one read that wakes the drive ———
            if (More())
            {
                request.Note("Log_StorageDaily_Round", null, rounds + 1, "@Test_StorageDaily_Idle");
                Pause(rng.Next(10_000, 60_001));
                if (More())
                {
                    long slot = rng.NextInt64(slots); var into = buffer.Span[..Sector];
                    long t = Stopwatch.GetTimestamp(); int n = file.Read(into, slot * Sector); double ms = Took(t);
                    wakeMax = Math.Max(wakeMax, ms); requests++;
                    if (n < Sector) { shortReads++; errors++; } else { long bad = CheckSlots(into, slot, version); wrong += bad; errors += bad; }
                }
            }
            rounds++;
            request.Note("Log_StorageDaily_Done", null, rounds, requests, latency.Percentile(0.99), stalls, errors);
        }
        ct.ThrowIfCancellationRequested();
        return $"everyday drive use; {rounds} round(s); {requests} verified mixed request(s) 4 KiB-1 MiB, 70/30 read/write, latency {latency.Describe()}; "
             + $"{copied} MiB copied through the file system and read back uncached; {smallFiles} small files written, read and deleted; "
             + $"slowest wake from idle {wakeMax:F1} ms; {stalls} request(s) over {StallMs:0} ms"
             + (shortReads > 0 ? $"; {shortReads} short read(s)" : "") + (wrong > 0 ? $"; {wrong} byte(s) read back wrong" : "");
    }

    /// <summary>Writes each 4 KiB of <paramref name="span"/> with the data of its slot's current version.</summary>
    private static void FillSlots(Span<byte> span, long firstSlot, int[] version)
    {
        for (int s = 0; s * Sector < span.Length; s++) MemoryPatterns.Fill(span.Slice(s * Sector, Sector), Pass(version[firstSlot + s]), (int)(firstSlot + s));
    }

    private static long CheckSlots(ReadOnlySpan<byte> span, long firstSlot, int[] version)
    {
        long bad = 0;
        for (int s = 0; s * Sector < span.Length; s++) bad += MemoryPatterns.CountMismatches(span.Slice(s * Sector, Sector), Pass(version[firstSlot + s]), (int)(firstSlot + s));
        return bad;
    }

    private static int Pass(int v) => MemoryPatterns.RandomPass + (v % 4096) * MemoryPatterns.Count;

    /// <summary>A file written the ordinary way, copied by Windows, and the copy read back uncached; the wrong bytes in the copy.</summary>
    internal static long Copy(string folder, NativeBlock buffer, int round, CancellationToken ct)
    {
        string source = Path.Combine(folder, $".mazesta-copy-{Guid.NewGuid():N}.tmp"), target = source + ".copy";
        try
        {
            using (var w = new FileStream(source, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
                for (int i = 0; i < CopyMb; i++) { ct.ThrowIfCancellationRequested(); MemoryPatterns.Fill(buffer.Span, MemoryPatterns.RandomPass + round * MemoryPatterns.Count, i); w.Write(buffer.Span); }
            File.Copy(source, target);
            long bad = 0;
            using var back = StorageFile.OpenRead(target);
            for (int i = 0; i < CopyMb; i++)
            {
                ct.ThrowIfCancellationRequested();
                int n = back.Read(buffer.Span, (long)i * StorageFile.Block);
                bad += n < StorageFile.Block ? StorageFile.Block - n : MemoryPatterns.CountMismatches(buffer.Span, MemoryPatterns.RandomPass + round * MemoryPatterns.Count, i);
            }
            return bad;
        }
        finally { TryDelete(source); TryDelete(target); }
    }

    /// <summary>Small files written, read back and deleted; the wrong bytes among them (a missing or short file counts all its bytes).</summary>
    internal static long Small(string folder, Random rng, CancellationToken ct)
    {
        string dir = Path.Combine(folder, $".mazesta-small-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var sizes = Enumerable.Range(0, SmallFiles).Select(_ => rng.Next(1, 65) << 10).ToArray(); var data = new byte[64 << 10];
            for (int i = 0; i < sizes.Length; i++) { ct.ThrowIfCancellationRequested(); MemoryPatterns.Fill(data.AsSpan(0, sizes[i]), MemoryPatterns.RandomPass, i); File.WriteAllBytes(Path.Combine(dir, $"{i}.bin"), data.AsSpan(0, sizes[i])); }
            long bad = 0;
            for (int i = 0; i < sizes.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                byte[] back = File.ReadAllBytes(Path.Combine(dir, $"{i}.bin"));
                bad += back.Length != sizes[i] ? sizes[i] : MemoryPatterns.CountMismatches(back, MemoryPatterns.RandomPass, i);
            }
            return bad;
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}
