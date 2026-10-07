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
    public static MemoryFacts? From(HardwareInventory inventory, IReadOnlyList<SpdModule> spd)
    {
        var modules = inventory.MemoryModules;
        int? speed = modules.Select(m => m.ConfiguredSpeedMts).FirstOrDefault(s => s is > 0) ?? modules.Select(m => m.SpeedMts).FirstOrDefault(s => s is > 0);
        string? type = modules.Select(m => m.TypeName).FirstOrDefault(t => t is not null) ?? spd.FirstOrDefault()?.MemoryType;
        var profile = spd.Count > 0 ? Ddr4Spd.Matching(spd[0], speed) : null;
        return speed is null && profile is null && type is null ? null : new(type, speed, profile?.Cl, profile?.Name);
    }
}

/// <summary>Reads the RAM's facts when a run asks for them (the answer is cached by whoever provides it); null when they are not known.</summary>
public delegate MemoryFacts? MemoryFactsSource();
