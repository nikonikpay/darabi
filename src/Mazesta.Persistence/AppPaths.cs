namespace Mazesta.Persistence;
public sealed class AppPaths
{
    public const string PortableMarker = "portable.marker";
    public bool IsPortable { get; private init; } public string DataRoot { get; private init; } = "";
    public string ConfigDir => Path.Combine(DataRoot, "config"); public string LogsDir => Path.Combine(DataRoot, "logs");
    public string SessionsDir => Path.Combine(DataRoot, "sessions"); public string HistoryDir => Path.Combine(DataRoot, "history");
    public string ConfigFile => Path.Combine(ConfigDir, "appconfig.json");
    public static AppPaths Create(string exeDirectory, string localAppData, bool portableMarkerExists) => portableMarkerExists
        ? new AppPaths { IsPortable = true, DataRoot = Path.Combine(exeDirectory, "Data") }
        : new AppPaths { IsPortable = false, DataRoot = Path.Combine(localAppData, "Mazesta", "Test") };
    public static AppPaths Detect()
    {
        string exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        return Create(exeDir, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), File.Exists(Path.Combine(exeDir, PortableMarker)));
    }
    public void EnsureDirectories() { foreach (var d in new[] { ConfigDir, LogsDir, SessionsDir, HistoryDir }) Directory.CreateDirectory(d); }
}
