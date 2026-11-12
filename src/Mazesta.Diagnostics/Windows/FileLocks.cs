using System.Diagnostics; using System.Runtime.InteropServices; using System.Text; using Mazesta.Core.Windows;
namespace Mazesta.Diagnostics.Windows;

/// <summary>
/// "Which program holds this file open?" through Windows' Restart Manager (the same API installers use to ask what to close), so the answer is
/// Windows' own, not a handle scan. A folder is checked file by file, up to <see cref="FolderCap"/> files. Closing ends the process after
/// checking it is still the same one (its id and start time), and never a service or a critical process.
/// </summary>
public static class FileLocks
{
    public const int FolderCap = 3000;   // ponytail: a bigger folder is checked only partly (said on the page); a handle scan would see all but is far heavier

    public static FileLockReport Find(IReadOnlyList<string> paths)
    {
        var files = new List<string>(); bool truncated = false;
        foreach (var p in paths)
        {
            if (File.Exists(p)) { files.Add(p); continue; }
            if (!Directory.Exists(p)) continue;
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var f in Directory.EnumerateFiles(p, "*", opts)) { if (files.Count >= FolderCap) { truncated = true; break; } files.Add(f); }
        }
        if (files.Count == 0) return new([], 0, false, null);

        var key = new StringBuilder(33);
        int err = RmStartSession(out uint session, 0, key);
        if (err != 0) return new([], files.Count, truncated, $"RmStartSession {err}");
        try
        {
            err = RmRegisterResources(session, (uint)files.Count, [.. files], 0, null, 0, null);
            if (err != 0) return new([], files.Count, truncated, $"RmRegisterResources {err}");
            uint count = 0, reasons = 0; RmProcessInfo[]? info = null;
            for (int tries = 0; tries < 5; tries++)
            {
                err = RmGetList(session, out uint needed, ref count, info, ref reasons);
                if (err == 0) break;
                if (err != ErrorMoreData) return new([], files.Count, truncated, $"RmGetList {err}");
                count = needed; info = new RmProcessInfo[needed];
            }
            if (err != 0) return new([], files.Count, truncated, $"RmGetList {err}");
            var holders = (info ?? []).Take((int)count)
                .Select(i => new FileLockHolder(i.Process.ProcessId, Stamp(i.Process.StartTime), Name(i), PathOf(i.Process.ProcessId), FileLockHolder.KindOf(i.ApplicationType)))
                .DistinctBy(h => (h.Pid, h.StartTime)).OrderBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            return new(holders, files.Count, truncated, null);
        }
        finally { _ = RmEndSession(session); }
    }

    /// <summary>Ends the process if it is still the one that was found. Null when it ended; otherwise why not.</summary>
    public static string? Close(FileLockHolder holder)
    {
        if (!holder.CanClose || holder.Pid == Environment.ProcessId) return "protected";
        try
        {
            using var p = Process.GetProcessById(holder.Pid);
            if (p.StartTime.ToFileTimeUtc() != holder.StartTime) return "gone";
            p.Kill(); return p.WaitForExit(5000) ? null : "timeout";
        }
        catch (ArgumentException) { return "gone"; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return ex.Message; }
    }

    /// <summary>The holder as it is now (its kind and name read again), so a page cannot ask to close what Windows did not report.</summary>
    public static FileLockHolder? Recheck(IReadOnlyList<string> paths, int pid, long start) => Find(paths).Holders.FirstOrDefault(h => h.Pid == pid && h.StartTime == start);

    private static long Stamp(System.Runtime.InteropServices.ComTypes.FILETIME t) => ((long)t.dwHighDateTime << 32) | (uint)t.dwLowDateTime;
    private static string Name(RmProcessInfo i) => !string.IsNullOrWhiteSpace(i.AppName) ? i.AppName : !string.IsNullOrWhiteSpace(i.ServiceShortName) ? i.ServiceShortName : $"PID {i.Process.ProcessId}";
    private static string? PathOf(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.MainModule?.FileName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return null; }
    }

    private const int ErrorMoreData = 234;
    [StructLayout(LayoutKind.Sequential)] private struct RmUniqueProcess { public int ProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME StartTime; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceShortName;
        public int ApplicationType; public uint AppStatus; public uint SessionId; [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmStartSession(out uint session, int flags, StringBuilder key);
    [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint session);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint files, string[] names, uint apps, RmUniqueProcess[]? processes, uint services, string[]? serviceNames);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] RmProcessInfo[]? info, ref uint rebootReasons);
}
