using System.Diagnostics; using System.Windows.Forms;
using Mazesta.Core.Tray; using Mazesta.Desktop.Localization; using Microsoft.Win32;
namespace Mazesta.Web;

/// <summary>
/// "Uninstall" for a copy that MazestaTestSetup put on the computer (Settings › Apps › Installed apps runs <c>MazestaWeb.exe --uninstall</c>). It
/// only acts on the folder the setup recorded for this very exe, so a portable copy or a developer's build is never touched. It asks whether the data
/// (settings, reports, history: the Data folder) goes too - by default it stays, so a later install picks it up - stops the tray, removes its start-up
/// task, shortcuts and the entry in Installed apps, then hands the deleting of the folder to a hidden PowerShell that waits for this process to end
/// (a running exe cannot delete itself).
/// </summary>
internal static class Uninstaller
{
    public const string Argument = "--uninstall", RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MazestaTest", ShortcutName = "Mazesta Test";

    public static int Run()
    {
        string dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        using (var key = Registry.CurrentUser.OpenSubKey(RegistryKey))
            if (key?.GetValue("InstallLocation") is not string where || !string.Equals(Path.GetFullPath(where).TrimEnd(Path.DirectorySeparatorChar), dir, StringComparison.OrdinalIgnoreCase))
            { Say(Loc.Get("Uninstall_NotInstalled"), MessageBoxIcon.Information); return 1; }
        if (Process.GetProcessesByName("MazestaWeb").Any(p => p.Id != Environment.ProcessId)) { Say(Loc.Get("Uninstall_Running"), MessageBoxIcon.Warning); return 1; }
        if (MessageBox.Show(Loc.Get("Uninstall_Ask"), Loc.Get("Uninstall_Title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
        // Yes: the data goes too; No: it stays in the folder; Cancel: nothing changes.
        var data = MessageBox.Show(Loc.Get("Uninstall_Data"), Loc.Get("Uninstall_Title"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (data == DialogResult.Cancel) return 0;
        bool deleteData = data == DialogResult.Yes;

        foreach (var tray in Process.GetProcessesByName("MazestaTray")) { try { tray.Kill(); tray.WaitForExit(3000); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
        try { using var p = Process.Start(new ProcessStartInfo("schtasks.exe", StartupTask.DeleteArguments()) { CreateNoWindow = true, UseShellExecute = false }); p?.WaitForExit(10000); }
        catch (System.ComponentModel.Win32Exception) { }
        foreach (var link in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName + ".lnk") })
            try { File.Delete(link); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName), true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        try { Registry.CurrentUser.DeleteSubKeyTree(RegistryKey, false); } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException) { }

        // Deleted after this process ends. Only the folder's own contents are removed, and Data only when the user said so.
        string q = dir.Replace("'", "''");
        string script = $"Start-Sleep -Seconds 3; Get-ChildItem -LiteralPath '{q}' -Force | " + (deleteData ? "" : "Where-Object Name -ne 'Data' | ") + "Remove-Item -Recurse -Force -ErrorAction SilentlyContinue; "
            + (deleteData ? $"Remove-Item -LiteralPath '{q}' -Force -ErrorAction SilentlyContinue" : "");
        Process.Start(new ProcessStartInfo("powershell.exe") { ArgumentList = { "-NoProfile", "-WindowStyle", "Hidden", "-Command", script }, CreateNoWindow = true, UseShellExecute = false });
        Say(Loc.Get(deleteData ? "Uninstall_Done" : "Uninstall_DoneKept"), MessageBoxIcon.Information);
        return 0;
    }

    private static void Say(string text, MessageBoxIcon icon) => MessageBox.Show(text, Loc.Get("Uninstall_Title"), MessageBoxButtons.OK, icon);
}
