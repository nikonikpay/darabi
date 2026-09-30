using System.Reflection; using System.Text.Json; using System.Text.RegularExpressions; using Mazesta.Core.Hardware;
namespace Mazesta.Core.Health.Checkup;

/// <summary>
/// A processor model's figures as its maker publishes them (Intel ARK, amd.com): <see cref="BaseMhz"/> the base clock (Intel: the P-cores'),
/// <see cref="BoostMhz"/> the single-core boost (Intel: the P-cores' Turbo Boost 2.0 figure, not the Turbo Boost Max 3.0 or Thermal Velocity
/// Boost figures, which only the best cores reach when cool; AMD: "Max. Boost Clock"), <see cref="BasePowerW"/> Intel's Processor Base Power
/// (or TDP) or AMD's default TDP, <see cref="TurboPowerW"/> Intel's Maximum Turbo Power, <see cref="TjMaxC"/> the highest temperature the maker
/// allows. <see cref="Source"/> is the page each figure was read from. A figure the page does not give is null.
/// </summary>
public sealed record CpuSpec(string Model, HardwareVendor Vendor, string? Segment, int? Cores, int? Threads, int? BaseMhz, int? BoostMhz, int? BasePowerW, int? TurboPowerW, int? TjMaxC,
    string Source);

/// <summary>
/// The processor figures the checkup compares a run with, read from the makers' own pages (see docs/CHECKUP.md for when and how). A processor is
/// found by its model number, however Windows words its name ("13th Gen Intel(R) Core(TM) i7-13700KF", "AMD Ryzen 9 7950X 16-Core Processor");
/// a model that is not listed is simply not found, and the checkup falls back to what the chip reports itself.
/// </summary>
public static partial class CpuSpecs
{
    [GeneratedRegex(@"\b(i[3579])-(\d{3,5}[A-Z]{0,3})\b", RegexOptions.IgnoreCase)] private static partial Regex IntelCore();
    [GeneratedRegex(@"\bUltra\s+(X?[3579])\s+(\d{3}[A-Z]{0,2})\b", RegexOptions.IgnoreCase)] private static partial Regex IntelUltra();
    [GeneratedRegex(@"\bRyzen\s+([3579])\s+(PRO\s+)?(\d{4}[A-Z0-9]{0,4})\b", RegexOptions.IgnoreCase)] private static partial Regex Ryzen();

    private static readonly Lazy<(string Read, Dictionary<string, CpuSpec> ByModel)> Table = new(Load);

    /// <summary>When the figures were read from the makers' pages.</summary>
    public static string ReadOn => Table.Value.Read;
    public static int Count => Table.Value.ByModel.Count;

    public static CpuSpec? Find(string? cpuName) => Key(cpuName) is { } key ? Table.Value.ByModel.GetValueOrDefault(key) : null;

    /// <summary>The model number in a processor's name, upper case: "I7-13700KF", "ULTRA 7 265K", "RYZEN 5 PRO 3400G"; null when there is none.</summary>
    public static string? Key(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string s = name.Replace("(R)", " ", StringComparison.OrdinalIgnoreCase).Replace("(TM)", " ", StringComparison.OrdinalIgnoreCase).Replace("®", " ").Replace("™", " ");
        if (IntelUltra().Match(s) is { Success: true } u) return $"ULTRA {u.Groups[1].Value} {u.Groups[2].Value}".ToUpperInvariant();
        if (IntelCore().Match(s) is { Success: true } i) return $"{i.Groups[1].Value}-{i.Groups[2].Value}".ToUpperInvariant();
        if (Ryzen().Match(s) is { Success: true } r) return $"RYZEN {r.Groups[1].Value} {(r.Groups[2].Success ? "PRO " : "")}{r.Groups[3].Value}".ToUpperInvariant();
        return null;
    }

    private sealed record Row(string M, string V, string? Seg, int? C, int? T, int? Base, int? Boost, int? Pbp, int? Mtp, int? Tj, string Src);
    private sealed record File(string Read, List<Row> Cpus);

    private static (string, Dictionary<string, CpuSpec>) Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mazesta.Core.Health.Checkup.cpu-specs.json") ?? throw new InvalidOperationException("cpu-specs.json is not embedded.");
        var file = JsonSerializer.Deserialize<File>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("cpu-specs.json is empty.");
        var byModel = new Dictionary<string, CpuSpec>(StringComparer.Ordinal);
        foreach (var r in file.Cpus)
            byModel.TryAdd(r.M, new(r.M, r.V == "amd" ? HardwareVendor.Amd : HardwareVendor.Intel, r.Seg, r.C, r.T, r.Base, r.Boost, r.Pbp, r.Mtp, r.Tj, r.Src));
        return (file.Read, byModel);
    }
}
