using System.Text.Encodings.Web; using System.Text.Json; using System.Text.Json.Serialization;
namespace Mazesta.Reporting;

/// <summary>The machine-readable report: the whole <see cref="SessionReport"/>, enums as names, Persian left readable (not \u-escaped).</summary>
public static class ReportJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    public static string Write(SessionReport report) => JsonSerializer.Serialize(report, Options);
    public static SessionReport? Read(string json) => JsonSerializer.Deserialize<SessionReport>(json, Options);
}
