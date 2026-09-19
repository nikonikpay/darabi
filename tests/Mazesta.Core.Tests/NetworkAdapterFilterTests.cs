using Mazesta.Core.Hardware; using Xunit;
namespace Mazesta.Core.Tests;
public class NetworkAdapterFilterTests
{
    [Theory]
    [InlineData("Wi-Fi-QoS Packet Scheduler-0000")]
    [InlineData("Ethernet 5-WFP Native MAC Layer LightWeight Filter-0000")]
    [InlineData("Wi-Fi-Virtual WiFi Filter Driver-0000")]
    [InlineData("Local Area Connection* 10")]
    [InlineData("Ethernet (Kernel Debugger)")]
    public void Filter_and_debug_bindings_are_virtual(string name) => Assert.True(NetworkAdapterFilter.IsVirtualBinding(name));

    [Theory] [InlineData("Ethernet")] [InlineData("Wi-Fi")] [InlineData("Tailscale")] [InlineData("Ethernet 3")]
    public void Real_adapters_are_not(string name) => Assert.False(NetworkAdapterFilter.IsVirtualBinding(name));
}
