using System.Text.Json;
namespace Mazesta.Persistence;

/// <summary>The tray's check schedule, read straight from appconfig.json. Read-only on purpose: the tray is a second process next to the
/// Desktop app, and going through <see cref="JsonStore{T}"/> could migrate and rewrite the file, or move a damaged one aside, under the
/// app's feet. A missing, unreadable or non-positive value falls back to the <see cref="AppConfig"/> default - a zero interval would throw in the timer.</summary>
public sealed record TrayIntervals(int FirstCheckSeconds, int IdleMinutes, int WatchSeconds, int HealthMinutes)
{
    public static TrayIntervals Read(string configFile)
    {
        var d = new AppConfig(); var defaults = new TrayIntervals(d.TrayFirstCheckSeconds, d.TrayIdleIntervalMinutes, d.TrayWatchIntervalSeconds, d.TrayHealthIntervalMinutes);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configFile)); var root = doc.RootElement;
            int Get(string name, int fallback) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) && n > 0 ? n : fallback;
            return new(Get("trayFirstCheckSeconds", defaults.FirstCheckSeconds), Get("trayIdleIntervalMinutes", defaults.IdleMinutes), Get("trayWatchIntervalSeconds", defaults.WatchSeconds),
                Get("trayHealthIntervalMinutes", defaults.HealthMinutes));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return defaults; }
    }
}
