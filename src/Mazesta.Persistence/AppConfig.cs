using System.Text.Json.Nodes;
namespace Mazesta.Persistence;
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
public sealed record ChartWindowConfig(string SensorId, WindowPlacement? Placement, int WindowMinutes);
public sealed class AppConfig : IVersionedDocument
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Language { get; set; } = "en";
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
