using System.Text.Json;
namespace Mazesta.Persistence;

/// <summary>The tray's check schedule, read straight from appconfig.json. Read-only on purpose: the tray is a second process next to the
/// Desktop app, and going through <see cref="JsonStore{T}"/> could migrate and rewrite the file, or move a damaged one aside, under the
/// app's feet. A missing, unreadable or non-positive value falls back to the <see cref="AppConfig"/> default - a zero interval would throw in the timer.
/// The alert thresholds are read here too, as the tray reads nothing else of the file.</summary>
public sealed record TrayIntervals(int FirstCheckSeconds, int IdleMinutes, int WatchSeconds, int HealthMinutes, int CpuAlertC = 98, int GpuAlertC = 90, int GpuHotSpotAlertC = 98, int HoldMinutes = 10, int RamAlertPercent = 90, int RamHoldMinutes = 5, int RamCheckMinutes = 1, int SsdAlertC = 70)
{
    public static TrayIntervals Read(string configFile)
    {
        var d = new AppConfig(); var defaults = new TrayIntervals(d.TrayFirstCheckSeconds, d.TrayIdleIntervalMinutes, d.TrayWatchIntervalSeconds, d.TrayHealthIntervalMinutes, d.TrayCpuAlertC, d.TrayGpuAlertC, d.TrayGpuHotSpotAlertC, d.TrayHoldMinutes, d.TrayRamAlertPercent, d.TrayRamHoldMinutes, d.TrayRamCheckMinutes, d.TraySsdAlertC);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configFile)); var root = doc.RootElement;
            // A file the app has not yet migrated (schema 3) still holds the old defaults; the tray treats those as unset, so a first start before the app has run already follows the new schedule.
            bool unmigrated = root.TryGetProperty("schemaVersion", out var sv) && sv.TryGetInt32(out int version) && version < 4;
            var oldDefaults = new Dictionary<string, int> { ["trayIdleIntervalMinutes"] = 10, ["trayWatchIntervalSeconds"] = 30, ["trayHealthIntervalMinutes"] = 30, ["trayCpuAlertC"] = 100, ["trayGpuAlertC"] = 95 };
            bool before5 = root.TryGetProperty("schemaVersion", out var sv5) && sv5.TryGetInt32(out int v5) && v5 < 5;   // the hourly check of schema 4 is now every 10 minutes
            int Get(string name, int fallback) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) && n > 0 && !(unmigrated && oldDefaults.TryGetValue(name, out int old) && old == n)
                && !(before5 && name == "trayIdleIntervalMinutes" && n == 60) ? n : fallback;
            return new(Get("trayFirstCheckSeconds", defaults.FirstCheckSeconds), Get("trayIdleIntervalMinutes", defaults.IdleMinutes), Get("trayWatchIntervalSeconds", defaults.WatchSeconds),
                Get("trayHealthIntervalMinutes", defaults.HealthMinutes), Alert("trayCpuAlertC", defaults.CpuAlertC), Alert("trayGpuAlertC", defaults.GpuAlertC), Alert("trayGpuHotSpotAlertC", defaults.GpuHotSpotAlertC),
                Get("trayHoldMinutes", defaults.HoldMinutes), Get("trayRamAlertPercent", defaults.RamAlertPercent) is var rp && rp is >= 50 and <= 99 ? rp : defaults.RamAlertPercent,
                Get("trayRamHoldMinutes", defaults.RamHoldMinutes), Get("trayRamCheckMinutes", defaults.RamCheckMinutes), Get("traySsdAlertC", defaults.SsdAlertC) is var sa && sa is >= 40 and <= 105 ? sa : defaults.SsdAlertC);
            int Alert(string name, int fallback) => Get(name, fallback) is var n && n is >= 60 and <= 105 ? n : fallback;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return defaults; }
    }

    /// <summary>The switch read afresh at each check, so turning it on or off in the settings needs no restart of the tray.</summary>
    public static bool SkipInGames(string configFile)
    {
        try { using var doc = JsonDocument.Parse(File.ReadAllText(configFile)); return doc.RootElement.TryGetProperty("traySkipInGames", out var v) && v.ValueKind == JsonValueKind.True; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }
}

/// <summary>The overlay's shortcuts, read straight from appconfig.json for the tray (read-only, like <see cref="TrayIntervals"/>); a missing, damaged or clashing setting gives the defaults.</summary>
public static class OverlayKeysFile
{
    public static Mazesta.Core.Tray.OverlayHotkeys Read(string configFile)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configFile)); var root = doc.RootElement; var d = new AppConfig();
            string Get(string name, string fallback) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
            return new Mazesta.Core.Tray.OverlayHotkeys(Get("overlayKeyToggle", d.OverlayKeyToggle), Get("overlayKeyMove", d.OverlayKeyMove), Get("overlayKeyLayout", d.OverlayKeyLayout), Get("overlayKeyPreset", d.OverlayKeyPreset)).Valid();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return new(); }
    }
}
