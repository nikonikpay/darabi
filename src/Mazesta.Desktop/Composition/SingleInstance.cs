using System.Runtime.InteropServices;
namespace Mazesta.Desktop.Composition;
// Note: the brief specifies LibraryImport (source-generated P/Invoke) for these three signatures,
// but on this SDK the generator reports SYSLIB1062 ("LibraryImportAttribute requires unsafe code")
// for a bool return marshalled via MarshalAs, which would force <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
// project-wide. The brief allows falling back to DllImport in that case, which needs no unsafe code.
internal static class SingleInstance
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    public static void ActivateExisting(string title) { var h = FindWindow(null, title); if (h != IntPtr.Zero) { ShowWindow(h, 9 /* SW_RESTORE */); SetForegroundWindow(h); } }
}
