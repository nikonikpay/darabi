using System.Text.Json;
namespace Mazesta.Diagnostics.Ai;

/// <summary>One measured run of a model on one device (gpu or cpu).</summary>
public sealed record AiMeasurement(DateTimeOffset At, double? PromptTokensPerSecond, double GenerationTokensPerSecond, string? Detail);

/// <summary>The latest measurement of each model on each device, kept in <c>Data/ai/results.json</c> so the page shows what this machine did
/// after a restart. The latest, not the best: the page answers "how does this model run here", and a slower run after a driver change is news.</summary>
public sealed class AiResults
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _file; private readonly object _lock = new();
    private readonly Dictionary<string, AiMeasurement> _all;

    public AiResults(string aiRoot)
    {
        _file = Path.Combine(aiRoot, "results.json");
        try { _all = File.Exists(_file) ? JsonSerializer.Deserialize<Dictionary<string, AiMeasurement>>(File.ReadAllText(_file), Json) ?? [] : []; }
        catch (JsonException) { _all = []; }   // a damaged file loses the old numbers, never the page
    }

    public static string Key(string model, string device) => $"{model}|{device}";
    public AiMeasurement? Get(string model, string device) { lock (_lock) return _all.GetValueOrDefault(Key(model, device)); }

    public void Set(string model, string device, AiMeasurement m)
    {
        lock (_lock)
        {
            _all[Key(model, device)] = m;
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_all, Json));
        }
    }
}
