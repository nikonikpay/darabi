using System.Text.RegularExpressions;
namespace Mazesta.Core.Inventory;

/// <summary>What a graphics chip is built from. Never read from the card itself (the driver does not say): it is derived from the architecture
/// and core count the driver reports (NVIDIA), or taken from the maker's published specification of that exact model.</summary>
public sealed record GpuUnits(int? ComputeUnits, int? Shaders, int? Tmus, int? Rops, int? RtCores, int? AiCores, string? Process);

/// <summary>A CPU family's platform as its maker publishes it. A model not in a listed family gets nothing, never a neighbour's figures.</summary>
public sealed record CpuPlatform(string Microarchitecture, string Process, string Pcie, string Memory);

public static class GpuFacts
{
    // NVIDIA streaming multiprocessor by CUDA compute capability: cores, texture units, RT cores, tensor cores per SM, and the process of the consumer chips.
    private static readonly Dictionary<string, (int Cores, int Tmu, int Rt, int Tensor, string? Process)> Sm = new()
    {
        ["6.1"] = (128, 8, 0, 0, null), ["7.5"] = (64, 4, 1, 8, "TSMC 12 nm"), ["8.6"] = (128, 4, 1, 4, "Samsung 8 nm"),
        ["8.9"] = (128, 4, 1, 4, "TSMC 4N"), ["12.0"] = (128, 4, 1, 4, "TSMC 4N"),
    };
    // Render outputs per NVIDIA model (they depend on how the chip was cut, not on the SM count).
    private static readonly Dictionary<string, int> NvidiaRops = new()
    {
        ["GTX1650"] = 32, ["GTX1650SUPER"] = 32, ["GTX1660"] = 48, ["GTX1660SUPER"] = 48, ["GTX1660TI"] = 48,
        ["RTX2060"] = 48, ["RTX2060SUPER"] = 64, ["RTX2070"] = 64, ["RTX2070SUPER"] = 64, ["RTX2080"] = 64, ["RTX2080SUPER"] = 64, ["RTX2080TI"] = 88,
        ["RTX3050"] = 32, ["RTX3060"] = 48, ["RTX3060TI"] = 80, ["RTX3070"] = 96, ["RTX3070TI"] = 96, ["RTX3080"] = 96, ["RTX3080TI"] = 112, ["RTX3090"] = 112, ["RTX3090TI"] = 112,
        ["RTX4060"] = 48, ["RTX4060TI"] = 48, ["RTX4070"] = 64, ["RTX4070SUPER"] = 80, ["RTX4070TI"] = 80, ["RTX4070TISUPER"] = 96, ["RTX4080"] = 112, ["RTX4080SUPER"] = 112, ["RTX4090"] = 176,
        ["RTX5060"] = 48, ["RTX5060TI"] = 48, ["RTX5070"] = 64, ["RTX5070TI"] = 96, ["RTX5080"] = 112, ["RTX5090"] = 176,
    };
    private static GpuUnits A(int shaders, int tmu, int rop, int rt, string process, int ai = 0) => new(shaders / 64, shaders, tmu, rop, rt == 0 ? null : rt, ai == 0 ? null : ai, process);
    private static GpuUnits X(int xe, int rop, int xmxPerXe, string process) => new(xe, xe * 128, xe * 8, rop, xe, xe * xmxPerXe, process);
    private static readonly Dictionary<string, GpuUnits> Table = new()
    {
        ["RX5500XT"] = A(1408, 88, 32, 0, "TSMC 7 nm"), ["RX5600XT"] = A(2304, 144, 64, 0, "TSMC 7 nm"), ["RX5700"] = A(2304, 144, 64, 0, "TSMC 7 nm"), ["RX5700XT"] = A(2560, 160, 64, 0, "TSMC 7 nm"),
        ["RX6400"] = A(768, 48, 32, 12, "TSMC 6 nm"), ["RX6500XT"] = A(1024, 64, 32, 16, "TSMC 6 nm"),
        ["RX6600"] = A(1792, 112, 64, 28, "TSMC 7 nm"), ["RX6600XT"] = A(2048, 128, 64, 32, "TSMC 7 nm"), ["RX6650XT"] = A(2048, 128, 64, 32, "TSMC 7 nm"),
        ["RX6700"] = A(2304, 144, 64, 36, "TSMC 7 nm"), ["RX6700XT"] = A(2560, 160, 64, 40, "TSMC 7 nm"), ["RX6750XT"] = A(2560, 160, 64, 40, "TSMC 7 nm"),
        ["RX6800"] = A(3840, 240, 96, 60, "TSMC 7 nm"), ["RX6800XT"] = A(4608, 288, 128, 72, "TSMC 7 nm"), ["RX6900XT"] = A(5120, 320, 128, 80, "TSMC 7 nm"), ["RX6950XT"] = A(5120, 320, 128, 80, "TSMC 7 nm"),
        ["RX7600"] = A(2048, 128, 64, 32, "TSMC 6 nm", 64), ["RX7600XT"] = A(2048, 128, 64, 32, "TSMC 6 nm", 64),
        ["RX7700XT"] = A(3456, 216, 96, 54, "TSMC 5 nm + 6 nm", 108), ["RX7800XT"] = A(3840, 240, 96, 60, "TSMC 5 nm + 6 nm", 120), ["RX7900GRE"] = A(5120, 320, 160, 80, "TSMC 5 nm + 6 nm", 160),
        ["RX7900XT"] = A(5376, 336, 192, 84, "TSMC 5 nm + 6 nm", 168), ["RX7900XTX"] = A(6144, 384, 192, 96, "TSMC 5 nm + 6 nm", 192),
        ["RX9060XT"] = A(2048, 128, 64, 32, "TSMC 4 nm", 64), ["RX9070"] = A(3584, 224, 128, 56, "TSMC 4 nm", 112), ["RX9070XT"] = A(4096, 256, 128, 64, "TSMC 4 nm", 128),
        ["ARCA380"] = X(8, 32, 16, "TSMC 6 nm"), ["ARCA580"] = X(24, 96, 16, "TSMC 6 nm"), ["ARCA750"] = X(28, 112, 16, "TSMC 6 nm"), ["ARCA770"] = X(32, 128, 16, "TSMC 6 nm"),
        ["ARCB570"] = X(18, 72, 8, "TSMC 5 nm"), ["ARCB580"] = X(20, 80, 8, "TSMC 5 nm"),
    };

