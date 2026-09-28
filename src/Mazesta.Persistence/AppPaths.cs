namespace Mazesta.Persistence;

/// <summary>
/// The app is portable only: everything it writes - settings, logs, reports, history, test checkpoints and the PDF printer's browser
/// cache - lives in <c>Data</c> next to MazestaWeb.exe (the tray, which sits beside it, reads the same folder). The whole folder can
/// be copied to a USB stick or another machine and keeps its results; nothing is written to the user profile.
/// </summary>
public sealed class AppPaths
{
    public const string DataFolderName = "Data";
    public string DataRoot { get; private init; } = "";
    public string ConfigDir => Path.Combine(DataRoot, "config"); public string LogsDir => Path.Combine(DataRoot, "logs");
    public string SessionsDir => Path.Combine(DataRoot, "sessions"); public string HistoryDir => Path.Combine(DataRoot, "history"); public string ReportsDir => Path.Combine(DataRoot, "reports");
    public string CacheDir => Path.Combine(DataRoot, "cache");
    public string ConfigFile => Path.Combine(ConfigDir, "appconfig.json");
    public static AppPaths Create(string exeDirectory) => new() { DataRoot = Path.Combine(exeDirectory, DataFolderName) };
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
