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

    /// <summary>Services and Windows' critical processes are never closed from here: ending one can stop Windows or lose its data.</summary>
    public bool CanClose => Kind is not (FileLockKind.Service or FileLockKind.Critical) && Pid > 4;
}

/// <summary>The answer to "who holds these files": <see cref="Error"/> set means Windows could not tell, which is not "nobody".</summary>
public sealed record FileLockReport(IReadOnlyList<FileLockHolder> Holders, int FilesChecked, bool Truncated, string? Error);
