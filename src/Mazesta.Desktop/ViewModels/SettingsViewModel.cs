using System.Reflection; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input; using Mazesta.Core.Text; using Mazesta.Desktop.Localization; using Mazesta.Desktop.Services; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfig _config; private readonly JsonStore<AppConfig> _store; private readonly PollingEngine _engine; private readonly MonitoringOptions _options; private readonly Action<string> _openFolder; private readonly ITrayController _tray;
    public string[] Languages => ["en", "fa"];
    public string[] RenderModes => ["auto", "software"];
    [ObservableProperty] private string _language; [ObservableProperty] private string _renderMode; [ObservableProperty] private string _fastIntervalText; [ObservableProperty] private string _storageIntervalText; [ObservableProperty] private string _shopName; [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _trayFirstCheckText; [ObservableProperty] private string _trayIdleText; [ObservableProperty] private string _trayWatchText; [ObservableProperty] private string _trayHealthText;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEnableTray), nameof(CanDisableTray))] private TrayState _trayState = new(false, false);
    [ObservableProperty] private string _trayStatusText = Loc.Get("Settings_Tray_Checking");
    /// <summary>Completes when the first tray query has finished (it runs schtasks, so it is off the UI thread).</summary>
    public Task TrayLoaded { get; }
    public bool CanEnableTray => !(TrayState.Registered && TrayState.Running);
    public bool CanDisableTray => TrayState.Registered || TrayState.Running;
    public string DataFolder { get; } public string ModeText { get; } public string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
    /// <summary>A corner of the screen for the overlay, with its label in the app's language.</summary>
    public sealed record OverlayCornerChoice(string Value, string Label);
    private readonly Services.OverlayService? _overlay;
    public IReadOnlyList<OverlayCornerChoice> OverlayCorners { get; } = [.. Services.OverlayService.Corners.Select(c => new OverlayCornerChoice(c, Loc.Get("Overlay_Corner_" + c)))];
    public bool HasOverlay => _overlay is not null;
    public string OverlayHotkey => Services.OverlayService.HotkeyText;
    /// <summary>Applied at once, like the overlay's own shortcut; saved with the other settings when the app closes.</summary>
    public OverlayCornerChoice? OverlayCorner
    {
        get => OverlayCorners.FirstOrDefault(c => c.Value == _config.OverlayCorner) ?? OverlayCorners[0];
        set { if (value is not null) { _overlay?.SetCorner(value.Value); OnPropertyChanged(); } }
    }
    public bool OverlayVisible { get => _overlay?.IsVisible == true; set { _overlay?.SetVisible(value); OnPropertyChanged(); } }

    public SettingsViewModel(AppConfig config, JsonStore<AppConfig> store, AppPaths paths, PollingEngine engine, MonitoringOptions options, Action<string> openFolder, ITrayController tray, Services.OverlayService? overlay = null)
    {
        _config = config; _store = store; _engine = engine; _options = options; _openFolder = openFolder; _tray = tray; _overlay = overlay;
        _language = config.Language; _renderMode = config.RenderMode; _fastIntervalText = config.FastIntervalSeconds.ToString(); _storageIntervalText = config.StorageIntervalSeconds.ToString(); _shopName = config.ShopName;
        _trayFirstCheckText = config.TrayFirstCheckSeconds.ToString(); _trayIdleText = config.TrayIdleIntervalMinutes.ToString(); _trayWatchText = config.TrayWatchIntervalSeconds.ToString(); _trayHealthText = config.TrayHealthIntervalMinutes.ToString();
        DataFolder = paths.DataRoot; ModeText = Loc.Get("Settings_Mode_Portable");
        TrayLoaded = RefreshTrayAsync();
    }
    internal bool TryValidate(out int fast, out int storage, out string error)
    {
        error = ""; storage = 0;
        if (!PersianDigits.TryParseInt(FastIntervalText, out fast) || !MonitoringOptions.AllowedFastSeconds.Contains(fast)) { error = Loc.Get("Settings_Invalid_Interval"); return false; }
        if (!PersianDigits.TryParseInt(StorageIntervalText, out storage) || storage < 60) { error = Loc.Get("Settings_Invalid_Interval"); return false; }
        return true;
    }
    private static bool TryRange(string text, int min, int max, out int value) => PersianDigits.TryParseInt(text, out value) && value >= min && value <= max;
    internal bool TryValidateTray(out int firstCheck, out int idle, out int watch, out string error) => TryValidateTray(out firstCheck, out idle, out watch, out _, out error);
    internal bool TryValidateTray(out int firstCheck, out int idle, out int watch, out int health, out string error)
    {
        idle = 0; watch = 0; health = 0;
        bool ok = TryRange(TrayFirstCheckText, 5, 300, out firstCheck) && TryRange(TrayIdleText, 1, 120, out idle) && TryRange(TrayWatchText, 5, 300, out watch)
            && TryRange(TrayHealthText, 5, 720, out health);
        error = ok ? "" : Loc.Get("Settings_Invalid_Interval"); return ok;
    }
    [RelayCommand] private void Save()
    {
        if (!TryValidate(out int fast, out int storage, out string error)) { Message = error; return; }
        if (!TryValidateTray(out int trayFirst, out int trayIdle, out int trayWatch, out int trayHealth, out error)) { Message = error; return; }
        bool restartNeeded = _config.Language != Language || _config.RenderMode != RenderMode;
        bool trayChanged = (_config.TrayFirstCheckSeconds, _config.TrayIdleIntervalMinutes, _config.TrayWatchIntervalSeconds, _config.TrayHealthIntervalMinutes) != (trayFirst, trayIdle, trayWatch, trayHealth);
        _config.Language = Language; _config.RenderMode = RenderMode; _config.FastIntervalSeconds = fast; _config.StorageIntervalSeconds = storage; _config.ShopName = ShopName.Trim().Length == 0 ? _config.ShopName : ShopName.Trim();
        _config.TrayFirstCheckSeconds = trayFirst; _config.TrayIdleIntervalMinutes = trayIdle; _config.TrayWatchIntervalSeconds = trayWatch; _config.TrayHealthIntervalMinutes = trayHealth;
        bool storageChanged = _options.StorageInterval != TimeSpan.FromSeconds(storage);
        _options.StorageInterval = TimeSpan.FromSeconds(storage); if (_engine.FastInterval != TimeSpan.FromSeconds(fast)) _engine.SetFastInterval(TimeSpan.FromSeconds(fast));
        // The storage cadence can be up to 15 minutes, so without re-arming, a shortened interval
        // would not take effect until the old one had elapsed.
        if (storageChanged) _engine.RearmStorageNodes();
        bool saved = _store.Save(_config);
        Message = (saved ? Loc.Get("Settings_Saved") : Loc.Get("Settings_SaveFailed")) + (restartNeeded ? " " + Loc.Get("Settings_RestartNote") : "");
        // The tray reads its intervals once at start, so a running one is restarted to pick up the change.
        if (saved && trayChanged && TrayState.Running) { Message += " " + (_tray.Restart() is { } failure ? Loc.Format("Settings_Tray_ChangeFailed", failure) : Loc.Get("Settings_Tray_Restarted")); }
    }
    [RelayCommand] private void OpenFolder() => _openFolder(DataFolder);

    [RelayCommand] private Task EnableTray() => ChangeTrayAsync(_tray.Enable, "Settings_Tray_Enabled");
    [RelayCommand] private Task DisableTray() => ChangeTrayAsync(_tray.Disable, "Settings_Tray_Disabled");

    // schtasks and killing a process take from tens of milliseconds to seconds, so none of it runs on the UI thread.
    private async Task ChangeTrayAsync(Func<string?> change, string successKey)
    {
        string? failure = await Task.Run(change);
        Message = failure is null ? Loc.Get(successKey) : Loc.Format("Settings_Tray_ChangeFailed", failure);
        await RefreshTrayAsync();
    }

    private async Task RefreshTrayAsync()
    {
        TrayState = await Task.Run(_tray.Query);
        TrayStatusText = TrayState.Error is { } e ? Loc.Format("Settings_Tray_CheckFailed", e)
            : Loc.Format("Settings_Tray_State", Loc.Get(TrayState.Running ? "Settings_Tray_Running" : "Settings_Tray_Stopped"), Loc.Get(TrayState.Registered ? "Value_Yes" : "Value_No"));
    }
}