    /// <summary>"NVIDIA GeForce RTX 4070 Ti SUPER" → "RTX4070TISUPER": brand words dropped, so a laptop part ("…Laptop GPU") never equals a desktop key.</summary>
    internal static string Key(string? name)
        => Regex.Replace(Regex.Replace(name ?? "", @"\(R\)|\(TM\)|\b(NVIDIA|GEFORCE|AMD|RADEON|INTEL|GRAPHICS)\b", "", RegexOptions.IgnoreCase), "[^A-Za-z0-9]", "").ToUpperInvariant();

    public static GpuUnits? For(string? name, string? computeCapability, int? cores)
    {
        string key = Key(name);
        if (computeCapability is not null && cores is { } c)
        {
            if (!Sm.TryGetValue(computeCapability, out var sm) || c % sm.Cores != 0) return null;
            int n = c / sm.Cores; bool rtx = sm.Rt > 0 && !key.StartsWith("GTX", StringComparison.Ordinal);
            return new(n, c, n * sm.Tmu, NvidiaRops.TryGetValue(key, out int rops) ? rops : null, rtx ? n * sm.Rt : null, rtx ? n * sm.Tensor : null, sm.Process);
        }
        return Table.GetValueOrDefault(key);
    }
}

public static class CpuFacts
{
    private static readonly CpuPlatform Zen2 = new("Zen 2 (Matisse)", "TSMC 7 nm (cores) + GlobalFoundries 12 nm (I/O)", "PCIe 4.0, 24 lanes", "DDR4-3200, 2 channels");
    private static readonly CpuPlatform Zen3 = new("Zen 3 (Vermeer)", "TSMC 7 nm (cores) + GlobalFoundries 12 nm (I/O)", "PCIe 4.0, 24 lanes", "DDR4-3200, 2 channels");
    private static readonly CpuPlatform Zen4 = new("Zen 4 (Raphael)", "TSMC 5 nm (cores) + 6 nm (I/O)", "PCIe 5.0, 28 lanes (24 usable + 4 to the chipset)", "DDR5-5200, 2 channels");
    private static readonly CpuPlatform Zen5 = new("Zen 5 (Granite Ridge)", "TSMC 4 nm (cores) + 6 nm (I/O)", "PCIe 5.0, 28 lanes (24 usable + 4 to the chipset)", "DDR5-5600, 2 channels");
    private static readonly string[] Z2 = ["3600", "3600X", "3600XT", "3700X", "3800X", "3800XT", "3900", "3900X", "3900XT", "3950X"];
    private static readonly string[] Z3 = ["5600", "5600X", "5600X3D", "5700X", "5700X3D", "5800", "5800X", "5800XT", "5800X3D", "5900", "5900X", "5900XT", "5950X"];
    private static readonly string[] Z4 = ["7600", "7600X", "7600X3D", "7700", "7700X", "7800X3D", "7900", "7900X", "7900X3D", "7950X", "7950X3D"];
    private static readonly string[] Z5 = ["9600", "9600X", "9700X", "9800X3D", "9900X", "9900X3D", "9950X", "9950X3D"];
    // Suffixes of desktop Intel parts; a mobile one (H, HX, U, P, G7…) is a different chip with other figures.
    private static readonly string[] DesktopSuffix = ["", "K", "KF", "KS", "F", "T", "X"];
    private static readonly CpuPlatform Hybrid = new("", "Intel 7", "PCIe 5.0 x16 + PCIe 4.0 x4 (20 lanes)", "DDR5 or DDR4 (by board), 2 channels");
    private static CpuPlatform Old(string arch, string pcie, string memory) => new(arch, "Intel 14 nm", pcie, memory);

