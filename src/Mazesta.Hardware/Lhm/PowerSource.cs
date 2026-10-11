using System.Runtime.InteropServices;
namespace Mazesta.Hardware.Lhm;

/// <summary>Whether the machine has a system battery: the laptop test the fan outputs need, read once (Windows answers without WMI or a driver).</summary>
internal static class PowerSource
{
    public static readonly Lazy<bool> HasBattery = new(() => GetSystemPowerStatus(out var s) && s.BatteryFlag is not (128 or 255));

    [StructLayout(LayoutKind.Sequential)] private struct SystemPowerStatus { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
