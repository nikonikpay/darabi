using Xunit; using Mazesta.Hardware.Rgb;
namespace Mazesta.Hardware.Tests;

public class AsusAuraOnboardTests
{
    private const string Hid = @"HID: \\?\HID#VID_0B05&PID_1BED&MI_02#8&3b7d768a&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private static RgbDevice Board(string name, string location = Hid, params RgbZone[] zones)
    {
        if (zones.Length == 0) zones = [new(0, "Addressable RGB Header 1", 1, 0, 500, 40), new(1, "Addressable RGB Header 2", 1, 0, 500, 40)];
        int leds = zones.Sum(z => (int)z.LedsCount);
        return new(3, RgbKind.Motherboard, name, "ASUS", "", location, [], 0, leds, [.. new RgbColor[leds]], zones);
    }

    [Fact] public void Report_applies_on_the_channel_and_repeats_the_colours_over_the_lights()
    {
        var r = AsusAuraOnboard.Report(6, [new RgbColor(1, 2, 3), new RgbColor(4, 5, 6), new RgbColor(7, 8, 9)]);
        Assert.Equal(65, r.Length); Assert.Equal(new byte[] { 0xEC, 0x40, 0x86, 0, 20, 1, 2, 3, 4, 5, 6, 7, 8, 9, 1, 2, 3 }, r[..17]);
    }

    [Fact] public void A_board_where_the_channel_was_seen_gets_a_zone_on_that_channel_alone()
    {
        var d = AsusAuraOnboard.Extend(Board("ASUS ROG STRIX X870E-E GAMING WIFI7 NEO"), _ => null);
        Assert.Equal([(byte)6], d.Onboard!.Channels); Assert.Equal(83, d.LedCount); Assert.Equal(83, d.Colors.Count);
        Assert.Equal(AsusAuraOnboard.SeenZone, d.Zones[^1].Name); Assert.False(d.Zones[^1].Resizable); Assert.Equal([40, 40, 3], d.Runs());
    }

    [Fact] public void Another_such_asus_board_gets_the_channels_past_the_headers_and_a_zone_that_says_if()
    {
        var d = AsusAuraOnboard.Extend(Board("ASUS TUF GAMING B650-PLUS"), _ => [new RgbColor(9, 9, 9)]);
        Assert.Equal([(byte)4, (byte)5, (byte)6, (byte)7], d.Onboard!.Channels); Assert.Equal(AsusAuraOnboard.LikelyZone, d.Zones[^1].Name);
        Assert.Equal(new RgbColor(9, 9, 9), d.Colors[80]);   // what was last written
    }

    [Fact] public void No_other_board_is_touched()
    {
        Assert.Null(AsusAuraOnboard.Find(Board("MSI MAG B650", @"HID: \\?\HID#VID_1462&PID_7E06#x")));
        Assert.Null(AsusAuraOnboard.Find(Board("ASUS PRIME", Hid, new RgbZone(0, "Aura Mainboard", 1, 6, 6, 6), new RgbZone(1, "Addressable RGB Header 1", 1, 0, 120, 30))));   // its lights are counted
        Assert.Null(AsusAuraOnboard.Find(Board("ASUS card") with { Kind = RgbKind.Graphics }));
    }
}
