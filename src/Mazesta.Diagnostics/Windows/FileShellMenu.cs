using Mazesta.Core.Tray; using Microsoft.Win32;
namespace Mazesta.Diagnostics.Windows;

/// <summary>
/// The file tools' entries in Explorer's right-click menu, in the current user's own part of the registry (nothing of the system's is changed):
/// "what holds it open" on files and folders, "make smaller" on pictures, "rename together" on files. Each starts the tray program without
/// administrator rights (<see cref="ShellFiles"/>), so a selection of many files gives no UAC prompt. Windows 11 shows them under "Show more options".
/// </summary>
public static class FileShellMenu
{
    private static readonly (string Key, string Tool)[] Entries =
    [
        (@"Software\Classes\*\shell\MazestaLocks", ShellFiles.Lock), (@"Software\Classes\Directory\shell\MazestaLocks", ShellFiles.Lock),
        (@"Software\Classes\SystemFileAssociations\image\shell\MazestaResize", ShellFiles.Resize),
        (@"Software\Classes\*\shell\MazestaRename", ShellFiles.Rename),
    ];

    public static string Command(string trayExe, string tool) => $"\"{trayExe}\" {ShellFiles.Argument} {tool} \"%1\"";

    /// <summary>The entries made (or made again) for this tray program and these labels; a moved copy or another language is put right at the next start.</summary>
    public static void Set(string trayExe, Func<string, string> label)
    {
        foreach (var (path, tool) in Entries)
        {
            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue("", label(tool)); key.SetValue("Icon", $"\"{trayExe}\",0");
            key.SetValue("MultiSelectModel", "Player");   // shown for any number of files, not only up to 15
            using var command = key.CreateSubKey("command"); command.SetValue("", Command(trayExe, tool));
        }
    }

    /// <summary>The command each entry starts now, or null where there is none.</summary>
    public static IReadOnlyList<string?> Registered() => [.. Entries.Select(e => { using var c = Registry.CurrentUser.OpenSubKey(e.Key + @"\command"); return c?.GetValue("") as string; })];

    /// <summary>Takes them out (the uninstaller's call).</summary>
    public static void RemoveAll()
    {
        foreach (var (path, _) in Entries)
            try { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
    }
}
