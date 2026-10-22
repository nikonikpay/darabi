using System.Runtime.InteropServices; using Mazesta.Core.Health.Checkup;
namespace Mazesta.Hardware.Details;

/// <summary>The display adapters' outputs as Windows lists them (one row per output), each with whether a screen on it is part of the desktop.</summary>
public static class DisplayAdapters
{
    private const int AttachedToDesktop = 0x1;

    public static IReadOnlyList<DisplayAdapter> Read()
    {
        var list = new List<DisplayAdapter>();
        var d = new DisplayDevice { Cb = Marshal.SizeOf<DisplayDevice>() };
        for (uint i = 0; EnumDisplayDevicesW(null, i, ref d, 0); i++, d.Cb = Marshal.SizeOf<DisplayDevice>())
            if (!string.IsNullOrWhiteSpace(d.DeviceString)) list.Add(new(d.DeviceString.Trim(), (d.StateFlags & AttachedToDesktop) != 0));
        return list;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevicesW(string? device, uint index, ref DisplayDevice info, uint flags);
}
