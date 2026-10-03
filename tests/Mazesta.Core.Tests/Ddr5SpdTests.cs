using Xunit; using Mazesta.Core.Inventory;
namespace Mazesta.Core.Tests;

/// <summary>Unlike the DDR4 tests, these bytes are not a real module's: they are written here from the standard's layout (a 5600 module with a
/// 6000 CL30 XMP profile), so they check the arithmetic and the guards, not the offsets against hardware.</summary>
public class Ddr5SpdTests
{
    private static void Word(byte[] spd, int at, int value) { spd[at] = (byte)value; spd[at + 1] = (byte)(value >> 8); }
    private static byte[] Spd(bool xmp = true)
    {
        var spd = new byte[1024];
        spd[2] = 0x12; spd[4] = 0x04; spd[6] = 0x20; spd[7] = 0x40; spd[234] = 0x08; spd[235] = 0x02;   // 16 Gbit dies, x8, 4 bank groups, 2 ranks, no ECC
        Word(spd, 20, 357); Word(spd, 22, 1010);
        spd[24] = 0xFE; spd[25] = 0x7F;   // CL 22-48
        Word(spd, 30, 16000); Word(spd, 32, 16000); Word(spd, 34, 16000); Word(spd, 36, 32000); Word(spd, 38, 48000);
        if (!xmp) return spd;
        spd[640] = 0x0C; spd[641] = 0x4A; spd[642] = 0x30;
        spd[704] = 0x30; spd[705] = 0x27; spd[706] = 0x27;   // VPP 1.80 V, VDD and VDDQ 1.35 V
        Word(spd, 709, 333); Word(spd, 717, 10000); Word(spd, 719, 12654); Word(spd, 721, 12654); Word(spd, 723, 31968); Word(spd, 725, 44622);
        return spd;
    }

    [Fact] public void The_module_s_organisation_is_read()
    {
        var m = Ddr5Spd.Decode(1, Spd(), capacityGb: 32);
        Assert.Equal(("DDR5", 32, 2, 8, 16, 4, false), (m.MemoryType, m.CapacityGb, m.Ranks, m.DeviceWidth, m.DieDensityGbit, m.BankGroups, m.Ecc));
    }

    [Fact] public void The_jedec_bins_run_from_the_module_s_fastest_down_with_even_cas_latencies()
    {
        var j = Ddr5Spd.Decode(1, Spd()).Jedec;
        Assert.Equal([5600, 5200, 4800, 4400, 4000, 3600, 3200], j.Select(p => p.SpeedMts));
        Assert.Equal((46, 45, 45, 90, 135), (j[0].Cl, j[0].Trcd, j[0].Trp, j[0].Tras, j[0].Trc));   // 5600: 16 ns over 357 ps is 44.7, CL the next even one
        Assert.Equal((40, 39, 39, 77, 116), (j[2].Cl, j[2].Trcd, j[2].Trp, j[2].Tras, j[2].Trc));   // 4800
        Assert.All(j, p => Assert.Equal(1.1, p.VoltageV));
    }

    [Fact] public void The_xmp_profile_is_6000_30_38_38_96_at_1_35_volts()
    {
        var m = Ddr5Spd.Decode(1, Spd());
        var x = Assert.Single(m.Xmp);
        Assert.Equal(new MemoryProfile("XMP 1", 6000, 30, 38, 38, 96, 134, 1.35), x with { VoltageV = Math.Round(x.VoltageV!.Value, 2) });
        Assert.Equal("3.0", m.XmpVersion);
        Assert.Equal("XMP 1", Ddr4Spd.Matching(m, 6000)!.Name);
    }

    [Fact] public void A_voltage_byte_that_does_not_read_as_ddr5_s_is_left_out_not_shown()
    {
        var spd = Spd(); spd[704] = 0x05;   // "VPP 0.25 V": this is not the layout we expect
        Assert.Null(Assert.Single(Ddr5Spd.Decode(1, spd).Xmp).VoltageV);
    }

    [Fact] public void No_xmp_header_means_no_profiles_and_a_place_taken_by_expo_is_not_read_as_xmp()
    {
        Assert.Empty(Ddr5Spd.Decode(1, Spd(xmp: false)).Xmp);
        var spd = Spd(); "EXPO"u8.CopyTo(spd.AsSpan(832)); Word(spd, 837, 333); Word(spd, 845, 10000); Word(spd, 847, 12654); Word(spd, 849, 12654); Word(spd, 851, 31968); Word(spd, 853, 44622);
        Assert.Single(Ddr5Spd.Decode(1, spd).Xmp);
    }

    [Fact] public void Anything_but_ddr5_is_refused()
    {
        var spd = Spd(); spd[2] = 0x0C;
        Assert.False(Ddr5Spd.IsDdr5(spd)); Assert.Throws<ArgumentException>(() => Ddr5Spd.Decode(0, spd));
    }
}
