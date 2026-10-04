using System.Diagnostics.Eventing.Reader; using Mazesta.Core.Crashes;
namespace Mazesta.Diagnostics.Crashes;

/// <summary>What was found, and what could not be looked at (so an empty list is not read as "this computer never crashed").</summary>
public sealed record CrashHistory(IReadOnlyList<CrashRecord> Crashes, IReadOnlyList<PowerLoss> PowerLosses, int DumpFiles, bool DumpsReadable, bool LogReadable, DateTimeOffset? LogSince);

/// <summary>
/// The blue screens Windows kept a record of: the dump files (C:\Windows\Minidump and MEMORY.DMP, their headers only) and the System log's own
/// line for each restart after a stop ("The computer has rebooted from a bugcheck", event 1001), joined into one list; and the restarts without a
/// stop code (Kernel-Power 41). It only reads. A computer set to keep no dumps, or whose log was cleared, has less to show: the oldest record
/// the log still holds is told with the result.
/// </summary>
public static class CrashReader
{
    private const int MaxDumps = 200, MaxEvents = 300, HeadBytes = 0x1040;

    public static CrashHistory Read()
    {
        var now = DateTimeOffset.UtcNow; var dumps = new List<CrashRecord>(); int files = 0; bool dumpsOk = true;
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        try
        {
            string folder = Path.Combine(win, "Minidump");
            var paths = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.dmp").OrderByDescending(File.GetLastWriteTimeUtc).Take(MaxDumps).ToList() : [];
            string full = Path.Combine(win, "MEMORY.DMP"); if (File.Exists(full)) paths.Add(full);
            foreach (string path in paths)
            {
                files++;
                if (Head(path) is { } head && BugCheckCatalog.ReadDump(head, path, new DateTimeOffset(File.GetLastWriteTimeUtc(path)), now) is { } crash
                    // MEMORY.DMP is the last crash once more, in full: listed only when its minidump is gone.
                    && !dumps.Any(d => d.Code == crash.Code && (d.At - crash.At).Duration() < TimeSpan.FromMinutes(2))) dumps.Add(crash);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { dumpsOk = false; }

        var logged = new List<CrashRecord>(); var losses = new List<PowerLoss>(); bool logOk = true; DateTimeOffset? since = null;
        try
        {
            foreach (var record in Events("*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and (EventID=1001)]]"))
                using (record)
                {
                    var texts = record.Properties.Select(p => p.Value?.ToString()).ToList();
                    if ((texts.Select(BugCheckCatalog.ParseLogText).FirstOrDefault(x => x is not null)) is not { } stop || record.TimeCreated is not { } at) continue;
                    string? dump = texts.FirstOrDefault(t => t is not null && t.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));
                    logged.Add(new(new DateTimeOffset(at), true, stop.Code, stop.Parameters, dump, null));
                }
            foreach (var record in Events("*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=41)]]"))
                using (record)
                    // The first value is the stop code Windows carried over the restart; 0 means there was none to carry.
                    if (record.TimeCreated is { } at && record.Properties.Count > 0 && Convert.ToUInt64(record.Properties[0].Value ?? 0UL, System.Globalization.CultureInfo.InvariantCulture) == 0) losses.Add(new(new DateTimeOffset(at)));
            using var oldest = new EventLogReader(new EventLogQuery("System", PathType.LogName, "*"));
            using var first = oldest.ReadEvent(); if (first?.TimeCreated is { } t) since = new DateTimeOffset(t);
        }
        catch (Exception e) when (e is EventLogException or UnauthorizedAccessException or InvalidCastException or FormatException or OverflowException) { logOk = false; }

        return new(BugCheckCatalog.Merge(dumps, logged), [.. losses.OrderByDescending(l => l.At)], files, dumpsOk, logOk, since);
    }

    private static IEnumerable<EventRecord> Events(string query)
    {
        using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, query) { ReverseDirection = true });
        int n = 0;
        for (var record = reader.ReadEvent(); record is not null && n++ < MaxEvents; record = reader.ReadEvent()) yield return record;
    }

    private static byte[]? Head(string path)
    {
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[HeadBytes]; int n = f.ReadAtLeast(head, HeadBytes, throwOnEndOfStream: false);
            return n >= 0x60 ? head[..n] : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
