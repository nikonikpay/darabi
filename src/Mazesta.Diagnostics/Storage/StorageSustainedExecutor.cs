using System.Diagnostics; using Mazesta.Diagnostics.Memory;
namespace Mazesta.Diagnostics.Storage;

/// <summary>
/// One long sequential write, timed in 256 MiB slices, then the whole file read back and checked. Most SSDs write first into a cache (part of the
/// flash used one bit per cell, SLC); when it is full they fall to the flash's own speed, which on QLC and DRAM-less drives can be slower than a hard
/// disk. A short benchmark never leaves the cache, so a drive that "does 3000 MB/s" can crawl through a game install or a long copy. The slices show
/// where the speed fell (about how big the cache is), the speed after it, and the slowest slice. A wrong byte or an I/O error fails the test; the speeds
/// are evidence, not a verdict - what is slow enough is the technician's call.
/// </summary>
public sealed class StorageSustainedExecutor : StorageExecutor
{
    public static readonly TestDefinition Spec = new(new TestId("storage.sustained"), "Test_Storage_Sustained", 900, CommonOptions("32768"));
    public override TestDefinition Definition => Spec;
    private const int SliceBlocks = 256;   // 256 MiB

    private protected override string Exercise(StorageFile file, TestExecutionRequest request, CancellationToken ct, ref long errors)
    {
        using var buffer = new NativeBlock(StorageFile.Block);
        var clock = Stopwatch.StartNew(); var slices = new List<double>(); long written = 0;
        var slice = Stopwatch.StartNew(); int inSlice = 0;
        for (long offset = 0, index = 0; offset < file.Length && clock.Elapsed.TotalSeconds < request.DurationSeconds; offset += StorageFile.Block, index++)
        {
            ct.ThrowIfCancellationRequested();
            MemoryPatterns.Fill(buffer.Span, MemoryPatterns.RandomPass, (int)index);
            file.Write(buffer.Span, offset); written += StorageFile.Block;
            if (++inSlice == SliceBlocks) { slices.Add(SliceBlocks * (double)StorageFile.Block / slice.Elapsed.TotalSeconds / 1e6); inSlice = 0; slice.Restart(); }
            Report(request, clock);
        }
        double writeSeconds = clock.Elapsed.TotalSeconds;
        if (written < file.Length) request.NoteWarning("Log_StorageSustained_Short", null, written >> 20, file.Length >> 20);
        long wrong = 0, shortReads = 0; var read = Stopwatch.StartNew();
        for (long offset = 0, index = 0; offset < written; offset += StorageFile.Block, index++)
        {
            ct.ThrowIfCancellationRequested();
            int n = file.Read(buffer.Span, offset);
            if (n < StorageFile.Block) { shortReads++; errors++; }
            else { long bad = MemoryPatterns.CountMismatches(buffer.Span, MemoryPatterns.RandomPass, (int)index); wrong += bad; errors += bad; }
        }
        var a = Analyse(slices);
        request.Note("Log_StorageSustained_Done", "MB/s per 256 MiB slice; the cache ends where the speed stays under 60 % of the first slices'", a.First, a.CacheEndGiB?.ToString("F1") ?? "-", a.After, a.Slowest);
        return $"sustained sequential write of {written >> 20} MiB in 256 MiB slices, unbuffered write-through: first {a.First:F0} MB/s"
             + (a.CacheEndGiB is { } at ? $", fell after {at:F1} GiB (write cache) to {a.After:F0} MB/s" : ", no lasting drop (the cache, if any, is larger than this write)")
             + $", slowest slice {a.Slowest:F0} MB/s, overall {written / Math.Max(0.001, writeSeconds) / 1e6:F0} MB/s; read back {written / Math.Max(0.001, read.Elapsed.TotalSeconds) / 1e6:F0} MB/s, every byte checked"
             + (shortReads > 0 ? $"; {shortReads} short read(s)" : "") + (wrong > 0 ? $"; {wrong} byte(s) read back wrong" : "");
    }

    /// <summary>The speed of the first slices (their median), where it fell for good - the first slice under 60 % of it from which the median of the next three is too
    /// of it, in GiB written before it - the median after that, and the slowest slice. No slices, all zero.</summary>
    internal static (double First, double? CacheEndGiB, double After, double Slowest) Analyse(IReadOnlyList<double> mbps)
    {
        if (mbps.Count == 0) return (0, null, 0, 0);
        static double Median(IEnumerable<double> v) { var s = v.Order().ToArray(); return s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2; }
        double first = Median(mbps.Take(3));
        for (int i = 1; i < mbps.Count; i++)
            if (mbps[i] < first * 0.6 && Median(mbps.Skip(i).Take(3)) < first * 0.6) return (first, i * SliceBlocks / 1024.0, Median(mbps.Skip(i)), mbps.Min());
        return (first, null, Median(mbps), mbps.Min());
    }
}
