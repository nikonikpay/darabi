using System.Text.Json;
namespace Mazesta.Persistence;

/// <summary>The saved profile a card is put in when the tray starts (at sign-in): GPU settings are dropped on every reboot, so the tray puts them back.</summary>
public sealed record GpuStartupChoice(string GpuId, string ProfileName);

/// <summary>
/// Which GPU profile each card starts in, in <c>Data/config/gpu-startup.json</c>. The tray menu and the tuning page both set it (picking a profile
/// there applies it now and keeps it for the next sign-in; stock clears it). It is a file of its own, not part of gpu-profiles.json, because the
/// app keeps that document open and saves it whole: the tray writing into it would be overwritten. Replaced whole through a temporary file; a
/// damaged or missing file reads as "stock for every card", which is what the card does after a reboot anyway.
/// </summary>
public static class GpuStartup
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string FileIn(AppPaths paths) => Path.Combine(paths.ConfigDir, "gpu-startup.json");
    public static string ProfilesFileIn(AppPaths paths) => Path.Combine(paths.ConfigDir, "gpu-profiles.json");

    public static IReadOnlyList<GpuStartupChoice> Read(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<List<GpuStartupChoice>>(File.ReadAllText(file), Json) ?? [] : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return []; }
    }

    public static string? For(string file, string gpuId) => Read(file).FirstOrDefault(c => c.GpuId == gpuId)?.ProfileName;

    /// <summary>The card's start-up profile, or none (<paramref name="profileName"/> null: it starts at stock).</summary>
    public static void Set(string file, string gpuId, string? profileName)
    {
        var all = Read(file).Where(c => c.GpuId != gpuId).ToList();
        if (profileName is not null) all.Add(new(gpuId, profileName));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(all, Json));
        File.Move(tmp, file, overwrite: true);
    }

    /// <summary>The app's saved profiles, read only (the tray never writes them); null when the file is missing or unreadable.</summary>
    public static GpuProfileDocument? ReadProfiles(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<GpuProfileDocument>(File.ReadAllText(file), JsonStore<GpuProfileDocument>.Options) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return null; }
    }
}
