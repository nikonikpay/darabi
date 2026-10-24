using Mazesta.Core.Windows; using Xunit;
namespace Mazesta.Core.Tests;

public class WakeSourcesTests
{
    [Fact] public void A_line_that_is_not_a_programmable_device_is_never_a_device()   // a localized "none" line of powercfg must not become a device
    {
        var d = WakeSources.Devices(["HID Keyboard Device", "Realtek PCIe GbE Family Controller"], ["هیچ", "HID Keyboard Device"], ["Realtek PCIe GbE Family Controller"]);
        Assert.Equal(2, d.Count);
        Assert.True(d.Single(x => x.Name == "HID Keyboard Device").Armed);
        Assert.False(d.Single(x => x.Kind == WakeKind.Network).Armed);
    }

    [Theory]
    [InlineData("HID-compliant mouse", WakeKind.Input), InlineData("Logitech USB Receiver", WakeKind.Usb), InlineData("Intel(R) Ethernet", WakeKind.Network), InlineData("PCI Express Root Port", WakeKind.Other)]
    public void Devices_are_grouped_by_what_their_name_and_the_adapter_list_say(string name, WakeKind kind)
        => Assert.Equal(kind, WakeSources.Classify(name, new HashSet<string>(["Intel(R) Ethernet"])));

    [Fact] public void The_timer_setting_is_the_last_two_numbers_in_any_language()
    {
        string[] fa = ["GUID: bd3b718a", "حداکثر 0x00000002", "فهرست 0x00000000", "شاخص فعلی برق: 0x00000001", "شاخص فعلی باتری: 0x00000002"];
        Assert.Equal((1, 2), WakeSources.TimerSetting(fa));
        Assert.Equal((null, null), WakeSources.TimerSetting(["nothing here"]));
    }

    [Fact] public void Names_are_trimmed_and_listed_once()
        => Assert.Single(WakeSources.Devices(["  HID Keyboard Device ", "HID Keyboard Device"], [], []));
}
