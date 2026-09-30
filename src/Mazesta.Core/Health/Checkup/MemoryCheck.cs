using System.Text.RegularExpressions; using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using static Mazesta.Core.Health.Checkup.Measures;
namespace Mazesta.Core.Health.Checkup;

/// <summary>
/// The memory as it is set up, from Windows' module list and the modules' own SPD chips: whether it runs at the speed the modules are rated for
/// (DDR4 only - its profiles are the ones decoded; a DDR5 module's rating is not read yet, and is said so rather than guessed), whether it runs in
/// one channel, and whether the modules are a mix. Windows' own "speed" field is not used for a verdict: which speed a board puts there differs.
/// </summary>
public static partial class MemoryCheck
{
    // Slot names that carry the channel letter on common boards: "DIMM_A1", "DIMMA2", "DDR4_B1", "ChannelA-DIMM0", "P0 CHANNEL A".
    [GeneratedRegex(@"(?:DIMM|DDR\d)_?\s?([A-H])\d|CHANNEL\s?-?([A-H])\b", RegexOptions.IgnoreCase)] private static partial Regex ChannelName();

    public static IReadOnlyList<Finding> Evaluate(IReadOnlyList<MemoryModuleInfo> modules, IReadOnlyList<SpdModule> spd)
    {
        var found = new List<Finding>();
        if (modules.Count == 0) return found;
        void Add(FindingCode code, FindingLevel level, IReadOnlyList<Measure> m) => found.Add(new(code, level, HardwareKind.Memory, m));
        int? running = modules.Select(m => m.ConfiguredSpeedMts).Where(v => v > 0).Min();

        var ddr4 = spd.Where(s => s.MemoryType == "DDR4" && s.Jedec.Count > 0).ToList();
        if (running is { } now && ddr4.Count > 0)
        {
            // A kit runs together, so the rating that counts is the slowest module's best.
            int? xmp = ddr4.All(s => s.Xmp.Count > 0) ? ddr4.Min(s => s.Xmp.Max(p => p.SpeedMts)) : null;
            int jedec = ddr4.Min(s => s.Jedec.Max(p => p.SpeedMts));
            if (xmp is { } x && now < x - 2) Add(FindingCode.RamXmpOff, FindingLevel.Attention, [M("Check_M_RamNow", now, MTs), M("Check_M_RamXmp", x, MTs)]);
            else if (xmp is { } y) Add(FindingCode.RamXmpOn, FindingLevel.Good, [M("Check_M_RamNow", now, MTs), M("Check_M_RamXmp", y, MTs)]);
            else if (now < jedec - 2) Add(FindingCode.RamBelowJedec, FindingLevel.Note, [M("Check_M_RamNow", now, MTs), M("Check_M_RamJedec", jedec, MTs)]);
            else Add(FindingCode.RamAtJedec, FindingLevel.Good, [M("Check_M_RamNow", now, MTs), M("Check_M_RamJedec", jedec, MTs)]);
        }
        else if (running is { } r2 && modules.Any(m => m.SmbiosType == 34)) Add(FindingCode.RamProfileUnread, FindingLevel.Note, [M("Check_M_RamNow", r2, MTs)]);

        // Soldered LPDDR is wired by the maker and reports its own way; channels are judged for socketed DDR3/4/5 only.
        var socketed = modules.Where(m => m.SmbiosType is 24 or 26 or 34).ToList();
        if (socketed.Count == 1 && modules.Count == 1) Add(FindingCode.RamSingleChannel, FindingLevel.Attention, [M("Check_M_Modules", 1, None)]);
        else if (socketed.Count >= 2 && socketed.Count == modules.Count)
        {
            var channels = socketed.Select(m => Channel(m.Slot) ?? Channel(m.Bank)).ToList();
            if (channels.All(c => c is not null) && channels.Distinct().Count() == 1) Add(FindingCode.RamSameChannel, FindingLevel.Attention, [M("Check_M_Modules", socketed.Count, None)]);
        }

        var parts = modules.Select(m => m.PartNumber?.Trim()).Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var sizes = modules.Select(m => m.CapacityBytes).Where(c => c > 0).Distinct().Count();
        if (modules.Count >= 2 && (parts > 1 || sizes > 1)) Add(FindingCode.RamMixed, FindingLevel.Note, [M("Check_M_Modules", modules.Count, None)]);
        return found;
    }

    internal static char? Channel(string? slot)
    {
        if (string.IsNullOrWhiteSpace(slot) || ChannelName().Match(slot) is not { Success: true } m) return null;
        return char.ToUpperInvariant((m.Groups[1].Success ? m.Groups[1] : m.Groups[2]).Value[0]);
    }
}
