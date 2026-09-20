using System.Runtime;
using System.Runtime.InteropServices;
namespace Mazesta.Tray;

/// <summary>After a check the provider is gone, but its garbage and the pages it touched would stay resident for the next ten minutes; hand them back so the idle footprint is the icon's, not the poll's.</summary>
internal static class Memory
{
    [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    public static void Release()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true); GC.WaitForPendingFinalizers(); GC.Collect();
        SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
    }
}
