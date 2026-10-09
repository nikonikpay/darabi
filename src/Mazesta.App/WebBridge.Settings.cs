using Mazesta.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>The settings page's state; the assistant writes the tray's warning temperatures through it, so the page shows what was set.</summary>
    private SettingsViewModel? _settingsVm;

    private void RegisterSettings()
    {
        // Validation and saving are the WPF edition's: the same rules, the same config file format.
        var settings = _settingsVm = _sp.GetRequiredService<Func<SettingsViewModel>>()();
        object State() => new
        {
            language = settings.Language, languages = settings.Languages, renderMode = settings.RenderMode, renderModes = settings.RenderModes,
            interval = settings.FastIntervalText, storageInterval = settings.StorageIntervalText, displayName = settings.DisplayName, message = settings.Message,
            trayFirst = settings.TrayFirstCheckText, trayIdle = settings.TrayIdleText, trayWatch = settings.TrayWatchText, trayHealth = settings.TrayHealthText, trayCpuAlert = settings.TrayCpuAlertText, trayGpuAlert = settings.TrayGpuAlertText, trayStatus = settings.TrayStatusText,
            usageReport = _config.UsageReport, usageLog = _sp.GetRequiredService<Mazesta.Desktop.Services.UsageRecorder>().Log.File,
            canEnableTray = settings.CanEnableTray, canDisableTray = settings.CanDisableTray, dataFolder = settings.DataFolder, mode = settings.ModeText, version = settings.Version,
        };
        Mirror("settings", settings, State);
        Method("settings.state", _ => State());
        Method("settings.set", p =>
        {
            string v = Str(p, "value");
            switch (Str(p, "field"))
            {
                case "language": if (settings.Languages.Contains(v)) settings.Language = v; break;
                case "renderMode": if (settings.RenderModes.Contains(v)) settings.RenderMode = v; break;
                case "interval": settings.FastIntervalText = v; break;
                case "storageInterval": settings.StorageIntervalText = v; break;
                case "displayName": settings.DisplayName = v; break;
                case "trayFirst": settings.TrayFirstCheckText = v; break;
                case "trayIdle": settings.TrayIdleText = v; break;
                case "trayWatch": settings.TrayWatchText = v; break;
                case "trayHealth": settings.TrayHealthText = v; break;
                case "trayCpuAlert": settings.TrayCpuAlertText = v; break;
                case "trayGpuAlert": settings.TrayGpuAlertText = v; break;
                case "usageReport": _config.UsageReport = Bool(p, "value"); _store.Save(_config); PushSoon("settings", State); break;
                default: throw new ArgumentException("unknown field");
            }
            return null;
        });
        MethodAsync("settings.exec", async p =>
        {
            switch (Str(p, "cmd"))
            {
                case "save": settings.SaveCommand.Execute(null); Push("interval", _config.FastIntervalSeconds); break;
                case "openFolder": settings.OpenFolderCommand.Execute(null); break;
                case "openUsageLog": { string f = _sp.GetRequiredService<Mazesta.Desktop.Services.UsageRecorder>().Log.File; if (File.Exists(f)) Open(f); else throw new InvalidOperationException(Mazesta.Desktop.Localization.Loc.Get("Settings_Usage_NoLog")); break; }
                case "enableTray": await settings.EnableTrayCommand.ExecuteAsync(null); break;
                case "disableTray": await settings.DisableTrayCommand.ExecuteAsync(null); break;
                default: throw new ArgumentException("unknown command");
            }
            return null;
        });
    }
}
