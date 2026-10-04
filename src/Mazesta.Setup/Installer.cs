using System.Diagnostics; using System.IO.Compression; using System.Reflection;
using Microsoft.Win32;
namespace Mazesta.Setup;

/// <summary>What installing does, apart from the window: checks the target, replaces an earlier copy's files (never its Data), unpacks the carried
/// zip, makes the shortcuts and the entry in Installed apps. The uninstall side is <c>MazestaWeb.exe --uninstall</c> in the app itself.</summary>
internal static class Installer
{
    public const string AppExe = "MazestaWeb.exe", ShortcutName = "Mazesta Test", Publisher = "Mazesta", RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MazestaTest";
    public static string Version => typeof(Installer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Mazesta Test");
    public static bool HasPayload => typeof(Installer).Assembly.GetManifestResourceStream("payload.zip") is not null;

    /// <summary>Where an earlier setup put the app (from Installed apps' entry), if its folder is still there.</summary>
    public static string? InstalledFolder()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
        return key?.GetValue("InstallLocation") is string dir && File.Exists(Path.Combine(dir, AppExe)) ? dir : null;
    }

    /// <summary>Null when <paramref name="folder"/> can be installed into: it does not exist, is empty, or already holds an installed copy (replaced, Data kept).
    /// Anything else (a folder with other things in it) is refused, so the setup never clears a folder it did not make.</summary>
    public static string? Problem(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder)) return SetupText.BadFolder;
        string full = Path.GetFullPath(folder);
        if (Path.GetPathRoot(full)?.TrimEnd('\\') == full.TrimEnd('\\')) return SetupText.BadFolder;   // a drive's root
        if (!Directory.Exists(full) || !Directory.EnumerateFileSystemEntries(full).Any()) return null;
        return File.Exists(Path.Combine(full, AppExe)) ? null : SetupText.NotEmpty;
    }

    public static bool IsRunning() => Process.GetProcessesByName("MazestaWeb").Length + Process.GetProcessesByName("MazestaTray").Length > 0;

    public static void Install(string folder, bool desktop, IProgress<(int Percent, string Text)> progress)
    {
        folder = Path.GetFullPath(folder);
        using var payload = typeof(Installer).Assembly.GetManifestResourceStream("payload.zip") ?? throw new InvalidOperationException(SetupText.NoPayload);
        using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
        Directory.CreateDirectory(folder);
        progress.Report((2, SetupText.Replacing));
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder).Where(e => !string.Equals(Path.GetFileName(e), "Data", StringComparison.OrdinalIgnoreCase)))
        { if (Directory.Exists(entry)) Directory.Delete(entry, true); else File.Delete(entry); }
        var files = zip.Entries.Where(e => e.Name.Length > 0).ToList(); int done = 0;
        foreach (var entry in files)
        {
            string target = Path.GetFullPath(Path.Combine(folder, entry.FullName));
            if (!target.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(entry.FullName);   // never outside the folder
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
            progress.Report((5 + 85 * ++done / files.Count, SetupText.Copying));
        }
        string exe = Path.Combine(folder, AppExe);
        if (!File.Exists(exe)) throw new InvalidDataException(AppExe);
        progress.Report((93, SetupText.Shortcuts));
        string menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
        Directory.CreateDirectory(menu);
        Shortcut(Path.Combine(menu, ShortcutName + ".lnk"), exe, folder);
        string deskLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName + ".lnk");
        if (desktop) Shortcut(deskLink, exe, folder); else if (File.Exists(deskLink)) File.Delete(deskLink);
        progress.Report((97, SetupText.Registering));
        using var key = Registry.CurrentUser.CreateSubKey(RegistryKey);
        key.SetValue("DisplayName", "Mazesta Test"); key.SetValue("DisplayVersion", Version); key.SetValue("Publisher", Publisher);
        key.SetValue("InstallLocation", folder); key.SetValue("DisplayIcon", exe);
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall"); key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(files.Sum(f => f.Length) / 1024), RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
        progress.Report((100, SetupText.Done));
    }

    private static void Shortcut(string path, string target, string workDir)
    {
        var shell = Type.GetTypeFromProgID("WScript.Shell") ?? throw new PlatformNotSupportedException("WScript.Shell");
        dynamic sh = Activator.CreateInstance(shell)!;
        try { dynamic link = sh.CreateShortcut(path); link.TargetPath = target; link.WorkingDirectory = workDir; link.IconLocation = target + ",0"; link.Description = "Mazesta Test"; link.Save(); }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sh); }
    }
}
