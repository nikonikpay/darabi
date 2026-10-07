using Microsoft.Win32;
namespace Mazesta.App;

/// <summary>Where the users' copy keeps its Data on this computer. A copy installed by the setup (Program Files) keeps it per user in the profile,
/// <c>%LocalAppData%\Mazesta Test\Data</c>; a copy installed by the earlier per-user setup kept it beside its exe (the setup records that folder in Installed apps,
/// for the machine and for the user). The company's edition and the flash copy list the reports found there; when nothing is, the technician picks the folder.</summary>
internal static class ClientData
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MazestaTest";

    public static string? Find()
    {
        foreach (var data in Candidates()) if (Mazesta.Reporting.ReportStore.FindReportsDirectory(data) is not null) return data;
        return null;
    }

    /// <summary>The first Data folder that exists (reports or not): what the flash copy reads and writes.</summary>
    public static string? FindExisting() => Candidates().FirstOrDefault(Directory.Exists);

    private static IEnumerable<string> Candidates()
    {
        yield return Mazesta.Persistence.AppPaths.InstalledDataRoot();
        foreach (var dir in InstallFolders()) yield return Path.Combine(dir, "Data");
    }

    private static IEnumerable<string> InstallFolders()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            string? recorded = null;
            try { using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64); using var key = root.OpenSubKey(UninstallKey); recorded = key?.GetValue("InstallLocation") as string; }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            if (!string.IsNullOrWhiteSpace(recorded)) yield return recorded;
        }
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Mazesta Test");
    }
}
