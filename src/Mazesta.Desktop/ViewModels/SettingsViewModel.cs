using System.Reflection; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input; using Mazesta.Core.Text; using Mazesta.Desktop.Localization; using Mazesta.Desktop.Services; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfig _config; private readonly JsonStore<AppConfig> _store; private readonly PollingEngine _engine; private readonly MonitoringOptions _options; private readonly ShellViewModel _shell; private readonly Action<string> _openFolder; private readonly ITrayController _tray;
    public string[] Languages => ["en", "fa"];
    public string[] RenderModes => ["auto", "software"];
    [ObservableProperty] private string _language; [ObservableProperty] private string _renderMode; [ObservableProperty] private string _fastIntervalText; [ObservableProperty] private string _storageIntervalText; [ObservableProperty] private string _shopName; [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _trayFirstCheckText; [ObservableProperty] private string _trayIdleText; [ObservableProperty] private string _trayWatchText;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEnableTray), nameof(CanDisableTray))] private TrayState _trayState = new(false, false);
    [ObservableProperty] private string _trayStatusText = "";
    public bool CanEnableTray => !(TrayState.Registered && TrayState.Running);
    public bool CanDisableTray => TrayState.Registered || TrayState.Running;
    public string DataFolder { get; } public string ModeText { get; } public string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
    public SettingsViewModel(AppConfig config, JsonStore<AppConfig> store, AppPaths paths, PollingEngine engine, MonitoringOptions options, ShellViewModel shell, Action<string> openFolder, ITrayController tray)
    {
        _config = config; _store = store; _engine = engine; _options = options; _shell = shell; _openFolder = openFolder; _tray = tray;
        _language = config.Language; _renderMode = config.RenderMode; _fastIntervalText = config.FastIntervalSeconds.ToString(); _storageIntervalText = config.StorageIntervalSeconds.ToString(); _shopName = config.ShopName;
        _trayFirstCheckText = config.TrayFirstCheckSeconds.ToString(); _trayIdleText = config.TrayIdleIntervalMinutes.ToString(); _trayWatchText = config.TrayWatchIntervalSeconds.ToString();
        DataFolder = paths.DataRoot; ModeText = Loc.Get(paths.IsPortable ? "Settings_Mode_Portable" : "Settings_Mode_Installed");
        RefreshTray();
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
        bool trayChanged = (_config.TrayFirstCheckSeconds, _config.TrayIdleIntervalMinutes, _config.TrayWatchIntervalSeconds) != (trayFirst, trayIdle, trayWatch);
        _config.Language = Language; _config.RenderMode = RenderMode; _config.FastIntervalSeconds = fast; _config.StorageIntervalSeconds = storage; _config.ShopName = ShopName.Trim().Length == 0 ? _config.ShopName : ShopName.Trim();
        _config.TrayFirstCheckSeconds = trayFirst; _config.TrayIdleIntervalMinutes = trayIdle; _config.TrayWatchIntervalSeconds = trayWatch;
        bool storageChanged = _options.StorageInterval != TimeSpan.FromSeconds(storage);
        _options.StorageInterval = TimeSpan.FromSeconds(storage); if (_engine.FastInterval != TimeSpan.FromSeconds(fast)) _engine.SetFastInterval(TimeSpan.FromSeconds(fast));
        // The storage cadence can be up to 15 minutes, so without re-arming, a shortened interval
        // would not take effect until the old one had elapsed.
        if (storageChanged) _engine.RearmStorageNodes();
        bool saved = _store.Save(_config); _shell.RefreshInterval();
        Message = (saved ? Loc.Get("Settings_Saved") : Loc.Get("Settings_SaveFailed")) + (restartNeeded ? " " + Loc.Get("Settings_RestartNote") : "");
        // The tray reads its intervals once at start, so a running one is restarted to pick up the change.
        if (saved && trayChanged && TrayState.Running) { Message += " " + (_tray.Restart() is { } failure ? Loc.Format("Settings_Tray_ChangeFailed", failure) : Loc.Get("Settings_Tray_Restarted")); RefreshTray(); }
    }
    [RelayCommand] private void OpenFolder() => _openFolder(DataFolder);

    [RelayCommand] private void EnableTray() => ChangeTray(_tray.Enable(), "Settings_Tray_Enabled");
    [RelayCommand] private void DisableTray() => ChangeTray(_tray.Disable(), "Settings_Tray_Disabled");

    private void ChangeTray(string? failure, string successKey) { Message = failure is null ? Loc.Get(successKey) : Loc.Format("Settings_Tray_ChangeFailed", failure); RefreshTray(); }

    private void RefreshTray()
    {
        TrayState = _tray.Query();
        TrayStatusText = TrayState.Error is { } e ? Loc.Format("Settings_Tray_CheckFailed", e)
            : Loc.Format("Settings_Tray_State", Loc.Get(TrayState.Running ? "Settings_Tray_Running" : "Settings_Tray_Stopped"), Loc.Get(TrayState.Registered ? "Value_Yes" : "Value_No"));
    }
}
