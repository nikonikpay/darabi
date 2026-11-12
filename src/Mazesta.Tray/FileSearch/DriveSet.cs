using Mazesta.Core.Search; using Mazesta.Hardware.Files;
namespace Mazesta.Tray.FileSearch;

/// <summary>
/// Every fixed drive's index for the quick file search. Read once in the background (NTFS from its file table, others by walking their
/// folders), then brought up to date only when the window opens (<see cref="Refresh"/>): an NTFS drive from its change journal, in
/// milliseconds; another drive read again when its copy is over half an hour old. Nothing runs while the window is closed.
/// </summary>
internal sealed class DriveSet : IDisposable
{
    private sealed class Drive(string root)
    {
        public string Root { get; } = root;
        public NtfsDrive? Ntfs; public volatile FileIndex? Index; public DateTime Read; public int Busy;
    }

    private static readonly TimeSpan WalkAge = TimeSpan.FromMinutes(30);
    private readonly List<Drive> _drives = []; private readonly CancellationTokenSource _stop = new(); private int _started;
    private readonly string? _log;

    /// <param name="log">A few lines per reading (names, time, memory), kept short, so the index's cost can be checked on any machine.</param>
    public DriveSet(string? log = null) => _log = log;

    /// <summary>Drives read and being read: the window's status line.</summary>
    public (int Ready, int All, long Names) State { get { var d = _drives.ToArray(); return (d.Count(x => x.Index is not null), d.Length, d.Sum(x => (long)(x.Index?.Count ?? 0))); } }
    public event Action? Changed;

    /// <summary>Every fixed drive found now, each read on a background thread of low priority (the system drive first).</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        string system = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).OrderBy(d => d.Name.Equals(system, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            _drives.Add(new Drive(d.Name));
        var all = _drives.ToArray();
        new Thread(() => { foreach (var d in all) Read(d); Compact(); }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Mazesta file index" }.Start();
    }

    /// <summary>The window is opening: each drive brought up to date (a journal read where there is one, else a new read in the background when old).</summary>
    public void Refresh()
    {
        foreach (var d in _drives.ToArray())
        {
            if (d.Index is not { } x || Volatile.Read(ref d.Busy) == 1) continue;
            try { if (d.Ntfs is { } n) { if (!n.CatchUp(x)) Again(d); } else if (DateTime.UtcNow - d.Read > WalkAge) Again(d); }
            catch (ObjectDisposedException) { }   // closing
        }
    }

    /// <summary>
    /// Every drive searched; the best <paramref name="max"/> of all of them by rank (the name itself, then its start, then anywhere; shorter
    /// names first), with the count of every match.
    /// </summary>
    public (List<(FileIndex Index, int Entry)> Hits, int Total) Search(FileQuery query, int max, CancellationToken ct)
    {
        var all = new List<(long Key, FileIndex Index, int Entry)>(); int total = 0;
        foreach (var d in _drives.ToArray())
        {
            if (d.Index is not { } x) continue;
            var best = new List<(long Key, int Entry)>();
            total += x.Search(query, max, best, ct);
            foreach (var (k, e) in best) all.Add((k, x, e));
        }
        return ([.. all.OrderBy(a => a.Key).ThenBy(a => a.Index.Root, StringComparer.Ordinal).Take(max).Select(a => (a.Index, a.Entry))], total);
    }

    private void Again(Drive d)
    {
        if (Interlocked.Exchange(ref d.Busy, 1) == 1) return;
        new Thread(() => { Read(d); Compact(); }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Mazesta file index " + d.Root }.Start();
    }

    private void Read(Drive d)
    {
        Volatile.Write(ref d.Busy, 1);
        try
        {
            if (_stop.IsCancellationRequested) return;
            d.Ntfs ??= NtfsDrive.TryOpen(d.Root, out _);
            FileIndex x; var clock = System.Diagnostics.Stopwatch.StartNew();
            try { x = d.Ntfs is { } n ? n.Build(_stop.Token) : FolderDrive.Walk(d.Root, _stop.Token); }
            catch (IOException) when (d.Ntfs is not null) { d.Ntfs.Dispose(); d.Ntfs = null; x = FolderDrive.Walk(d.Root, _stop.Token); }   // a table that could not be read: its folders are walked
            d.Index = x; d.Read = DateTime.UtcNow;
            Log($"{d.Root} {x.Count} names in {clock.ElapsedMilliseconds} ms ({(d.Ntfs is null ? "folders walked" : "file table")}), index {x.Bytes >> 20} MB");
            Changed?.Invoke();
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or IOException or UnauthorizedAccessException) { }   // closing, or a drive gone while read: left out
        finally { Volatile.Write(ref d.Busy, 0); }
    }

    // The arrays that served the reading are garbage now: they are returned to Windows, so the tray keeps only the names it needs.
    private void Compact()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        var info = GC.GetGCMemoryInfo(); using var me = System.Diagnostics.Process.GetCurrentProcess();
        Log($"all read: {State.Names} names; index {_drives.ToArray().Sum(d => d.Index?.Bytes ?? 0) >> 20} MB, managed heap {info.HeapSizeBytes >> 20} MB, committed {info.TotalCommittedBytes >> 20} MB, process private {me.PrivateMemorySize64 >> 20} MB");
    }

    private void Log(string line)
    {
        if (_log is null) return;
        try
        {
            if (File.Exists(_log) && new FileInfo(_log).Length > 64 * 1024) File.Delete(_log);   // a short log: started again when it grows
            File.AppendAllText(_log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _stop.Cancel();
        foreach (var d in _drives) { d.Ntfs?.Dispose(); d.Index = null; }
        _drives.Clear();
    }
}