    public static CpuPlatform? For(string? name)
    {
        if (name is null) return null;
        if (Regex.Match(name, @"Ryzen \d (\w+)") is { Success: true } r)
        {
            string m = r.Groups[1].Value;
            return Z2.Contains(m) ? Zen2 : Z3.Contains(m) ? Zen3 : Z4.Contains(m) ? Zen4 : Z5.Contains(m) ? Zen5 : null;
        }
        if (Regex.Match(name, @"Core\(TM\) Ultra [3579] (2\d\d)([A-Z]*)\b") is { Success: true } u)
            return DesktopSuffix.Contains(u.Groups[2].Value) ? new("Arrow Lake-S", "TSMC N3B (compute tile) + N6 (SoC)", "PCIe 5.0 x16 + PCIe 5.0 x4 + PCIe 4.0 x4 (24 lanes)", "DDR5-6400, 2 channels") : null;
        if (Regex.Match(name, @"\bi[3579]-(\d{4,5})([A-Z]*)\b") is not { Success: true } i || !DesktopSuffix.Contains(i.Groups[2].Value)) return null;
        string digits = i.Groups[1].Value; int gen = int.Parse(digits.Length == 5 ? digits[..2] : digits[..1]);
        return gen switch
        {
            14 => Hybrid with { Microarchitecture = "Raptor Lake Refresh" }, 13 => Hybrid with { Microarchitecture = "Raptor Lake" }, 12 => Hybrid with { Microarchitecture = "Alder Lake" },
            11 => Old("Rocket Lake", "PCIe 4.0 x16 + PCIe 4.0 x4 (20 lanes)", "DDR4-3200, 2 channels"),
            10 => Old("Comet Lake", "PCIe 3.0, 16 lanes", "DDR4, 2 channels"), 9 => Old("Coffee Lake Refresh", "PCIe 3.0, 16 lanes", "DDR4, 2 channels"),
            8 => Old("Coffee Lake", "PCIe 3.0, 16 lanes", "DDR4, 2 channels"), 7 => Old("Kaby Lake", "PCIe 3.0, 16 lanes", "DDR4 or DDR3L, 2 channels"),
            6 => Old("Skylake", "PCIe 3.0, 16 lanes", "DDR4 or DDR3L, 2 channels"),
            _ => null,
        };
    }
}
