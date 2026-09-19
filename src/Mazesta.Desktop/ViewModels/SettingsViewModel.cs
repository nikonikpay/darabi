using System.Reflection; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input; using Mazesta.Core.Text; using Mazesta.Desktop.Localization; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfig _config; private readonly JsonStore<AppConfig> _store; private readonly PollingEngine _engine; private readonly MonitoringOptions _options; private readonly ShellViewModel _shell; private readonly Action<string> _openFolder;
    public string[] Languages => ["en", "fa"];
    public string[] RenderModes => ["auto", "software"];
    [ObservableProperty] private string _language; [ObservableProperty] private string _renderMode; [ObservableProperty] private string _fastIntervalText; [ObservableProperty] private string _storageIntervalText; [ObservableProperty] private string _shopName; [ObservableProperty] private string _message = "";
    public string DataFolder { get; } public string ModeText { get; } public string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
    public SettingsViewModel(AppConfig config, JsonStore<AppConfig> store, AppPaths paths, PollingEngine engine, MonitoringOptions options, ShellViewModel shell, Action<string> openFolder)
    {
        _config = config; _store = store; _engine = engine; _options = options; _shell = shell; _openFolder = openFolder;
        _language = config.Language; _renderMode = config.RenderMode; _fastIntervalText = config.FastIntervalSeconds.ToString(); _storageIntervalText = config.StorageIntervalSeconds.ToString(); _shopName = config.ShopName;
        DataFolder = paths.DataRoot; ModeText = Loc.Get(paths.IsPortable ? "Settings_Mode_Portable" : "Settings_Mode_Installed");
    }
    internal bool TryValidate(out int fast, out int storage, out string error)
    {
        error = ""; storage = 0;
        if (!PersianDigits.TryParseInt(FastIntervalText, out fast) || !MonitoringOptions.AllowedFastSeconds.Contains(fast)) { error = Loc.Get("Settings_Invalid_Interval"); return false; }
        if (!PersianDigits.TryParseInt(StorageIntervalText, out storage) || storage < 60) { error = Loc.Get("Settings_Invalid_Interval"); return false; }
        return true;
    }
    [RelayCommand] private void Save()
    {
        if (!TryValidate(out int fast, out int storage, out string error)) { Message = error; return; }
        bool restartNeeded = _config.Language != Language || _config.RenderMode != RenderMode;
        _config.Language = Language; _config.RenderMode = RenderMode; _config.FastIntervalSeconds = fast; _config.StorageIntervalSeconds = storage; _config.ShopName = ShopName.Trim().Length == 0 ? _config.ShopName : ShopName.Trim();
        bool storageChanged = _options.StorageInterval != TimeSpan.FromSeconds(storage);
        _options.StorageInterval = TimeSpan.FromSeconds(storage); if (_engine.FastInterval != TimeSpan.FromSeconds(fast)) _engine.SetFastInterval(TimeSpan.FromSeconds(fast));
        // The storage cadence can be up to 15 minutes, so without re-arming, a shortened interval
        // would not take effect until the old one had elapsed.
        if (storageChanged) _engine.RearmStorageNodes();
        bool saved = _store.Save(_config); _shell.RefreshInterval();
        Message = (saved ? Loc.Get("Settings_Saved") : Loc.Get("Settings_SaveFailed")) + (restartNeeded ? " " + Loc.Get("Settings_RestartNote") : "");
    }
    [RelayCommand] private void OpenFolder() => _openFolder(DataFolder);
}
