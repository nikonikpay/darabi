using Xunit; using Mazesta.Core.Inventory;
namespace Mazesta.Core.Tests;

/// <summary>The decoder is checked on a real module's SPD (a G.Skill F4-4000C18-32GTZR read on the owner's machine) against what HWiNFO reads
/// from the same module: nothing here is made up, so a wrong offset shows as a wrong number.</summary>
public class Ddr4SpdTests
{
    private static readonly Dictionary<int, string> Rows = new()
    {
        [0] = "23 11 0C 02 86 29 00 08 00 60 00 03 09 03 00 00 00 00 06 0D F8 3F 00 00 6E 6E 6E 11 00 6E F0 0A",
        [32] = "20 08 00 05 00 A8 18 28 28 00 78 00 14 3C 00 00 00 00 00 00 00 00 00 00 00 00 00 00 15 2B 16 36",
        [64] = "0B 2B 0C 36 00 00 36 15 2B 0C 2C 16 2B 0C 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
        [96] = "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 9C 00 00 00 00 00 E7 00 F2 20",
        [128] = "11 11 41 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
        [320] = "04 CD 00 00 00 00 00 00 00 46 34 2D 34 30 30 30 43 31 38 2D 33 32 47 54 5A 52 00 00 00 00 80 AD",
        [384] = "0C 4A 05 20 00 00 00 00 00 A8 00 00 04 00 08 00 00 48 58 58 10 A8 00 F0 0A 20 08 00 05 00 C0 10",
        [416] = "27 00 00 00 00 00 00 00 00 E6 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
        [480] = "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 52 47 42 00 00 00 00 00 00 00 00 00 00 00 00 00",
    };
    private static byte[] Spd()
    {
        var spd = new byte[512];
        foreach (var (at, hex) in Rows) { var b = hex.Split(' '); for (int i = 0; i < b.Length; i++) spd[at + i] = Convert.ToByte(b[i], 16); }
        return spd;
    }

    [Fact] public void The_module_s_organisation_is_read_from_its_spd()
    {
        var m = Ddr4Spd.Decode(2, Spd());
        Assert.Equal("DDR4", m.MemoryType); Assert.Equal(32, m.CapacityGb); Assert.Equal(2, m.Ranks); Assert.Equal(8, m.DeviceWidth);
        Assert.Equal(16, m.DieDensityGbit); Assert.Equal(4, m.BankGroups); Assert.False(m.Ecc);
    }

    [Fact] public void The_xmp_profile_is_4000_18_22_22_42_at_1_40_volts()
    {
        var x = Assert.Single(Ddr4Spd.Decode(2, Spd()).Xmp);   // profile 2 is not enabled on this module
        Assert.Equal(new MemoryProfile("XMP 1", 4000, 18, 22, 22, 42, 64, 1.40), x with { VoltageV = Math.Round(x.VoltageV!.Value, 2) });
    }

    [Fact] public void The_jedec_bins_run_from_2666_down_with_the_timings_hwinfo_shows()
    {
        var j = Ddr4Spd.Decode(2, Spd()).Jedec;
        Assert.Equal([2666, 2400, 2133, 1866, 1600], j.Select(p => p.SpeedMts));
        Assert.Equal((19, 19, 19, 43, 61), (j[0].Cl, j[0].Trcd, j[0].Trp, j[0].Tras, j[0].Trc));   // HWiNFO: 1333 MHz 19-19-19-43 tRC 61
        Assert.Equal((17, 17, 17, 39, 55), (j[1].Cl, j[1].Trcd, j[1].Trp, j[1].Tras, j[1].Trc));   // 1200 MHz 17-17-17-39 55
        Assert.Equal((15, 15, 15, 35, 49), (j[2].Cl, j[2].Trcd, j[2].Trp, j[2].Tras, j[2].Trc));   // 1067 MHz 15-15-15-35 49
    }

    [Fact] public void The_running_profile_is_matched_by_speed_and_a_speed_set_by_hand_matches_none()
    {
        var m = Ddr4Spd.Decode(2, Spd());
        Assert.Equal("JEDEC 2133", Ddr4Spd.Matching(m, 2133)!.Name);   // this machine: XMP is off
        Assert.Equal("XMP 1", Ddr4Spd.Matching(m, 4000)!.Name);
        Assert.Null(Ddr4Spd.Matching(m, 3600));
        Assert.Null(Ddr4Spd.Matching(m, null));
    }

    [Fact] public void Anything_but_ddr4_is_refused_rather_than_misread()
    {
        var spd = Spd(); spd[2] = 0x12;   // DDR5
        Assert.False(Ddr4Spd.IsDdr4(spd)); Assert.Throws<ArgumentException>(() => Ddr4Spd.Decode(0, spd));
    }
}
