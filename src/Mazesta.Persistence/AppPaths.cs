namespace Mazesta.Persistence;

/// <summary>
/// The app is portable only: everything it writes - settings, logs, reports, history, test checkpoints and the PDF printer's browser
/// cache - lives in <c>Data</c> next to the app's exe (the tray, which sits beside it, reads the same folder). The whole folder can
/// be copied to a USB stick or another machine and keeps its results; nothing is written to the user profile.
/// </summary>
public sealed class AppPaths
{
    public const string DataFolderName = "Data";
    public string DataRoot { get; private init; } = "";
    /// <summary>Where logs and downloaded caches go: <see cref="DataRoot"/>, except in a flash-drive copy, which keeps them on the drive.</summary>
    public string LocalRoot { get; private init; } = "";
    /// <summary>A copy run from a technician's flash drive (<see cref="FlashFile"/> beside the exe): it reads and writes the installed copy's Data on the customer's PC.</summary>
    public bool Flash { get; private init; }
    public const string FlashFile = "flash.json";
    public string ConfigDir => Path.Combine(DataRoot, "config"); public string LogsDir => Path.Combine(LocalRoot, "logs");
    public string SessionsDir => Path.Combine(DataRoot, "sessions"); public string HistoryDir => Path.Combine(DataRoot, "history"); public string ReportsDir => Path.Combine(DataRoot, "reports");
    public string CacheDir => Path.Combine(LocalRoot, "cache");
    /// <summary>Downloaded releases, their unpacked copy and the files a release replaced (for going back by hand).</summary>
    public string UpdateDir => Path.Combine(CacheDir, "update");
    /// <summary>The benchmark comparison lists downloaded from the shop's site.</summary>
    public string BenchDbDir => Path.Combine(LocalRoot, "benchdb");
    /// <summary>The comparison lists read from the site's plugin (built there from the uploaded runs), beside the signed ones of the update folder.</summary>
    public string BenchSiteDir => Path.Combine(LocalRoot, "benchdb-site");
    public string ConfigFile => Path.Combine(ConfigDir, "appconfig.json");
    public static AppPaths Create(string exeDirectory) { string data = Path.Combine(exeDirectory, DataFolderName); return new() { DataRoot = data, LocalRoot = data }; }
    /// <summary>The flash-drive copy: settings, reports and history are the installed copy's <paramref name="installedData"/> (the drive's own Data folder when
    /// there is no installed copy); logs and caches stay on the drive, so the customer's disk only gets what the app itself is for.</summary>
    public static AppPaths CreateFlash(string exeDirectory, string? installedData)
        => new() { DataRoot = installedData ?? Path.Combine(exeDirectory, DataFolderName), LocalRoot = Path.Combine(exeDirectory, "Local"), Flash = true };
    public static AppPaths Detect() => Create(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
    public void EnsureDirectories() { foreach (var d in new[] { ConfigDir, LogsDir, SessionsDir, HistoryDir, ReportsDir, CacheDir }) Directory.CreateDirectory(d); }

    /// <summary>Where versions before the portable-only change kept their data (%LocalAppData%\Mazesta\Test).</summary>
    public static string LegacyDataRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mazesta", "Test");

    /// <summary>On the first start of a portable copy (no Data folder yet), copies an earlier installed version's settings and reports in,
    /// so they are not left behind. The old folder is not changed or deleted. Returns whether anything was copied.</summary>
    public bool AdoptLegacyData(string legacyRoot)
    {
        if (Directory.Exists(DataRoot) || !Directory.Exists(legacyRoot)) return false;
        foreach (var file in Directory.EnumerateFiles(legacyRoot, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(DataRoot, Path.GetRelativePath(legacyRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
        return true;
    }
}
