using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics;

/// <summary>
/// Keeps the computer and its screen awake while a test session runs (a power request, as a video player makes): a queue of long tests with
/// nobody at the keyboard otherwise ends when Windows' idle timer puts the machine to sleep - a run of the RAM tests was found asleep seven
/// hours later, its bit-fade hold counted as 27 031 s. The request belongs to the handle, not to a thread, and ends when it is closed.
/// Where Windows refuses the request the session runs as before.
/// </summary>
internal sealed class KeepAwake : IDisposable
{
    private readonly nint _request;

    public KeepAwake(string reason)
    {
        var context = new ReasonContext { Version = 0, Flags = SimpleString, Reason = reason };
        _request = PowerCreateRequest(ref context);
        if (_request is 0 or -1) { _request = 0; return; }
        PowerSetRequest(_request, SystemRequired); PowerSetRequest(_request, DisplayRequired);
    }

    public void Dispose() { if (_request != 0) CloseHandle(_request); }

    private const uint SimpleString = 1; private const int DisplayRequired = 0, SystemRequired = 1;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ReasonContext { public uint Version, Flags; [MarshalAs(UnmanagedType.LPWStr)] public string Reason; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint PowerCreateRequest(ref ReasonContext context);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PowerSetRequest(nint request, int type);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}
