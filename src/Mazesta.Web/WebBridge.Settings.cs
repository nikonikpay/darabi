using Mazesta.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    private void RegisterSettings()
    {
        // Validation and saving are the WPF edition's: the same rules, the same config file format.
        var settings = _sp.GetRequiredService<Func<SettingsViewModel>>()();
        object State() => new
        {
            language = settings.Language, languages = settings.Languages, renderMode = settings.RenderMode, renderModes = settings.RenderModes,
            interval = settings.FastIntervalText, storageInterval = settings.StorageIntervalText, shopName = settings.ShopName, message = settings.Message,
            trayFirst = settings.TrayFirstCheckText, trayIdle = settings.TrayIdleText, trayWatch = settings.TrayWatchText, trayStatus = settings.TrayStatusText,
            canEnableTray = settings.CanEnableTray, canDisableTray = settings.CanDisableTray, dataFolder = settings.DataFolder, mode = settings.ModeText, version = settings.Version,
            overlayVisible = settings.OverlayVisible, overlayCorner = settings.OverlayCorner?.Value, overlayCorners = settings.OverlayCorners.Select(c => new { value = c.Value, label = c.Label }),
            hotkey = settings.OverlayHotkey,
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
                case "shopName": settings.ShopName = v; break;
                case "trayFirst": settings.TrayFirstCheckText = v; break;
                case "trayIdle": settings.TrayIdleText = v; break;
                case "trayWatch": settings.TrayWatchText = v; break;
                case "overlayVisible": settings.OverlayVisible = Bool(p, "value"); break;
                case "overlayCorner": settings.OverlayCorner = settings.OverlayCorners.FirstOrDefault(c => c.Value == v); break;
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
                case "enableTray": await settings.EnableTrayCommand.ExecuteAsync(null); break;
                case "disableTray": await settings.DisableTrayCommand.ExecuteAsync(null); break;
                default: throw new ArgumentException("unknown command");
            }
            return null;
        });
    }
}
