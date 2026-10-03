using System.ComponentModel; using System.Runtime.InteropServices;
using Mazesta.Core.Gaming;
namespace Mazesta.Diagnostics.Windows;

/// <summary>The service control manager through its own API (advapi32): no output of a command-line tool to parse, and Windows' own error for
/// a change it refuses. Stopping waits for the service to report that it stopped, up to <see cref="StopWait"/>.</summary>
public sealed class WindowsServiceControl : IServiceControl
{
    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(20);
    private const uint ManagerConnect = 0x0001, QueryConfig = 0x0001, ChangeConfig = 0x0002, QueryStatus = 0x0004, StartRight = 0x0010, StopRight = 0x0020, NoChange = 0xFFFFFFFF, ControlStop = 1;
    private const int Stopped = 1, StartPending = 2, Running = 4, DoesNotExist = 1060, AlreadyRunning = 1056, NotActive = 1062, InsufficientBuffer = 122;

    public ServiceInfo? Query(string name) => With(name, QueryConfig | QueryStatus, out int error, service =>
    {
        if (!QueryServiceStatus(service, out var status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        QueryServiceConfig(service, IntPtr.Zero, 0, out uint needed);
        if (Marshal.GetLastWin32Error() != InsufficientBuffer) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!QueryServiceConfig(service, buffer, needed, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var config = Marshal.PtrToStructure<ServiceConfig>(buffer);
            return new ServiceInfo(name, Marshal.PtrToStringUni(config.DisplayName) ?? name, (int)config.StartType, status.CurrentState is Running or StartPending);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }) is { } info ? info : error == DoesNotExist ? null : throw new Win32Exception(error);

    public string? SetStartType(string name, int startType) => Change(name, ChangeConfig, service
        => ChangeServiceConfig(service, NoChange, (uint)startType, NoChange, null, null, IntPtr.Zero, null, null, null, null) ? 0 : Marshal.GetLastWin32Error());

    public string? Stop(string name) => Change(name, StopRight | QueryStatus, service =>
    {
        if (!ControlService(service, ControlStop, out var status)) { int e = Marshal.GetLastWin32Error(); if (e != NotActive) return e; }
        var until = DateTime.UtcNow + StopWait;
        while (QueryServiceStatus(service, out status) && status.CurrentState != Stopped && DateTime.UtcNow < until) Thread.Sleep(250);
        return status.CurrentState == Stopped ? 0 : 1053;   // ERROR_SERVICE_REQUEST_TIMEOUT: it did not answer in time
    });

    public string? Start(string name) => Change(name, StartRight, service =>
    {
        if (StartService(service, 0, IntPtr.Zero)) return 0;
        int e = Marshal.GetLastWin32Error(); return e == AlreadyRunning ? 0 : e;
    });

    private static string? Change(string name, uint rights, Func<IntPtr, int> work)
    {
        int code = With(name, rights, out int open, service => (int?)work(service)) ?? open;
        return code == 0 ? null : new Win32Exception(code).Message;
    }

    /// <summary>Runs <paramref name="work"/> on the opened service; default with <paramref name="error"/> set when it could not be opened.</summary>
    private static T? With<T>(string name, uint rights, out int error, Func<IntPtr, T> work)
    {
        error = 0;
        IntPtr manager = OpenSCManager(null, null, ManagerConnect);
        if (manager == IntPtr.Zero) { error = Marshal.GetLastWin32Error(); return default; }
        try
        {
            IntPtr service = OpenService(manager, name, rights);
            if (service == IntPtr.Zero) { error = Marshal.GetLastWin32Error(); return default; }
            try { return work(service); }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus { public uint ServiceType; public int CurrentState; public uint ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint; }
    [StructLayout(LayoutKind.Sequential)] private struct ServiceConfig { public uint ServiceType, StartType, ErrorControl; public IntPtr BinaryPathName, LoadOrderGroup; public uint TagId; public IntPtr Dependencies, ServiceStartName, DisplayName; }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool CloseServiceHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryServiceConfigW")] private static extern bool QueryServiceConfig(IntPtr service, IntPtr config, uint size, out uint needed);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "ChangeServiceConfigW")]
    private static extern bool ChangeServiceConfig(IntPtr service, uint serviceType, uint startType, uint errorControl, string? binaryPath, string? loadOrderGroup, IntPtr tagId, string? dependencies, string? startName, string? password, string? displayName);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool ControlService(IntPtr service, uint control, out ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "StartServiceW")] private static extern bool StartService(IntPtr service, uint argc, IntPtr argv);
}
