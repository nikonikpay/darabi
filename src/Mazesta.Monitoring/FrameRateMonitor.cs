using System.Diagnostics; using System.Runtime.InteropServices;
using Mazesta.Core.Overlay; using Microsoft.Extensions.Logging;
namespace Mazesta.Monitoring;

/// <summary>Where the overlay gets the frame rate from.</summary>
public interface IFrameRateSource
{
    /// <summary>Starts watching presents (idempotent). False, with <see cref="Problem"/> set, when Windows refused the trace session.</summary>
    bool Start();
    void Stop();
    /// <summary>The frame rate of the program in front now, or null when it presented nothing recently (or its API is not traced).</summary>
    FrameRateReading? Read();
    /// <summary>Why frame rates cannot be measured, for the log and the overlay page; null while it works.</summary>
    string? Problem { get; }
}

/// <summary>
/// Frame rates from Windows' own event tracing (ETW), with no driver and no injected code: a real-time trace session listens to the DXGI and
/// Direct3D 9 providers' "present" events, which every Direct3D 10/11/12 and 9 program raises once per frame, and counts them per process.
/// The graphics kernel's own Present event (DxgKrnl, "Present" keyword) is counted too, for programs that raise neither - Vulkan and OpenGL
/// presents reach the kernel the same way; on the dev box it fired exactly once per DXGI present (471 = 471, 303 = 303 in five seconds), so it
/// is used only for a process with no API-level presents and never adds to them. Which program is shown is <see cref="FrameTarget"/>'s choice.
/// The session needs administrator rights, which the app has. It runs only while the overlay shows a frame item: <see cref="Stop"/> closes it.
/// </summary>
public sealed class FrameRateMonitor(ILogger log) : IFrameRateSource, IDisposable
{
    private const string SessionName = "Mazesta-FrameRate";
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"), D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9"),
        DxgKrnlProvider = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
    private const ushort DxgiPresentStart = 42, D3D9PresentStart = 1, DxgKrnlPresent = 184;
    private const ulong DxgKrnlPresentKeyword = 0x8000000;
    private const int PropertiesSize = 120, NameBytes = 1024;
    private readonly object _lock = new();
    private readonly Dictionary<int, Queue<double>> _presents = [], _kernelPresents = [];
    private readonly Dictionary<int, string?> _names = [];
    private readonly HashSet<int> _excluded = [];
    private EventRecordCallback? _callback;   // kept alive while the session runs: native code calls it
    private ulong _session, _trace; private IntPtr _props; private Thread? _thread;

    public string? Problem { get; private set; }
    public bool IsRunning => _thread is not null;

    public bool Start()
    {
        lock (_lock)
        {
            if (_thread is not null) return true;
            try
            {
                _props = Marshal.AllocHGlobal(PropertiesSize + NameBytes);
                uint status = StartSession();
                if (status == ErrorAlreadyExists) { ControlTraceW(0, SessionName, Properties(), ControlStop); status = StartSession(); }   // a session left by a crash
                if (status != 0) return Fail($"StartTrace {status}{(status == ErrorAccessDenied ? " (needs administrator)" : "")}");
                foreach (var (provider, keywords) in new[] { (DxgiProvider, 0UL), (D3D9Provider, 0UL), (DxgKrnlProvider, DxgKrnlPresentKeyword) })
                {
                    var g = provider;
                    uint e = EnableTraceEx2(_session, ref g, EnableProvider, 4, keywords, 0, 0, IntPtr.Zero);
                    if (e != 0) log.LogWarning("Frame rate: provider {Provider} not enabled ({Status})", provider, e);
                }
                _excluded.Clear(); _excluded.Add(Environment.ProcessId);
                foreach (var p in Process.GetProcessesByName("dwm")) using (p) _excluded.Add(p.Id);
                _callback = OnEvent;
                var logfile = new EventTraceLogfile { LoggerName = SessionName, ProcessTraceMode = ProcessTraceModeRealTime | ProcessTraceModeEventRecord, EventRecordCallback = Marshal.GetFunctionPointerForDelegate(_callback) };
                _trace = OpenTraceW(ref logfile);
                if (_trace == InvalidHandle) return Fail($"OpenTrace {Marshal.GetLastPInvokeError()}");
                ulong trace = _trace;
                _thread = new Thread(() => { uint r = ProcessTrace([trace], 1, IntPtr.Zero, IntPtr.Zero); if (r != 0 && r != ErrorCancelled) log.LogInformation("Frame rate trace ended: {Status}", r); })
                { IsBackground = true, Name = "Mazesta frame rate" };
                _thread.Start();
                Problem = null;
                log.LogInformation("Frame rate tracing started");
                return true;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException) { return Fail(e.Message); }
        }
    }

    private bool Fail(string why)
    {
        Problem = why; log.LogWarning("Frame rate cannot be measured: {Reason}", why);
        Close();
        return false;
    }

    private uint StartSession() => StartTraceW(out _session, SessionName, Properties());

    /// <summary>EVENT_TRACE_PROPERTIES for a real-time session with the name after it, written at the documented offsets (x64).</summary>
    private IntPtr Properties()
    {
        var p = _props;
        for (int i = 0; i < PropertiesSize + NameBytes; i += 4) Marshal.WriteInt32(p, i, 0);
        Marshal.WriteInt32(p, 0, PropertiesSize + NameBytes);   // Wnode.BufferSize
        Marshal.WriteInt32(p, 40, 2);                           // Wnode.ClientContext: system time
        Marshal.WriteInt32(p, 44, WnodeFlagTracedGuid);         // Wnode.Flags
        Marshal.WriteInt32(p, 48, 64);                          // BufferSize (KB)
        Marshal.WriteInt32(p, 64, EventTraceRealTimeMode);      // LogFileMode
        Marshal.WriteInt32(p, 68, 1);                           // FlushTimer: a second, so frames arrive about a second late at most
        Marshal.WriteInt32(p, 116, PropertiesSize);             // LoggerNameOffset
        return p;
    }

    private void OnEvent(IntPtr record)
    {
        // EVENT_RECORD.EventHeader: ProcessId at 12, TimeStamp at 16, ProviderId at 24, EventDescriptor.Id at 40.
        ushort id = (ushort)Marshal.ReadInt16(record, 40);
        if (id != DxgiPresentStart && id != D3D9PresentStart && id != DxgKrnlPresent) return;
        var provider = Marshal.PtrToStructure<Guid>(record + 24);
        Dictionary<int, Queue<double>> into;
        if ((id == DxgiPresentStart && provider == DxgiProvider) || (id == D3D9PresentStart && provider == D3D9Provider)) into = _presents;
        else if (id == DxgKrnlPresent && provider == DxgKrnlProvider) into = _kernelPresents;
        else return;
        int pid = Marshal.ReadInt32(record, 12);
        if (pid is 0 or 4) return;   // the idle and system processes carry the kernel's own work, never a program's frames
        double t = Marshal.ReadInt64(record, 16) / 1e7;   // FILETIME: 100 ns units
        lock (_lock)
        {
            if (!into.TryGetValue(pid, out var q)) into[pid] = q = new Queue<double>();
            q.Enqueue(t);
            while (q.Count > 0 && t - q.Peek() > FrameTimeStats.LowWindowSeconds + 1) q.Dequeue();
        }
    }

    public FrameRateReading? Read()
    {
        double now = DateTime.UtcNow.ToFileTimeUtc() / 1e7;
        int foreground = ForegroundProcess(out var hwnd);
        Dictionary<int, double[]> frames = [];
        lock (_lock)
        {
            // API-level presents where a process has them; the kernel count only for one that raises none (Vulkan, OpenGL).
            foreach (var (pid, q) in _presents) if (q.Count > 0) frames[pid] = [.. q];
            foreach (var (pid, q) in _kernelPresents) if (q.Count > 0 && !frames.ContainsKey(pid)) frames[pid] = [.. q];
        }
        var lastSecond = frames.ToDictionary(kv => kv.Key, kv => now - kv.Value[^1] > FrameTimeStats.StaleSeconds ? 0 : kv.Value.Count(t => kv.Value[^1] - t <= 1.0));
        Dictionary<int, int>? parents = null;
        int? ParentOf(int pid) { parents ??= ParentProcesses(); return parents.TryGetValue(pid, out var p) ? p : null; }
        if (FrameTarget.Choose(foreground, lastSecond, ParentOf, foreground == 0 ? [] : ChildWindowOwners(hwnd), _excluded) is not { } target) return null;
        // A child process drawing for the window in front (a browser's GPU process) is named after the program in front.
        int named = target.Link == FrameLink.Descendant ? foreground : target.Pid;
        string? name;
        lock (_lock) if (!_names.TryGetValue(named, out name)) _names[named] = name = Name(named);
        return FrameTimeStats.Compute(frames[target.Pid], now, target.Pid, name);
    }

    private static string? Name(int pid) { try { using var p = Process.GetProcessById(pid); return p.ProcessName; } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return null; } }

    private static int ForegroundProcess(out IntPtr hwnd)
    {
        hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        return (int)pid;
    }

    /// <summary>Processes owning a child window of <paramref name="hwnd"/>: a Store game's CoreWindow sits inside ApplicationFrameHost's frame.</summary>
    private static List<int> ChildWindowOwners(IntPtr hwnd)
    {
        var owners = new List<int>();
        EnumChildWindows(hwnd, (child, lParam) => { _ = GetWindowThreadProcessId(child, out uint pid); if (pid != 0 && !owners.Contains((int)pid)) owners.Add((int)pid); return true; }, IntPtr.Zero);
        return owners;
    }

    /// <summary>Every process's parent, from one Toolhelp snapshot; taken only when the program in front does not present itself.</summary>
    private static Dictionary<int, int> ParentProcesses()
    {
        var map = new Dictionary<int, int>();
        IntPtr snap = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snap == new IntPtr(-1)) return map;
        try
        {
            var e = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e)) map[(int)e.ProcessId] = (int)e.ParentProcessId;
        }
        finally { CloseHandle(snap); }
        return map;
    }

    /// <summary>Closes the session; the trace thread ends on its own once Windows stops delivering. It is joined outside the lock, because
    /// its callback takes the lock too.</summary>
    public void Stop()
    {
        Thread? thread; lock (_lock) thread = Close();
        thread?.Join(TimeSpan.FromSeconds(3));
    }

    private Thread? Close()
    {
        if (_session != 0 && _props != IntPtr.Zero) ControlTraceW(_session, null, Properties(), ControlStop);
        _session = 0;
        if (_trace != 0 && _trace != InvalidHandle) CloseTrace(_trace);
        _trace = 0;
        if (_props != IntPtr.Zero) { Marshal.FreeHGlobal(_props); _props = IntPtr.Zero; }
        _presents.Clear(); _kernelPresents.Clear(); _names.Clear();
        var thread = _thread; _thread = null;
        return thread;   // the callback stays referenced: the thread may still call it until it ends
    }

    public void Dispose() => Stop();

    // ——— Windows event tracing ———
    private const uint ErrorAlreadyExists = 183, ErrorAccessDenied = 5, ErrorCancelled = 1223, ControlStop = 1, EnableProvider = 1;
    private const int WnodeFlagTracedGuid = 0x20000, EventTraceRealTimeMode = 0x100;
    private const uint ProcessTraceModeRealTime = 0x100, ProcessTraceModeEventRecord = 0x10000000;
    private const ulong InvalidHandle = ulong.MaxValue;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void EventRecordCallback(IntPtr record);

    [StructLayout(LayoutKind.Sequential)] private struct EventTraceHeader { public ushort Size; public ushort FieldTypeFlags; public uint Version; public uint ThreadId; public uint ProcessId; public long TimeStamp; public Guid Guid; public uint KernelTime; public uint UserTime; }
    [StructLayout(LayoutKind.Sequential)] private struct EventTrace { public EventTraceHeader Header; public uint InstanceId; public uint ParentInstanceId; public Guid ParentGuid; public IntPtr MofData; public uint MofLength; public uint ClientContext; }
    [StructLayout(LayoutKind.Sequential)] private struct SystemTime { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TimeZoneInformation
    {
        public int Bias; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string StandardName; public SystemTime StandardDate; public int StandardBias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DaylightName; public SystemTime DaylightDate; public int DaylightBias;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TraceLogfileHeader
    {
        public uint BufferSize, Version, ProviderVersion, NumberOfProcessors; public long EndTime; public uint TimerResolution, MaximumFileSize, LogFileMode, BuffersWritten;
        public Guid LogInstanceGuid; public IntPtr LoggerName, LogFileName; public TimeZoneInformation TimeZone; public long BootTime, PerfFreq, StartTime; public uint ReservedFlags, BuffersLost;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct EventTraceLogfile
    {
        public string? LogFileName; public string LoggerName; public long CurrentTime; public uint BuffersRead; public uint ProcessTraceMode; public EventTrace CurrentEvent;
        public TraceLogfileHeader LogfileHeader; public IntPtr BufferCallback; public uint BufferSize, Filled, EventsLost; public IntPtr EventRecordCallback; public uint IsKernelTrace; public IntPtr Context;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern uint StartTraceW(out ulong handle, string name, IntPtr properties);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern uint ControlTraceW(ulong handle, string? name, IntPtr properties, uint code);
    [DllImport("advapi32.dll")] private static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint controlCode, byte level, ulong matchAny, ulong matchAll, uint timeout, IntPtr parameters);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ulong OpenTraceW(ref EventTraceLogfile logfile);
    [DllImport("advapi32.dll")] private static extern uint ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
    [DllImport("advapi32.dll")] private static extern uint CloseTrace(ulong handle);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
    private const uint SnapProcess = 0x2;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ProcessEntry32
    {
        public uint Size, Usage, ProcessId; public IntPtr DefaultHeapId; public uint ModuleId, Threads, ParentProcessId; public int PriorityClassBase; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
