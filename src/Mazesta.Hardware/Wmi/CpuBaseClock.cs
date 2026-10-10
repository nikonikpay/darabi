using System.Management;
namespace Mazesta.Hardware.Wmi;

/// <summary>The CPU's rated clock as Windows reports it (Win32_Processor.MaxClockSpeed, the base clock on most machines), read once; null when unreadable.</summary>
public static class CpuBaseClock
{
    private static readonly Lazy<double?> Value = new(() =>
    {
        try { return new WmiQuery().Query(@"root\cimv2", "SELECT MaxClockSpeed FROM Win32_Processor").FirstOrDefault()?["MaxClockSpeed"] is { } v && Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture) is > 0 and var mhz ? mhz : null; }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidCastException) { return null; }
    });
    public static double? Mhz => Value.Value;
}
