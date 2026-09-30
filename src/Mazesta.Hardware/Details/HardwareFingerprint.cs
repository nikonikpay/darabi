using System.Runtime.InteropServices; using System.Security.Cryptography; using System.Text; using Microsoft.Win32;
namespace Mazesta.Hardware.Details;

/// <summary>
/// A cheap identity of the machine's parts, read in milliseconds from the registry and the device manager (no WMI, no sensor driver): the Windows
/// install, the board and BIOS, the CPU, the installed memory, and the graphics, disk and network devices present now. Any part added, removed or
/// swapped, a BIOS update or the portable app moved to another PC changes it. Driver versions and drive counters are not in it: those are re-read anyway.
/// </summary>
public static class HardwareFingerprint
{
    // Device setup classes (devguid.h): display adapters, disk drives, network adapters, processors.
    private static readonly string[] Classes = ["{4d36e968-e325-11ce-bfc1-08002be10318}", "{4d36e967-e325-11ce-bfc1-08002be10318}",
        "{4d36e972-e325-11ce-bfc1-08002be10318}", "{50127dc3-0f36-415e-a6cc-4cb3be910b65}"];

    public static string Read()
    {
        var sb = new StringBuilder();
        void Add(string? s) => sb.Append(s).Append('\n');
        Add(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string);
        const string bios = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS";
        foreach (var v in new[] { "SystemManufacturer", "SystemProductName", "BaseBoardManufacturer", "BaseBoardProduct", "BIOSVersion", "BIOSReleaseDate" })
            Add(Registry.GetValue(bios, v, null)?.ToString());
        Add(Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string);
        const string nt = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        Add($"{Registry.GetValue(nt, "CurrentBuild", null)}.{Registry.GetValue(nt, "UBR", null)}");
        Add(GetPhysicallyInstalledSystemMemory(out ulong kb) ? kb.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        foreach (var c in Classes) foreach (var id in PresentDevices(c).Order(StringComparer.OrdinalIgnoreCase)) Add(id);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private const uint FilterClass = 0x200, FilterPresent = 0x100;

    private static IEnumerable<string> PresentDevices(string classGuid)
    {
        if (CM_Get_Device_ID_List_SizeW(out uint len, classGuid, FilterClass | FilterPresent) != 0 || len == 0) return [];
        var buffer = new char[len];
        if (CM_Get_Device_ID_ListW(classGuid, buffer, len, FilterClass | FilterPresent) != 0) return [];
        return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_ID_List_SizeW(out uint len, string filter, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint len, uint flags);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPhysicallyInstalledSystemMemory(out ulong kilobytes);
}
