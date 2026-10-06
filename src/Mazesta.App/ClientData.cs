using Microsoft.Win32;
namespace Mazesta.App;

/// <summary>Where the users' copy keeps its Data on this computer: next to the exe the setup installed (the setup records it in Installed apps), else the setup's
/// usual folder. The company's edition lists the reports found there; when nothing is, the technician picks the folder.</summary>
internal static class ClientData
{
    public static string? Find()
    {
        foreach (var dir in Candidates()) if (Mazesta.Reporting.ReportStore.FindReportsDirectory(Path.Combine(dir, "Data")) is not null) return Path.Combine(dir, "Data");
        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        string? recorded = null;
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MazestaTest"); recorded = key?.GetValue("InstallLocation") as string; }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        if (!string.IsNullOrWhiteSpace(recorded)) yield return recorded;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Mazesta Test");
    }
}
