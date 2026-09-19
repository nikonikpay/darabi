namespace Mazesta.Hardware.Lhm;

/// <summary>
/// LibreHardwareMonitor lists every binding of every adapter as its own node ("Wi-Fi-QoS Packet
/// Scheduler-0000", "Ethernet 5-WFP Native MAC Layer LightWeight Filter-0000", Wi-Fi Direct
/// "Local Area Connection* 10", the kernel-debug NIC). They carry no independent traffic and turn the
/// network section into 40 mostly-empty rows, so they are not exposed.
/// </summary>
internal static class NetworkAdapterFilter
{
    private static readonly string[] Markers = ["-QoS Packet Scheduler", "-WFP ", "Filter Driver", "Kernel Debugger", "Local Area Connection*"];

    public static bool IsVirtualBinding(string adapterName) => Markers.Any(m => adapterName.Contains(m, StringComparison.OrdinalIgnoreCase));
}
