using System.Diagnostics; using System.IO; using System.Reflection; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input; using Mazesta.Core.Text; using Mazesta.Core.Tray; using Mazesta.Desktop.Localization; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfig _config; private readonly JsonStore<AppConfig> _store; private readonly PollingEngine _engine; private readonly MonitoringOptions _options; private readonly ShellViewModel _shell; private readonly Action<string> _openFolder;
    public string[] Languages => ["en", "fa"];
    public string[] RenderModes => ["auto", "software"];
    [ObservableProperty] private string _language; [ObservableProperty] private string _renderMode; [ObservableProperty] private string _fastIntervalText; [ObservableProperty] private string _storageIntervalText; [ObservableProperty] private string _shopName; [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _trayFirstCheckText; [ObservableProperty] private string _trayIdleText; [ObservableProperty] private string _trayWatchText;
    [ObservableProperty] private bool _isStartupRegistered; [ObservableProperty] private string _startupStatusText = "";
    public string DataFolder { get; } public string ModeText { get; } public string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
    public SettingsViewModel(AppConfig config, JsonStore<AppConfig> store, AppPaths paths, PollingEngine engine, MonitoringOptions options, ShellViewModel shell, Action<string> openFolder)
    {
        _config = config; _store = store; _engine = engine; _options = options; _shell = shell; _openFolder = openFolder;
        _language = config.Language; _renderMode = config.RenderMode; _fastIntervalText = config.FastIntervalSeconds.ToString(); _storageIntervalText = config.StorageIntervalSeconds.ToString(); _shopName = config.ShopName;
        _trayFirstCheckText = config.TrayFirstCheckSeconds.ToString(); _trayIdleText = config.TrayIdleIntervalMinutes.ToString(); _trayWatchText = config.TrayWatchIntervalSeconds.ToString();
        DataFolder = paths.DataRoot; ModeText = Loc.Get(paths.IsPortable ? "Settings_Mode_Portable" : "Settings_Mode_Installed");
        RefreshStartupStatus();
    }
    internal bool TryValidate(out int fast, out int storage, out string error)
    {
        error = ""; storage = 0;
        if (!PersianDigits.TryParseInt(FastIntervalText, out fast) || !MonitoringOptions.AllowedFastSeconds.Contains(fast)) { error = Loc.Get("Settings_Invalid_Interval"); return false; }
        if (!PersianDigits.TryParseInt(StorageIntervalText, out storage) || storage < 60) { error = Loc.Get("Settings_Invalid_Interval"); return false; }
        return true;
    }
    internal bool TryValidateTray(out int firstCheck, out int idle, out int watch, out string error)
    {
        error = ""; idle = 0; watch = 0;
        if (!PersianDigits.TryParseInt(TrayFirstCheckText, out firstCheck) || firstCheck is < 5 or > 300) { error = Loc.Get("Settings_Invalid_Interval"); return false; }
        if (!PersianDigits.TryParseInt(TrayIdleText, out idle) || idle is < 1 or > 120) { error = Loc.Get("Settings_Invalid_Interval"); return false; }
        if (!PersianDigits.TryParseInt(TrayWatchText, out watch) || watch is < 5 or > 300) { error = Loc.Get("Settings_Invalid_Interval"); return false; }
        return true;
    }
    [RelayCommand] private void Save()
    {
        if (!TryValidate(out int fast, out int storage, out string error)) { Message = error; return; }
        if (!TryValidateTray(out int trayFirst, out int trayIdle, out int trayWatch, out error)) { Message = error; return; }
        bool restartNeeded = _config.Language != Language || _config.RenderMode != RenderMode;
        _config.Language = Language; _config.RenderMode = RenderMode; _config.FastIntervalSeconds = fast; _config.StorageIntervalSeconds = storage; _config.ShopName = ShopName.Trim().Length == 0 ? _config.ShopName : ShopName.Trim();
        _config.TrayFirstCheckSeconds = trayFirst; _config.TrayIdleIntervalMinutes = trayIdle; _config.TrayWatchIntervalSeconds = trayWatch;
        bool storageChanged = _options.StorageInterval != TimeSpan.FromSeconds(storage);
        _options.StorageInterval = TimeSpan.FromSeconds(storage); if (_engine.FastInterval != TimeSpan.FromSeconds(fast)) _engine.SetFastInterval(TimeSpan.FromSeconds(fast));
        // The storage cadence can be up to 15 minutes, so without re-arming, a shortened interval
        // would not take effect until the old one had elapsed.
        if (storageChanged) _engine.RearmStorageNodes();
        bool saved = _store.Save(_config); _shell.RefreshInterval();
        Message = (saved ? Loc.Get("Settings_Saved") : Loc.Get("Settings_SaveFailed")) + (restartNeeded ? " " + Loc.Get("Settings_RestartNote") : "");
    }
    [RelayCommand] private void OpenFolder() => _openFolder(DataFolder);

    /// <summary>Whether the tray is set to start with Windows is asked of Task Scheduler itself, not
    /// stored in our own config - a stored flag could drift from reality (task removed by the user
    /// or by Windows) and the honesty rule (spec) forbids showing a state we have not actually checked.</summary>
    private void RefreshStartupStatus()
    {
        try { IsStartupRegistered = StartupTask.IsRegistered(RunSchtasks(StartupTask.QueryArguments()).Output); StartupStatusText = Loc.Get(IsStartupRegistered ? "Settings_Tray_StartupOn" : "Settings_Tray_StartupOff"); }
        catch (Exception e) { IsStartupRegistered = false; StartupStatusText = Loc.Format("Settings_Tray_StartupCheckFailed", e.Message); }
    }

    private static string? FindTrayExe()
    {
        string candidate = Path.Combine(AppContext.BaseDirectory, "MazestaTray.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static (int ExitCode, string Output) RunSchtasks(string arguments)
    {
        var psi = new ProcessStartInfo("schtasks.exe", arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("schtasks.exe did not start");
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    [RelayCommand]
    private void EnableStartup()
    {
        if (FindTrayExe() is not { } exe) { Message = Loc.Get("Settings_Tray_ExeNotFound"); return; }
        try
        {
            var (code, output) = RunSchtasks(StartupTask.RegisterArguments(exe));
            Message = code == 0 ? Loc.Get("Settings_Tray_Registered") : Loc.Format("Settings_Tray_RegisterFailed", output.Trim());
        }
        catch (Exception e) { Message = Loc.Format("Settings_Tray_RegisterFailed", e.Message); }
        RefreshStartupStatus();
    }

    [RelayCommand]
    private void DisableStartup()
    {
        try
        {
            var (code, output) = RunSchtasks(StartupTask.DeleteArguments());
            Message = code == 0 ? Loc.Get("Settings_Tray_Unregistered") : Loc.Format("Settings_Tray_RegisterFailed", output.Trim());
        }
        catch (Exception e) { Message = Loc.Format("Settings_Tray_RegisterFailed", e.Message); }
        RefreshStartupStatus();
    }
}
