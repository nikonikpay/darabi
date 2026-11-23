using System.Diagnostics; using Mazesta.Diagnostics.Memory;
namespace Mazesta.Diagnostics.Storage;

public enum ProbeStatus { Ok, Slow, ReadOnly, IoError, Skipped }

/// <summary>What one drive did in the probe. Speeds are MB/s of the whole file, <see cref="RandomMs"/> the mean of 4 KiB reads at random places.</summary>
public sealed record DriveProbeResult(string Root, ProbeStatus Status, double? WriteMBps, double? ReadMBps, double? RandomMs, string? Detail);

/// <summary>
/// The daily practical check of a fixed drive, beyond what its SMART data says: a 64 MiB file is written to the drive (unbuffered, so it reaches the
/// media), read back and compared byte for byte, then read at 256 random places. A drive that cannot be written to is read-only, one that fails or
/// returns wrong bytes is faulty, and one that is far slower than any drive of its kind should be is slow even when its health reads fine. A busy drive
/// can look slow once, so a slow result is measured again after a pause and the better of the two counts. The file is deleted when it closes.
/// </summary>
public static class DriveProbe
{
    private const int Mb = 64, Reads = 256, Sector = StorageFile.Sector;
    private const long FreeNeeded = 2L << 30;
    private const int ErrorWriteProtect = 19;

    /// <summary>The slowest a healthy drive of this kind goes (MB/s sequential, ms for a random 4 KiB read): an SSD under 100 MB/s or over 5 ms, a hard disk under 30 MB/s or over 60 ms.</summary>
    public static bool IsSlow(string? media, double? write, double? read, double? randomMs)
    {
        bool ssd = media == "SSD";
        double minMbps = ssd ? 100 : 30, maxMs = ssd ? 5 : 60;
        return write < minMbps || read < minMbps || randomMs > maxMs;
    }

    public static DriveProbeResult Run(string root, string? media, CancellationToken ct = default)
    {
        var first = Once(root, media, ct);
        if (first.Status != ProbeStatus.Slow) return first;
        ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
        var again = Once(root, media, ct);
        return again.Status == ProbeStatus.Ok || again.Status == ProbeStatus.Slow && again.ReadMBps + again.WriteMBps > first.ReadMBps + first.WriteMBps ? again : first;
    }

    private static DriveProbeResult Once(string root, string? media, CancellationToken ct)
    {
        string dir = Path.Combine(root, ".mazesta-probe");
        try
        {
            var info = new DriveInfo(root);
            if (!info.IsReady) return new(root, ProbeStatus.IoError, null, null, null, "not ready");
            if (info.AvailableFreeSpace < FreeNeeded) return new(root, ProbeStatus.Skipped, null, null, null, "little free space");
            Directory.CreateDirectory(dir);
            long length = StorageFile.LengthOf(Mb); using var buffer = new NativeBlock(StorageFile.Block);
            using var file = StorageFile.Create(dir, length, writeThrough: true);
            var clock = Stopwatch.StartNew();
            for (long o = 0; o < length; o += StorageFile.Block) { ct.ThrowIfCancellationRequested(); MemoryPatterns.Fill(buffer.Span, MemoryPatterns.RandomPass, (int)(o / StorageFile.Block)); file.Write(buffer.Span, o); }
            double write = Mbps(length, clock);
            clock.Restart(); long wrong = 0;
            for (long o = 0; o < length; o += StorageFile.Block)
            {
                ct.ThrowIfCancellationRequested();
                int n = file.Read(buffer.Span, o); wrong += n < StorageFile.Block ? StorageFile.Block - n : MemoryPatterns.CountMismatches(buffer.Span, MemoryPatterns.RandomPass, (int)(o / StorageFile.Block));
            }
            double read = Mbps(length, clock);
            if (wrong > 0) return new(root, ProbeStatus.IoError, write, read, null, $"{wrong} byte(s) read back wrong");
            var rng = new Random(Environment.TickCount); var one = buffer.Span[..Sector]; long slots = length / Sector; clock.Restart();
            for (int i = 0; i < Reads; i++) { ct.ThrowIfCancellationRequested(); if (file.Read(one, rng.NextInt64(slots) * Sector) < Sector) return new(root, ProbeStatus.IoError, write, read, null, "short read"); }
            double ms = clock.Elapsed.TotalMilliseconds / Reads;
            return new(root, IsSlow(media, write, read, ms) ? ProbeStatus.Slow : ProbeStatus.Ok, write, read, ms, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return new(root, ProbeStatus.Skipped, null, null, null, "no access"); }   // not a fault of the drive: this account may not write there
        catch (IOException e) when ((e.HResult & 0xFFFF) == ErrorWriteProtect) { return new(root, ProbeStatus.ReadOnly, null, null, null, e.Message); }
        catch (IOException e) { return new(root, ProbeStatus.IoError, null, null, null, e.Message); }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static double Mbps(long bytes, Stopwatch s) => bytes / 1048576.0 / Math.Max(s.Elapsed.TotalSeconds, 0.001);
}
