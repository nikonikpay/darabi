using System.Text.Json; using Mazesta.Core.Fans;
namespace Mazesta.Persistence;

/// <summary>
/// The graphics card's fan curve, in <c>Data/config/gpu-fan.json</c>: what is in force now (automatic, or a curve of temperature against duty read from the core or the
/// hot spot), the name of the profile it came from, and the ones the user saved. The card's fans move together, so one setting covers them all (see <c>GpuFanController</c>).
/// A <see cref="FanSetting"/> is reused: its Mode is "auto" or "curve", its Source "core" or "hot".
/// </summary>
public sealed class GpuFanProfiles
{
    /// <summary>The ready-made profiles, by name: "auto" gives the fans back to the card; the others are curves (see <c>FanCurve.Preset</c>).</summary>
    public static readonly string[] Builtin = ["auto", "silent", "standard", "performance", "full"];
    public static string FileIn(AppPaths paths) => Path.Combine(paths.ConfigDir, "gpu-fan.json");

    public string Active { get; set; } = "auto";
    public FanSetting Current { get; set; } = new();
    public Dictionary<string, FanSetting> Custom { get; set; } = [];

    private static readonly JsonSerializerOptions Opt = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static GpuFanProfiles Read(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<GpuFanProfiles>(File.ReadAllText(file), Opt) is { } p ? p.Fixed() : new() : new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return new(); }
    }
    public void Write(string file) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); string tmp = file + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opt)); File.Move(tmp, file, overwrite: true); }
    private GpuFanProfiles Fixed()
    {
        Custom ??= []; Current ??= new(); if (string.IsNullOrWhiteSpace(Active)) Active = "auto";
        if (Current.Mode == "curve" && FanCurve.Clean(Current.Points ?? []) is null) Current = new();   // a curve with too few points is no curve: back to automatic
        return this;
    }
}
