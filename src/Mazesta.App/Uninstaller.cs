using System.Diagnostics; using System.Windows.Forms;
using Mazesta.Core.Tray; using Mazesta.Desktop.Localization; using Mazesta.Hardware.Lhm; using Mazesta.Persistence; using Microsoft.Win32;
namespace Mazesta.App;

/// <summary>
/// "Uninstall" for a copy that MazestaTestSetup put on the computer (Settings › Apps › Installed apps runs <c>Mazesta.exe --uninstall</c>). It
/// only acts on the folder the setup recorded for this very exe, so a portable copy or a developer's build is never touched. It asks whether the data
/// (settings, reports, history: <c>%LocalAppData%\Mazesta Test</c>, or the Data folder of a copy the earlier per-user setup made) goes too - by default it
/// stays, so a later install picks it up - stops the tray and OpenRGB, takes out the PawnIO driver the app installed, removes its start-up task, shortcuts
/// and the entry in Installed apps, then hands the deleting of the folder to a hidden PowerShell that waits for this process to end (a running exe cannot delete itself).
/// </summary>
internal static class Uninstaller
{
    public const string Argument = "--uninstall", UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MazestaTest", ShortcutName = "Mazesta Test";

    public static int Run()
    {
        string dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        RegistryKey? hive = Recorded(RegistryHive.LocalMachine, dir) ? Registry.LocalMachine : Recorded(RegistryHive.CurrentUser, dir) ? Registry.CurrentUser : null;
        if (hive is null) { Say(Loc.Get("Uninstall_NotInstalled"), MessageBoxIcon.Information); return 1; }
        if (Process.GetProcessesByName("Mazesta").Any(p => p.Id != Environment.ProcessId)) { Say(Loc.Get("Uninstall_Running"), MessageBoxIcon.Warning); return 1; }
        if (MessageBox.Show(Loc.Get("Uninstall_Ask"), Loc.Get("Uninstall_Title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
        // Yes: the data goes too; No: it stays; Cancel: nothing changes.
        var data = MessageBox.Show(Loc.Get("Uninstall_Data"), Loc.Get("Uninstall_Title"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (data == DialogResult.Cancel) return 0;
        bool deleteData = data == DialogResult.Yes, installedMode = File.Exists(Path.Combine(dir, AppPaths.InstalledMarker));

        foreach (var name in new[] { "MazestaTray", "OpenRGB" })
            foreach (var p in Process.GetProcessesByName(name))
                using (p)
                {
                    string? path = null; try { path = p.MainModule?.FileName; } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                    if (path is null || !path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;   // an OpenRGB of the user's own is left alone
                    try { p.Kill(); p.WaitForExit(3000); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
        // The driver the app put in (the signed PawnIO setup it carries); it is removed with the app, as the app is what installed it.
        string pawn = Path.Combine(dir, "Redist", PawnIoDriver.SetupFileName);
        if (File.Exists(pawn) && PawnIoDriver.IsInstalled())
            try { using var p = Process.Start(new ProcessStartInfo(pawn, "-uninstall -silent") { CreateNoWindow = true, UseShellExecute = false }); p?.WaitForExit(120_000); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        try { using var p = Process.Start(new ProcessStartInfo("schtasks.exe", StartupTask.DeleteArguments()) { CreateNoWindow = true, UseShellExecute = false }); p?.WaitForExit(10000); }
        catch (System.ComponentModel.Win32Exception) { }
        foreach (var desktop in new[] { Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolder.DesktopDirectory })
            try { File.Delete(Path.Combine(Environment.GetFolderPath(desktop), ShortcutName + ".lnk")); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (var programs in new[] { Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs })
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(programs), ShortcutName), true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        try { hive.DeleteSubKeyTree(UninstallKey, false); } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException) { }

        // Deleted after this process ends. Only the program folder's own contents are removed, and the data only when the user said so.
        string q = dir.Replace("'", "''"), profile = Path.GetDirectoryName(AppPaths.InstalledDataRoot())!.Replace("'", "''");
        string script = $"Start-Sleep -Seconds 3; Get-ChildItem -LiteralPath '{q}' -Force | " + (deleteData || installedMode ? "" : "Where-Object Name -ne 'Data' | ") + "Remove-Item -Recurse -Force -ErrorAction SilentlyContinue; "
            + (deleteData || installedMode ? $"Remove-Item -LiteralPath '{q}' -Force -ErrorAction SilentlyContinue; " : "")
            + (deleteData && installedMode ? $"Remove-Item -LiteralPath '{profile}' -Recurse -Force -ErrorAction SilentlyContinue" : "");
        Process.Start(new ProcessStartInfo("powershell.exe") { ArgumentList = { "-NoProfile", "-WindowStyle", "Hidden", "-Command", script }, CreateNoWindow = true, UseShellExecute = false });
        Say(Loc.Get(deleteData ? "Uninstall_Done" : "Uninstall_DoneKept"), MessageBoxIcon.Information);
        return 0;
    }

    private static bool Recorded(RegistryHive h, string dir)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(h, RegistryView.Registry64); using var key = root.OpenSubKey(UninstallKey);
            return key?.GetValue("InstallLocation") is string where && string.Equals(Path.GetFullPath(where).TrimEnd(Path.DirectorySeparatorChar), dir, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    private static void Say(string text, MessageBoxIcon icon) => MessageBox.Show(text, Loc.Get("Uninstall_Title"), MessageBoxButtons.OK, icon);
}
