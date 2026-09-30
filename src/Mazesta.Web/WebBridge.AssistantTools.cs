using System.IO; using System.Text.Json; using System.Text.Json.Nodes;
using Mazesta.Core.Hardware; using Mazesta.Desktop.Composition; using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics.Ai; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Monitoring; using Mazesta.Reporting;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    /// <summary>
    /// What the assistant may call. All four only read: no test is started, nothing is changed. Each returns compact JSON of what the app really
    /// holds; a value that is not available is left out, never written as 0. The page shows which tools ran beside the answer.
    /// </summary>
    private IReadOnlyList<AiTool> AssistantTools(Func<SensorSnapshot?> latest)
    {
        var engine = _sp.GetRequiredService<PollingEngine>(); var inventory = _sp.GetRequiredService<InventoryCache>();
        static string Json(object o) => JsonSerializer.Serialize(o, Json_);
        static string? Text(JsonElement a, string name) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static int Int(JsonElement a, string name, int fallback, int max) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? Math.Clamp(n, 1, max) : fallback;
        const string NoArgs = """{"type":"object","properties":{}}""";

        return
        [
            new("get_machine_summary", "The computer's specification: processor, graphics cards, memory, operating system, motherboard, BIOS and the drives with their health.", NoArgs,
                async (_, _) =>
                {
                    var inv = await inventory.GetAsync().ConfigureAwait(false);
                    return Json(new
                    {
                        cpu = inv.Cpu?.Name?.Trim(), gpus = inv.Gpus.Select(g => g.Name?.Trim()), ramGb = inv.TotalPhysicalMemoryBytes is { } b ? Math.Round(b / 1073741824.0, 1) : (double?)null,
                        os = inv.Os?.Caption, board = inv.Motherboard is { } m ? $"{m.Manufacturer} {m.Product}".Trim() : null, bios = inv.Bios?.Version,
                        drives = inv.Storage.Select(d => new { name = d.FriendlyName, health = SystemInfoViewModel.DriveHealth(d.HealthStatus, d.WearPercent) }),
                    });
                }),
            new("get_sensors", "The live sensor readings now (temperatures, loads, clocks, power, fans), grouped by device. Optional kind: Temperature, Load, Clock, Power or Fan.",
                """{"type":"object","properties":{"kind":{"type":"string","enum":["Temperature","Load","Clock","Power","Fan"]}}}""",
                (a, _) =>
                {
                    var snap = latest(); if (snap is null) return Task.FromResult(Json(new { error = "no sensor reading has arrived yet" }));
                    string? kind = Text(a, "kind"); var values = snap.Readings.Where(r => r.Quality == DataQuality.Ok && r.Value is not null).ToDictionary(r => r.Id, r => r.Value!.Value);
                    var devices = engine.Hardware.Select(n => new
                    {
                        device = n.Name, part = n.Kind.ToString(),
                        sensors = n.Sensors.Where(s => values.ContainsKey(s.Id) && (kind is null || s.Kind.ToString() == kind) && s.Kind.ToString() is "Temperature" or "Load" or "Clock" or "Power" or "Fan")
                            .Take(12).Select(s => new { name = s.Name, kind = s.Kind.ToString(), value = Math.Round(values[s.Id], 1), unit = s.Unit.ToString() }),
                    }).Where(d => d.sensors.Any()).Take(8);
                    return Task.FromResult(Json(new { secondsAgo = Math.Round((DateTimeOffset.UtcNow - snap.Timestamp).TotalSeconds), devices }));
                }),
            new("list_reports", "The newest saved test reports: when, kind, verdict and how many tests passed, failed or did not run. Optional limit (default 5, at most 10).",
                """{"type":"object","properties":{"limit":{"type":"integer"}}}""",
                (a, _) => Task.FromResult(Json(new
                {
                    reports = new ReportStore(_paths.ReportsDir).List().OrderByDescending(r => r.CreatedAt).Take(Int(a, "limit", 5, 10))
                        .Select(r => new { createdAt = r.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), kind = r.Kind.ToString(), verdict = r.Verdict?.ToString(), r.Counts, benchmarks = r.Benchmarks }),
                }))),
            new("get_benchmark_history", "This computer's newest benchmark results, newest first, to see whether it got slower or faster. Optional benchmark (part of its id) and limit (default 6, at most 15).",
                """{"type":"object","properties":{"benchmark":{"type":"string"},"limit":{"type":"integer"}}}""",
                (a, _) =>
                {
                    if (_benchRunLog is null || _benchSystemHash?.Invoke() is not { } hash) return Task.FromResult(Json(new { error = "the benchmark history is not available yet" }));
                    string? name = Text(a, "benchmark");
                    var runs = _benchRunLog.Recent(hash, 300).Where(r => name is null || r.Benchmark.Contains(name, StringComparison.OrdinalIgnoreCase)).Take(Int(a, "limit", 6, 15))
                        .Select(r => new { at = r.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), benchmark = r.Benchmark, settings = r.Settings, value = Math.Round(r.Value, 2), unit = r.Unit, overclocked = r.Overclocked });
                    return Task.FromResult(Json(new { runs }));
                }),
        ];
    }

    private static readonly JsonSerializerOptions Json_ = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
}
