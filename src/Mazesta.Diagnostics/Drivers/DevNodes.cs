using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Drivers;

/// <summary>
/// A device's driver as the Plug and Play manager has it now (CfgMgr32), not as WMI's Win32_PnPSignedDriver lists it: that list is refreshed
/// lazily and still shows the old driver for some time after a driver is installed, which would report a successful update as not done.
/// </summary>
public static class DevNodes
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Category; public uint Id; }

    private static readonly Guid DriverProps = new("a8b865dd-2e3d-4094-ad97-e593a70c75d6");   // DEVPKEY_Device_DriverDate (2), DriverVersion (3)
    private const uint TypeString = 0x12, TypeFileTime = 0x10;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern int CM_Get_DevNode_PropertyW(uint devInst, ref PropertyKey key, out uint type, byte[]? buffer, ref uint size, uint flags);

    /// <summary>The driver's version and date on the device now; nulls when the device is not present or does not say.</summary>
    public static (string? Version, DateOnly? Date) Driver(string deviceId)
    {
        if (CM_Locate_DevNodeW(out uint node, deviceId, 0) != 0) return (null, null);
        string? version = Read(node, 3, TypeString) is { } v ? System.Text.Encoding.Unicode.GetString(v).TrimEnd('\0') : null;
        DateOnly? date = Read(node, 2, TypeFileTime) is { Length: 8 } d ? DateOnly.FromDateTime(DateTime.FromFileTimeUtc(BitConverter.ToInt64(d))) : null;
        return (version is { Length: > 0 } ? version : null, date);
    }

    private static byte[]? Read(uint node, uint id, uint want)
    {
        var key = new PropertyKey { Category = DriverProps, Id = id }; uint size = 0;
        CM_Get_DevNode_PropertyW(node, ref key, out _, null, ref size, 0);   // CR_BUFFER_SMALL, with the size
        if (size == 0) return null;
        var buffer = new byte[size];
        return CM_Get_DevNode_PropertyW(node, ref key, out uint type, buffer, ref size, 0) == 0 && type == want ? buffer : null;
    }
}
