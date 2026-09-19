using System.Text.Json.Nodes;
namespace Mazesta.Persistence;
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
public sealed record ChartWindowConfig(string SensorId, WindowPlacement? Placement, int WindowMinutes);
public sealed class AppConfig : IVersionedDocument
{
    public const int CurrentSchemaVersion = 2;
    public static IReadOnlyList<IMigration> Migrations { get; } = [new Migration0To1(), new Migration1To2()];
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Language { get; set; } = "fa";
    /// <summary>"auto" lets WPF use the GPU; "software" draws the whole UI on the CPU - for a machine whose graphics driver cannot be trusted (a repair shop meets those), where the window otherwise comes up blank.</summary>
    public string RenderMode { get; set; } = "auto";
    public int FastIntervalSeconds { get; set; } = 2;
    public int StorageIntervalSeconds { get; set; } = 900;
    public string ShopName { get; set; } = "مازستا";
    public List<string> ExpandedGroups { get; set; } = [];
    public WindowPlacement? MainWindow { get; set; }
    public List<ChartWindowConfig> ChartWindows { get; set; } = [];
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
