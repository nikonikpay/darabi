using System.Globalization; using Mazesta.Core.Inventory; using Mazesta.Diagnostics.Cpu;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>One fact about the part a run measured or the system it ran on, as read from the machine: <see cref="Group"/> is "part" or
/// "system", <see cref="Key"/> a localisation key for its name, <see cref="Value"/> the reading as text. A fact that could not be read is left
/// out, never guessed.</summary>
public sealed record SpecItem(string Group, string Key, string Value);

/// <summary>
/// What a run's numbers mean beside its headline: which of its metrics describe the conditions it ran in (clocks, temperatures, power, which
/// the monitor measured over the run) rather than its result, and the part's and system's specifications kept with each logged run, so a
/// result can be judged against another (the same CPU at another clock, the same GPU running hot).
/// </summary>
public static class BenchmarkDetails
{
    public const string PartGroup = "part", SystemGroup = "system", RunGroup = "run";

    /// <summary>The metrics that are measured conditions of the run, not results of the work.</summary>
    public static readonly IReadOnlySet<string> ConditionKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "Bench_Threads", "Bench_Cpu_Clock", "Bench_Cpu_ClockPeak", "Bench_Cpu_PClock", "Bench_Cpu_EClock", "Bench_Cpu_Power", "Bench_Cpu_Vcore", "Bench_Cpu_TempAvg", "Bench_Cpu_TempMax",
        "Bench_Gpu_Clock", "Bench_Gpu_MemClock", "Bench_Gpu_Load", "Bench_Gpu_Power", "Bench_Gpu_Voltage", "Bench_Gpu_TempAvg", "Bench_Gpu_TempMax", "Bench_Gpu_HotSpotMax",
        "Bench_Gpu_VramTempMax", "Bench_Gpu_Fan",
        "Bench_Gpu_ClockMax", "Bench_Gpu_MemClockMax", "Bench_Gpu_LoadMax", "Bench_Gpu_PowerMax", "Bench_Gpu_VramUsedMax",
        "Bench_Cpu_PClockMax", "Bench_Cpu_EClockMax", "Bench_Cpu_Load", "Bench_Cpu_LoadMax", "Bench_Cpu_PowerMax", "Bench_Scene_GpuBusy",
    };
    /// <summary>A measured condition of the run, not a result of its work: the listed keys, the RAM's figures (its use, speed and CL; the bandwidth and
    /// latency of the quick probe are conditions of the score they make), and a CCD's clock.</summary>
    public static bool IsCondition(string key) => ConditionKeys.Contains(key) || key.StartsWith("Bench_Ram_", StringComparison.Ordinal) || key.StartsWith("Bench_App_", StringComparison.Ordinal) || IsCcd(key);
    private static bool IsCcd(string key) => key.StartsWith("Bench_Cpu_Ccd", StringComparison.Ordinal);

    /// <summary>The part a condition belongs to ("Gpu", "Cpu", "Ram"): the page folds each part's figures under a heading of its own, closed until opened.
    /// Null for a condition of no part (the thread count).</summary>
    public static string? Section(string key) => !IsCondition(key) ? null
        : key.StartsWith("Bench_Gpu_", StringComparison.Ordinal) ? "Gpu" : key.StartsWith("Bench_Cpu_", StringComparison.Ordinal) ? "Cpu" : key.StartsWith("Bench_Ram_", StringComparison.Ordinal) ? "Ram" : key.StartsWith("Bench_App_", StringComparison.Ordinal) ? "App" : null;

    /// <summary>Whether a result is one that can be won: the shares and the RAM's effect (what the score's model says of where the cost lies) are not.</summary>
    public static bool HasDirection(string key) => !IsCondition(key) && !key.StartsWith("Bench_Scene_Share", StringComparison.Ordinal) && key != "Bench_Scene_RamEffect";

    /// <summary>Whether a higher value of a result is the better one: false for times (ms, ns), true for everything else a result counts.</summary>
    public static bool HigherIsBetter(string unit) => unit is not ("ms" or "ns" or "s" or "µs");

    /// <summary>The cores as Windows reports them: "8 P + 16 E" on a hybrid CPU (performance cores are the higher efficiency class), else the count.</summary>
    public static (int Performance, int Efficient, int Threads) Split(IReadOnlyList<CpuCore> cores)
    {
        if (cores.Count == 0) return (0, 0, 0);
        byte top = cores.Max(c => c.EfficiencyClass);
        bool hybrid = cores.Any(c => c.EfficiencyClass != top);
        int p = hybrid ? cores.Count(c => c.EfficiencyClass == top) : cores.Count;
        return (p, cores.Count - p, cores.Sum(c => c.Threads));
    }

    public static IEnumerable<SpecItem> Cpu(CpuInfo? cpu, IReadOnlyList<CpuCore> cores)
    {
        var (p, e, threads) = Split(cores);
        if (p > 0) yield return new(PartGroup, "Spec_Cores", e > 0 ? Inv($"{p} P + {e} E ({p + e})") : Inv($"{p}"));
        else if (cpu?.PhysicalCores is > 0 and var n) yield return new(PartGroup, "Spec_Cores", Inv($"{n}"));
        if (threads > 0) yield return new(PartGroup, "Spec_Threads", Inv($"{threads}"));
        else if (cpu?.LogicalProcessors is > 0 and var l) yield return new(PartGroup, "Spec_Threads", Inv($"{l}"));
        if (cpu?.MaxClockMhz is > 0 and var mhz) yield return new(PartGroup, "Spec_RatedClock", Inv($"{mhz / 1000.0:0.00} GHz"));
        if (Text(cpu?.Socket) is { } socket) yield return new(PartGroup, "Spec_Socket", socket);
    }

    /// <summary><paramref name="vramBytes"/> is the card's own total (the monitor reads it; Windows' WMI caps it at 4 GB), or null.</summary>
    public static IEnumerable<SpecItem> Gpu(GpuInfo? gpu, double? vramBytes, int discrete = 0)
    {
        // one card runs a GPU test; the others (an integrated GPU never counts, nor does a card the test cannot open) are named only as the cards present
        if (discrete > 0) yield return new(PartGroup, "Spec_GpuCount", discrete > 1 ? Inv($"1 / {discrete}") : "1");
        if (vramBytes is > 0 and var v) yield return new(PartGroup, "Spec_Vram", Size(v));
        if (Text(gpu?.DriverVersion) is { } driver) yield return new(PartGroup, "Spec_Driver", driver);
    }

    public static IEnumerable<SpecItem> Memory(HardwareInventory inv)
    {
        if (MemorySummary(inv) is { } ram) yield return new(PartGroup, "Spec_Memory", ram);
        var parts = inv.MemoryModules.Where(m => m.CapacityBytes > 0).GroupBy(m => string.Join(" ", new[] { Text(m.Manufacturer), Text(m.PartNumber) }.OfType<string>()))
            .Where(g => g.Key.Length > 0).Select(g => g.Count() > 1 ? Inv($"{g.Key} ×{g.Count()}") : g.Key).ToList();
        if (parts.Count > 0) yield return new(PartGroup, "Spec_Modules", string.Join(" + ", parts));
    }

    public static IEnumerable<SpecItem> Drive(StorageDeviceInfo? d)
    {
        if (d is null) yield break;
        if (Text(d.BusType) is { } bus) yield return new(PartGroup, "Spec_Bus", bus);
        if (Text(d.MediaType) is { } media) yield return new(PartGroup, "Spec_Media", media);
        if (d.SizeBytes is > 0 and var size) yield return new(PartGroup, "Spec_Capacity", Size(size, decimalUnits: true));
        if (Text(d.FirmwareVersion) is { } fw) yield return new(PartGroup, "Spec_Firmware", fw);
    }

    /// <summary>The rest of the machine, for context: the CPU unless it is the part measured, the memory unless it is, the board, BIOS and Windows.</summary>
    public static IEnumerable<SpecItem> Machine(HardwareInventory inv, PeerPart part)
    {
        if (part != PeerPart.Cpu && Text(inv.Cpu?.Name) is { } cpu) yield return new(SystemGroup, "Spec_Cpu", BenchmarkPeers.PartName(cpu));
        if (part != PeerPart.Memory && MemorySummary(inv) is { } ram) yield return new(SystemGroup, "Spec_Memory", ram);
        if (inv.Motherboard is { } b && Text($"{b.Manufacturer} {b.Product}") is { } board) yield return new(SystemGroup, "Spec_Board", board);
        if (Text(inv.Bios?.Version) is { } bios) yield return new(SystemGroup, "Spec_Bios", inv.Bios?.ReleaseDate is { } date ? Inv($"{bios} ({date:yyyy-MM-dd})") : bios);
        if (inv.Os is { } os && Text($"{os.Caption} {os.BuildNumber}") is { } windows) yield return new(SystemGroup, "Spec_Os", windows.Replace("Microsoft ", "", StringComparison.Ordinal));
    }

    /// <summary>The installed memory as the modules report it: "32 GB (2×16 GB) 6000 MT/s". Null when the modules cannot be read.</summary>
    public static string? MemorySummary(HardwareInventory inv)
    {
        var modules = inv.MemoryModules.Where(m => m.CapacityBytes > 0).ToList();
        if (modules.Count == 0) return null;
        long total = modules.Sum(m => m.CapacityBytes!.Value) >> 30;
        string sizes = string.Join(" + ", modules.GroupBy(m => m.CapacityBytes!.Value >> 30).OrderByDescending(g => g.Key).Select(g => $"{g.Count()}×{g.Key} GB"));
        int speed = modules.Select(m => m.ConfiguredSpeedMts ?? m.SpeedMts ?? 0).Where(v => v > 0).DefaultIfEmpty(0).Min();
        return Inv($"{total} GB ({sizes})") + (speed > 0 ? Inv($" {speed} MT/s") : "");
    }

    private static string Size(double bytes, bool decimalUnits = false)
    {
        double gb = bytes / (decimalUnits ? 1e9 : 1 << 30);
        return gb >= 1000 ? Inv($"{gb / 1000:0.#} TB") : gb >= 10 ? Inv($"{gb:0} GB") : Inv($"{gb:0.#} GB");
    }
    private static string? Text(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
