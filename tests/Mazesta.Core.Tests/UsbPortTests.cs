using Xunit; using Mazesta.Core.Inventory;
namespace Mazesta.Core.Tests;

public class UsbPortTests
{
    [Fact] public void The_port_and_hub_come_from_the_location() { Assert.Equal((3, 1), UsbPort.Parse("Port_#0003.Hub_#0001")); Assert.Null(UsbPort.Parse("0000.0014.0000.003.000")); Assert.Null(UsbPort.Parse(null)); }
    [Fact] public void Only_a_device_in_a_port_counts()
    {
        Assert.True(UsbPort.IsPlugged(@"USB\VID_0781&PID_5567\4C530001"));
        Assert.False(UsbPort.IsPlugged(@"USB\VID_046D&PID_C52B&MI_01\7&1"));
        Assert.False(UsbPort.IsPlugged(@"USB\ROOT_HUB30\4&2"));
        Assert.False(UsbPort.IsPlugged(@"USBSTOR\DISK&VEN_SANDISK"));
    }
}
