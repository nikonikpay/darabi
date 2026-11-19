namespace Mazesta.Core.Windows;

/// <summary>What kind of program holds a file open, as Windows' Restart Manager names it (RM_APP_TYPE).</summary>
public enum FileLockKind { Other, Window, Service, Explorer, Console, Critical }

/// <summary>One program that holds a file open. <see cref="StartTime"/> (FILETIME, UTC) pins the process: a closed one's id can be reused by another.</summary>
public sealed record FileLockHolder(int Pid, long StartTime, string Name, string? Path, FileLockKind Kind)
{
    public static FileLockKind KindOf(int rmAppType) => rmAppType switch
    {
        1 or 2 => FileLockKind.Window, 3 => FileLockKind.Service, 4 => FileLockKind.Explorer, 5 => FileLockKind.Console, 1000 => FileLockKind.Critical, _ => FileLockKind.Other,
    };

    private static readonly HashSet<string> SystemNames = new(StringComparer.OrdinalIgnoreCase) { "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "dwm", "fontdrvhost", "svchost" };

    /// <summary>
    /// Services and Windows' own processes are never closed from here: ending one can stop Windows or lose its data. Restart Manager's "critical"
    /// label is not used for this: it also marks an ordinary elevated program it cannot restart, such as the owner's own app.
    /// <c>FileLocks.Close</c> adds the kernel's own check (a process marked critical) before it ends anything.
    /// </summary>
    public bool CanClose => Kind is not FileLockKind.Service && Pid > 4 && !SystemNames.Contains(System.IO.Path.GetFileNameWithoutExtension(Path ?? Name));
}

/// <summary>The answer to "who holds these files": <see cref="Error"/> set means Windows could not tell, which is not "nobody".</summary>
public sealed record FileLockReport(IReadOnlyList<FileLockHolder> Holders, int FilesChecked, bool Truncated, string? Error);
