using System.Runtime.InteropServices; using Mazesta.Core.Inventory;
namespace Mazesta.Hardware.Details;

/// <summary>
/// A PnP device's PCI Express link and its place in the device tree, from the properties Windows keeps for every PCI device
/// (DEVPKEY_PciDevice_CurrentLinkSpeed/Width, MaxLinkSpeed/Width; the speed is the PCIe generation). Works for any vendor's card, NVMe controller or
/// network adapter; a device that is not PCI Express has none and gives null. Checked on the owner's machine against HWiNFO: RTX 3090 Gen4 x16,
/// NVMe controller Gen3 x4.
/// </summary>
internal static class PciDevice
{
    private static readonly Guid PciKeys = new("3AB22E31-8264-4b4e-9AF5-A8D2D8E33E62"), DeviceKeys = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");

    public static PciLinkInfo? Link(string? pnpDeviceId)
    {
        if (string.IsNullOrEmpty(pnpDeviceId) || CM_Locate_DevNodeW(out uint dev, pnpDeviceId, 0) != 0) return null;
        int? P(uint pid) => Uint(dev, PciKeys, pid) is { } v and > 0 and < 64 ? (int)v : null;
        var link = new PciLinkInfo(P(9), P(10), P(11), P(12));
        return link.CurrentGen is null && link.MaxGen is null && link.CurrentWidth is null ? null : link;
    }

    /// <summary>The PCI bus number of a device (DEVPKEY_Device_BusNumber), to pair it with what a vendor library reports by bus.</summary>
    public static int? BusNumber(string? pnpDeviceId)
        => !string.IsNullOrEmpty(pnpDeviceId) && CM_Locate_DevNodeW(out uint dev, pnpDeviceId, 0) == 0 && Uint(dev, DeviceKeys, 23) is { } v ? (int)v : null;

    /// <summary>The device a device hangs from (a disk's controller), walking up until <paramref name="accept"/> says yes; null at the root.</summary>
    public static string? Ancestor(string? pnpDeviceId, Func<string, bool> accept)
    {
        if (string.IsNullOrEmpty(pnpDeviceId) || CM_Locate_DevNodeW(out uint dev, pnpDeviceId, 0) != 0) return null;
        for (int depth = 0; depth < 8 && CM_Get_Parent(out uint parent, dev, 0) == 0; depth++, dev = parent)
        {
            var id = new char[512];
            if (CM_Get_Device_IDW(parent, id, id.Length, 0) != 0) return null;
            string s = new string(id).TrimEnd('\0');
            if (accept(s)) return s;
        }
        return null;
    }

    private static uint? Uint(uint dev, Guid set, uint pid)
    {
        var key = new DevPropKey { Fmtid = set, Pid = pid }; var buffer = new byte[4]; uint size = 4;
        return CM_Get_DevNode_PropertyW(dev, ref key, out _, buffer, ref size, 0) == 0 && size == 4 ? BitConverter.ToUInt32(buffer) : null;
    }

    [StructLayout(LayoutKind.Sequential)] private struct DevPropKey { public Guid Fmtid; public uint Pid; }
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, int flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_DevNode_PropertyW(uint devInst, ref DevPropKey key, out uint type, byte[] buffer, ref uint size, int flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Parent(out uint parent, uint devInst, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_IDW(uint devInst, char[] buffer, int length, int flags);
}
