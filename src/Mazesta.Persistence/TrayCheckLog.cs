using System.Text.Json; using System.Text.Json.Serialization;
namespace Mazesta.Persistence;

/// <summary>One drive as the tray's health check read it. Null where Windows reported nothing (never 0).</summary>
public sealed record TrayDrive(string Name, string? Status, int? WearPercent, double? TemperatureC, bool NeedsAttention);

/// <summary>One tray check: <see cref="Kind"/> is "temps" (a sensor poll) or "health" (the drives). <see cref="Problems"/> are the alerts it raised,
/// in words; <see cref="Error"/> is why the check itself failed. A value the check could not read is null.</summary>
public sealed record TrayCheck(DateTimeOffset Time, string Kind, double? CpuTempC, double? GpuTempC, IReadOnlyList<TrayDrive> Drives, IReadOnlyList<string> Problems, string? Error,
    double? GpuHotSpotC = null)   // absent in checks logged before the hot spot was read
{
    [JsonIgnore] public bool IsProblem => Problems.Count > 0 || Error is not null;
}

/// <summary>
/// The tray's recent checks, newest last, in <c>Data/tray/checks.json</c>: the tray writes it, its summary window and the app read it. The file is
/// replaced whole through a temporary file, so a reader never sees half of it; a damaged or missing file reads as no checks, and is rewritten by the
/// next check. Only the newest <see cref="Keep"/> are kept.
/// </summary>
public static class TrayCheckLog
{
    public const int Keep = 60;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

    public static string FileIn(AppPaths paths) => Path.Combine(paths.DataRoot, "tray", "checks.json");

    public static IReadOnlyList<TrayCheck> Read(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<List<TrayCheck>>(File.ReadAllText(file), Json) ?? [] : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return []; }
    }

    public static IReadOnlyList<TrayCheck> Append(string file, TrayCheck check)
    {
        var all = Read(file).Append(check).TakeLast(Keep).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(all, Json));
        File.Move(tmp, file, overwrite: true);
        return all;
    }
}
