using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using static Mazesta.Core.Health.Checkup.Measures;
namespace Mazesta.Core.Health.Checkup;

/// <summary>Windows' power state and the active power plan's processor settings, for the source it is on now (mains or battery). A value that
/// could not be read is null. <see cref="BoostMode"/> is the plan's "processor performance boost mode" (0 = disabled).</summary>
public sealed record PowerFacts(bool? OnMains, bool? HasBattery, int? MaxProcessorPercent, int? BoostMode);

/// <summary>A drive's PCI Express link as its slot's port negotiated it, and what the drive and the slot can do.</summary>
public sealed record DriveLink(string? Name, int? Gen, int? Width, int? DriveGen, int? DriveWidth, int? SlotGen, int? SlotWidth);

/// <summary>A display adapter as Windows lists it, and whether a screen on it is part of the desktop now.</summary>
public sealed record DisplayAdapter(string Name, bool DrivesScreen);

/// <summary>The machine's settings that hold every part back without being a part's fault: the power plan, the battery, an NVMe drive in a slot
/// narrower or older than the drive.</summary>
public static class PlatformCheck
{
    public static IReadOnlyList<Finding> Power(PowerFacts p)
    {
        var found = new List<Finding>();
        // Any cap under 100 % keeps Windows from asking for more than the base clock, so boost is gone with it (99 % is the well-known way to turn it off).
        if (p.MaxProcessorPercent is { } max and < 100) found.Add(new(FindingCode.PowerPlanCap, FindingLevel.Attention, HardwareKind.Cpu, [M("Check_M_MaxState", max, Percent)]));
        if (p.BoostMode == 0) found.Add(new(FindingCode.PowerPlanNoBoost, FindingLevel.Attention, HardwareKind.Cpu, []));
        if (p.OnMains == false && p.HasBattery == true) found.Add(new(FindingCode.PowerOnBattery, FindingLevel.Note, HardwareKind.Cpu, []));
        if (found.Count == 0 && p.MaxProcessorPercent is not null) found.Add(new(FindingCode.PowerPlanOk, FindingLevel.Good, HardwareKind.Cpu, [M("Check_M_MaxState", p.MaxProcessorPercent.Value, Percent)]));
        return found;
    }

    /// <summary>A drive's link is judged by width only when the slot's side is known: a generation read at rest may be lowered to save power.</summary>
    public static IReadOnlyList<Finding> Drives(IEnumerable<DriveLink> drives)
    {
        var found = new List<Finding>();
        foreach (var d in drives)
        {
            if (d.Width is not { } w || d.DriveWidth is not { } dw) continue;
            int expect = Math.Min(dw, d.SlotWidth ?? dw);
            if (w < expect && d.SlotWidth is null) found.Add(new(FindingCode.DriveLinkBelowDrive, FindingLevel.Note, HardwareKind.Storage, [M("Check_M_LinkWidth", w, Lanes), M("Check_M_CardWidth", dw, Lanes)], d.Name));
            else if (w < expect) found.Add(new(FindingCode.DriveLinkNarrow, FindingLevel.Attention, HardwareKind.Storage, [M("Check_M_LinkWidth", w, Lanes), M("Check_M_LinkWidthExpected", expect, Lanes)], d.Name));
            else if (d.SlotWidth is { } sw && sw < dw || d.SlotGen is { } sg && d.DriveGen is { } dg && sg < dg)
                found.Add(new(FindingCode.DriveSlotLimited, FindingLevel.Note, HardwareKind.Storage,
                    [.. d.SlotGen is { } s1 ? [M("Check_M_SlotGen", s1, None)] : Array.Empty<Measure>(), .. d.SlotWidth is { } s2 ? [M("Check_M_SlotWidth", s2, Lanes)] : Array.Empty<Measure>(),
                     .. d.DriveGen is { } d1 ? [M("Check_M_CardGen", d1, None)] : Array.Empty<Measure>(), M("Check_M_CardWidth", dw, Lanes)], d.Name));
        }
        return found;
    }

    /// <summary>A desktop with a graphics card whose screens all hang on the processor's graphics: the cable is in the board's port, so games
    /// and the tests run on the weak chip. Not on a laptop: there the processor's graphics drives the panel by design and hands heavy work over.</summary>
    public static IReadOnlyList<Finding> Screen(IEnumerable<DisplayAdapter> adapters, bool laptop)
    {
        if (laptop) return [];
        var all = adapters.GroupBy(a => a.Name).Select(g => new DisplayAdapter(g.Key, g.Any(a => a.DrivesScreen))).ToList();
        var cards = all.Where(a => Card(a.Name)).ToList();
        if (cards.Count == 0 || cards.Any(c => c.DrivesScreen)) return [];
        return [.. all.Where(a => a.DrivesScreen && GpuKind.Integrated(a.Name, false)).Take(1)
            .Select(a => new Finding(FindingCode.ScreenOnIntegrated, FindingLevel.Attention, HardwareKind.Gpu, [], cards[0].Name))];
    }

    // A real graphics card by its maker's name, so a virtual or remote-desktop adapter never counts as one.
    private static bool Card(string name) => !GpuKind.Integrated(name, false)
        && (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || name.Contains("Intel", StringComparison.OrdinalIgnoreCase));

    /// <summary>The link as <see cref="PciSlotLink"/> holds it, for a drive.</summary>
    public static DriveLink Of(string? name, PciSlotLink s) => new(name, s.Port.CurrentGen, s.Port.CurrentWidth, s.CardMaxGen, s.CardMaxWidth, s.Port.MaxGen, s.Port.MaxWidth);
}
