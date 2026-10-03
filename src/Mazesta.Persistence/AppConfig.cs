using System.Text.Json.Nodes;
namespace Mazesta.Persistence;
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
public sealed record ChartWindowConfig(string SensorId, WindowPlacement? Placement, int WindowMinutes);
public sealed class AppConfig : IVersionedDocument
{
    public const int CurrentSchemaVersion = 3;
    public static IReadOnlyList<IMigration> Migrations { get; } = [new Migration0To1(), new Migration1To2(), new Migration2To3()];
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Language { get; set; } = "fa";
    /// <summary>"auto" lets WPF use the GPU; "software" draws the whole UI on the CPU - for a machine whose graphics driver cannot be trusted (a repair shop meets those), where the window otherwise comes up blank.</summary>
    public string RenderMode { get; set; } = "auto";
    public int FastIntervalSeconds { get; set; } = 2;
    public int StorageIntervalSeconds { get; set; } = 900;
    public string ShopName { get; set; } = "مازستا";
    /// <summary>The service job being worked on (spec 7.1): printed on every report while set. Saved with the settings so the job survives a
    /// restart or a reboot during a test; empty when there is none.</summary>
    public string ServiceNumber { get; set; } = "";
    public List<string> ExpandedGroups { get; set; } = [];
    public WindowPlacement? MainWindow { get; set; }
    public List<ChartWindowConfig> ChartWindows { get; set; } = [];
    /// <summary>Delay before the tray's first health check after sign-in, so it never runs during it.</summary>
    public int TrayFirstCheckSeconds { get; set; } = 20;
    /// <summary>How often the tray checks while nothing is wrong.</summary>
    public int TrayIdleIntervalMinutes { get; set; } = 10;
    /// <summary>How often the tray checks while a health rule is building towards an alert (HealthAlerts.IsWatching).</summary>
    public int TrayWatchIntervalSeconds { get; set; } = 30;
    /// <summary>How often the tray reads the drives' health (SMART / Windows' verdict, wear, uncorrected errors). Absent in older files, so 30.</summary>
    public int TrayHealthIntervalMinutes { get; set; } = 30;
    /// <summary>The processor's and the graphics card's temperature (°C) above which the tray and the app warn. Absent in older files, so 95.</summary>
    public int TrayCpuAlertC { get; set; } = 95;
    public int TrayGpuAlertC { get; set; } = 95;
    /// <summary>The on-screen overlay was on when the app last closed: it comes back on at start. Absent in older files, so off.</summary>
    public bool OverlayVisible { get; set; }
    /// <summary>TopLeft, TopRight, BottomLeft or BottomRight of the primary screen.</summary>
    public string OverlayCorner { get; set; } = "TopLeft";
    /// <summary>What the overlay shows, in order, each with its chart on or off (ids from OverlayCatalog). Null in older files: the game preset.</summary>
    public List<Mazesta.Core.Overlay.OverlayChoice>? OverlayItems { get; set; }
    /// <summary>The preset the items came from ("game", "render", "troubleshoot"), or "custom" once they were changed by hand.</summary>
    public string OverlayPreset { get; set; } = Mazesta.Core.Overlay.OverlayCatalog.DefaultPreset;
    /// <summary>The overlay panel's opacity, 0.5 to 1.</summary>
    public double OverlayOpacity { get; set; } = 0.9;
    /// <summary>The overlay's size: 0.85 small, 1 normal, 1.2 large.</summary>
    public double OverlayScale { get; set; } = 1.0;
    /// <summary>"list" (one column) or "columns" (two blocks side by side, the denser layout of the first overlay). Absent in older files: list.</summary>
    public string OverlayLayout { get; set; } = "list";
    /// <summary>The address (or name) the overlay's ping, packet loss and jitter are measured to. Absent in older files: Google's resolver.</summary>
    public string OverlayPingTarget { get; set; } = "8.8.8.8";
    /// <summary>The services the game mode stops (see <see cref="Mazesta.Core.Gaming.GameBoost"/>); null takes its defaults.</summary>
    public List<string>? GameModeServices { get; set; }
    /// <summary>What each service was before the game mode stopped it; not empty means the mode is on, and switching it off puts these back.</summary>
    public List<Mazesta.Core.Gaming.ServiceSnapshot>? GameModeSaved { get; set; }
}
public sealed class Migration0To1 : IMigration
{
    public int From => 0;
    public JsonObject Apply(JsonObject d)
    {
        var o = new JsonObject();
        if (d["pollSeconds"] is JsonNode p) o["fastIntervalSeconds"] = p.DeepClone();
        foreach (var key in new[] { "language", "fastIntervalSeconds", "storageIntervalSeconds", "shopName", "expandedGroups", "mainWindow", "chartWindows" })
            if (d[key] is JsonNode n) o[key] = n.DeepClone();
        return o;
    }
}
/// <summary>The product is Persian-first (spec §0): every config written before this version carries the
/// old English default, so it is reset to Persian once. The Settings page still switches back; this
/// runs only when an older file is loaded, never again for a v2 file.</summary>
public sealed class Migration1To2 : IMigration
{
    public int From => 1;
    public JsonObject Apply(JsonObject d) { var o = (JsonObject)d.DeepClone(); o["language"] = "fa"; return o; }
}
/// <summary>Adds the tray's check-interval settings; a document written before they existed just
/// keeps every other field and picks up the new ones' C# defaults on deserialize.</summary>
public sealed class Migration2To3 : IMigration
{
    public int From => 2;
    public JsonObject Apply(JsonObject d) => (JsonObject)d.DeepClone();
}
