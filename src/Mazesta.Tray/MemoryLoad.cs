using System.Runtime.InteropServices;
namespace Mazesta.Tray;

/// <summary>How much of the physical RAM is in use now, as Windows counts it (the figure Task Manager shows). A cheap call: no sensor driver is opened for it.</summary>
internal static class MemoryLoad
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Status { public uint Length, Load; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual; }
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref Status s);

    public static double? UsedPercent()
    {
        var s = new Status { Length = (uint)Marshal.SizeOf<Status>() };
        return GlobalMemoryStatusEx(ref s) && s.TotalPhys > 0 ? 100.0 * (s.TotalPhys - s.AvailPhys) / s.TotalPhys : null;
    }
}
