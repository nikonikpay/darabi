namespace Mazesta.Core.Inventory;

/// <summary>
/// What the installed RAM runs at, for the readout over a test or benchmark and for its result: the speed the modules are configured for and the CAS
/// latency, in clocks, of the module's own profile (XMP/EXPO or JEDEC) with that speed. Windows does not report the timings in use, so the latency is
/// the profile's, the way the app's memory page names it, and is left out when no profile has the running speed (a speed set by hand) or the modules'
/// SPD could not be read: never a guess.
/// </summary>
/// <param name="Type">DDR4, DDR5 …; <paramref name="Profile"/> the matching profile's name ("XMP 1", "JEDEC 4800").</param>
public sealed record MemoryFacts(string? Type, int? SpeedMts, int? CasLatency, string? Profile)
{
    /// <summary>What the first module's own SPD says besides the latency: the profile's CL-RCD-RP-RAS, the ranks, the maker of the DRAM chips (Samsung, SK hynix, Micron …) and the bank groups. Left out when not read.</summary>
    public string? Timings { get; init; }
    public int? Ranks { get; init; }
    public string? Chips { get; init; }
    public int? BankGroups { get; init; }

    public static MemoryFacts? From(HardwareInventory inventory, IReadOnlyList<SpdModule> spd)
    {
        var modules = inventory.MemoryModules;
        int? speed = modules.Select(m => m.ConfiguredSpeedMts).FirstOrDefault(s => s is > 0) ?? modules.Select(m => m.SpeedMts).FirstOrDefault(s => s is > 0);
        string? type = modules.Select(m => m.TypeName).FirstOrDefault(t => t is not null) ?? spd.FirstOrDefault()?.MemoryType;
        var profile = spd.Count > 0 ? Ddr4Spd.Matching(spd[0], speed) : null;
        string? profileName = profile?.Name;
        // A speed set by hand matches no profile: the nearest one's latency is still shown, named with its own speed so it is not mistaken for the running one.
        if (profile is null && speed is { } mts && spd.Count > 0 && spd[0].Xmp.Concat(spd[0].Jedec).OrderBy(p => Math.Abs(p.SpeedMts - mts)).FirstOrDefault() is { } near) { profile = near; profileName = $"{near.Name} @ {near.SpeedMts} MT/s"; }
        return speed is null && profile is null && type is null ? null : new(type, speed, profile?.Cl, profileName)
        {
            Timings = profile is null ? null : $"{profile.Cl}-{profile.Trcd}-{profile.Trp}-{profile.Tras}", Ranks = spd.FirstOrDefault()?.Ranks ?? modules.Select(m => m.Ranks).FirstOrDefault(r => r > 0),
            Chips = spd.FirstOrDefault()?.DramManufacturer, BankGroups = spd.FirstOrDefault()?.BankGroups is > 0 and var bg ? bg : null,
        };
    }
}

/// <summary>Reads the RAM's facts when a run asks for them (the answer is cached by whoever provides it); null when they are not known.</summary>
public delegate MemoryFacts? MemoryFactsSource();
