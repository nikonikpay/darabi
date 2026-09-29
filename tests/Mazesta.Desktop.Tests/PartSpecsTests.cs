using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Xunit;
namespace Mazesta.Desktop.Tests;

public class PartSpecsTests
{
    private static SpdModule Module(int slot, string part, params MemoryProfile[] xmp)
        => new(slot, "DDR4", part, "G Skill Intl", "SK Hynix", 32, 2, 8, 16, 4, false, null, [new("JEDEC 2133", 2133, 15, 15, 15, 35, 49, 1.2)], xmp, xmp.Length > 0 ? "2.0" : null);
    private static MemoryModuleInfo Dimm(string slot, string part, int speed = 2133) => new(slot, 32L << 30, "G Skill Intl", part, speed, speed, "BANK 1", 2, 1200, 26, 64, 64);
    private static HardwareInventory Inventory(params MemoryModuleInfo[] modules) => HardwareInventory.Empty with { MemoryModules = modules, TotalPhysicalMemoryBytes = 64L << 30 };
    private static string Row(SpecCard c, string key) => c.Rows.Single(r => r.Label == Loc.Get(key)).Value;

    [Fact] public void A_link_reads_as_generation_lanes_and_transfer_rate()
    {
        Assert.Equal("PCIe 4.0 x16 (16 GT/s)", PartSpecs.Link(4, 16));
        Assert.Equal("PCIe 3.0 x4 (8 GT/s)", PartSpecs.Link(3, 4));
        Assert.Null(PartSpecs.Link(null, null));
    }

    [Fact] public void The_cpu_identifier_shows_decimal_and_hex()
        => Assert.Equal("Family 23 (17h) · Model 113 (71h) · Stepping 0", PartSpecs.Identifier("AMD64 Family 23 Model 113 Stepping 0"));

    [Fact] public void Modules_and_their_spd_pair_up_and_the_profile_at_the_running_speed_is_marked()
    {
        var xmp = new MemoryProfile("XMP 1", 4000, 18, 22, 22, 42, 64, 1.4);
        var details = HardwareDetails.Empty with { Spd = [Module(2, "F4-4000C18-32GTZR", xmp), Module(3, "F4-4000C18-32GTZR", xmp)] };
        var cards = PartSpecs.For(HardwareKind.Memory, Inventory(Dimm("DIMM_A2", "F4-4000C18-32GTZR"), Dimm("DIMM_B2", "F4-4000C18-32GTZR")), details);
        Assert.Equal(3, cards.Count);   // the summary and one card per module
        Assert.Equal("JEDEC 2133", Row(cards[0], "Spec_ProfileNow"));
        Assert.Contains("XMP 1 4000", Row(cards[0], "Spec_XmpState"));   // XMP is offered but not in use
        Assert.Equal("DIMM_A2 (BANK 1)", cards[1].Title);
        Assert.Equal(1, cards[1].Table!.Highlight);                          // XMP 1 is row 0, JEDEC 2133 row 1
    }

    [Fact] public void Spd_that_disagrees_with_smbios_is_shown_apart_never_paired_by_guess()
    {
        var details = HardwareDetails.Empty with { Spd = [Module(0, "OTHER-PART")] };
        var cards = PartSpecs.For(HardwareKind.Memory, Inventory(Dimm("DIMM_A2", "F4-4000C18-32GTZR")), details);
        Assert.Equal(3, cards.Count);
        Assert.Null(cards[1].Table);                                         // the SMBIOS module has no profiles pinned on it
        Assert.Equal(Loc.Format("Spec_SpdSlot", 0), cards[2].Title);
        Assert.DoesNotContain(cards[0].Rows, r => r.Label == Loc.Get("Spec_ProfileNow"));
    }

    [Fact] public void Without_spd_the_summary_says_why_there_are_no_profiles()
        => Assert.Equal(Loc.Get("Spec_NoSpd"), PartSpecs.For(HardwareKind.Memory, Inventory(Dimm("DIMM_A2", "X")), HardwareDetails.Empty)[0].Note);
}
