using System.Runtime.InteropServices; using Mazesta.Core.Tuning; using Microsoft.Extensions.Logging;
namespace Mazesta.Hardware.Amd;

/// <summary>
/// AMD's ADLX (amdadlx64.dll, installed with Adrenalin) called through its interface tables: every ADLX object is a pointer to a table of function
/// pointers, in the order of AMD's headers (SDK/Include). Slot numbers below are those orders. ADL's older Overdrive8 calls answer "not supported"
/// to every write on a Radeon RX 9000, ADLX is what Adrenalin itself uses; both were tried on an RX 9060 XT. Initialised once and never closed,
/// as NVML is.
/// </summary>
internal static unsafe class Adlx
{
    [DllImport("amdadlx64.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int ADLXInitialize(ulong version, out IntPtr system);
    /// <summary>1.4: the oldest interface set that has the manual graphics, VRAM and power tuning used here; a newer driver answers it too.</summary>
    private const ulong Version = (1UL << 48) | (4UL << 32);
    public const int Ok = 0, AlreadyEnabled = 1, AlreadyInitialized = 2, NotSupported = 12;
    public static bool Succeeded(int r) => r is Ok or AlreadyEnabled or AlreadyInitialized;

    private static IntPtr Fn(IntPtr o, int slot) => *(IntPtr*)(*(IntPtr*)o + slot * IntPtr.Size);
    public static int Init(out IntPtr system) => ADLXInitialize(Version, out system);
    /// <summary>A call that returns one object pointer: <c>f(this, T**)</c>.</summary>
    public static int Obj(IntPtr o, int slot, out IntPtr r) { IntPtr x = 0; int rc = ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, int>)Fn(o, slot))(o, &x); r = x; return rc; }
    public static int Obj(IntPtr o, int slot, uint index, out IntPtr r) { IntPtr x = 0; int rc = ((delegate* unmanaged[Cdecl]<IntPtr, uint, IntPtr*, int>)Fn(o, slot))(o, index, &x); r = x; return rc; }
    public static int Obj(IntPtr o, int slot, IntPtr arg, out IntPtr r) { IntPtr x = 0; int rc = ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr*, int>)Fn(o, slot))(o, arg, &x); r = x; return rc; }
    public static int Flag(IntPtr o, int slot, IntPtr arg, out bool r) { byte x = 0; int rc = ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte*, int>)Fn(o, slot))(o, arg, &x); r = x != 0; return rc; }
    public static int Get(IntPtr o, int slot, out int r) { int x = 0; int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int*, int>)Fn(o, slot))(o, &x); r = x; return rc; }
    public static int Set(IntPtr o, int slot, int value) => ((delegate* unmanaged[Cdecl]<IntPtr, int, int>)Fn(o, slot))(o, value);
    /// <summary>ADLX_IntRange: minimum, maximum, step.</summary>
    public static int Range(IntPtr o, int slot, out int min, out int max) { int* r = stackalloc int[3]; int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int*, int>)Fn(o, slot))(o, r); min = r[0]; max = r[1]; return rc; }
    /// <summary>Asks an object for another of its interfaces by name; the object keeps its own reference.</summary>
    public static int Query(IntPtr o, string interfaceId, out IntPtr r)
    {
        IntPtr id = Marshal.StringToHGlobalUni(interfaceId), x = 0;
        try { int rc = ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr*, int>)Fn(o, 2))(o, id, &x); r = x; return rc; }
        finally { Marshal.FreeHGlobal(id); }
    }
    public static string? Text(IntPtr o, int slot) => Obj(o, slot, out var p) == Ok && p != 0 ? Marshal.PtrToStringAnsi(p) : null;

    // IADLXSystem has no Acquire/Release: 1 GetGPUs, 8 GetGPUTuningServices. IADLXGPUList: 11 At_GPUList. IADLXGPU: 7 Name, 9 PNPString.
    // IADLXGPUTuningServices: 8 and 9 IsSupportedManualGFX/VRAMTuning, 14 and 15 GetManualGFX/VRAMTuning.
    // IADLXManualGraphicsTuning2: 6 GetGPUMaxFrequencyRange, 7 Get, 8 Set, 9 GetGPUVoltageRange, 10 Get, 11 Set.
    // IADLXManualVRAMTuning2: 7 GetMaxVRAMFrequencyRange, 8 Get, 9 Set.
    public const int SysGetGpus = 1, SysTuningServices = 8, ListAt = 11, GpuName = 7, GpuPnp = 9;
    public const int SvcGfx = 14, SvcVram = 15, SvcSupportedGfx = 8, SvcSupportedVram = 9;
    public const int GfxMaxRange = 6, GfxMaxGet = 7, GfxMaxSet = 8, GfxVoltRange = 9, GfxVoltGet = 10, GfxVoltSet = 11;
    public const int VramRange = 7, VramGet = 8, VramSet = 9;
}

/// <summary>ADL's performance log (a call that fills every sensor at once): the card's clocks, temperature, power and fan while it is tuned. Telemetry only; nothing is written through ADL.</summary>
internal sealed unsafe class AdlLog
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Alloc(int size);
    private static readonly Alloc Allocator = size => Marshal.AllocCoTaskMem(size);
    [DllImport("atiadlxx.dll")] private static extern int ADL2_Main_Control_Create(Alloc alloc, int enumConnected, out IntPtr context);
    [DllImport("atiadlxx.dll")] private static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int count);
    [DllImport("atiadlxx.dll")] private static extern int ADL2_Adapter_Active_Get(IntPtr context, int index, out int active);
    [DllImport("atiadlxx.dll")] private static extern int ADL2_New_QueryPMLogData_Get(IntPtr context, int index, IntPtr output);
    private const int Sensors = 256, GfxClk = 1, MemClk = 2, TempEdge = 8, FanPercent = 15, AsicPower = 23, BoardPower = 73;

    private IntPtr _ctx, _buffer; private int _index = -1;

    /// <summary>Opens ADL and finds the first active adapter that answers the log. A machine without ADL simply has no telemetry.</summary>
    public static AdlLog? Open()
    {
        try
        {
            var log = new AdlLog();
            if (ADL2_Main_Control_Create(Allocator, 1, out log._ctx) != 0 || ADL2_Adapter_NumberOfAdapters_Get(log._ctx, out int n) != 0) return null;
            log._buffer = Marshal.AllocHGlobal(4 + Sensors * 8);
            for (int i = 0; i < n && log._index < 0; i++)
                if (ADL2_Adapter_Active_Get(log._ctx, i, out int active) == 0 && active != 0 && log.Query(i)) log._index = i;
            return log._index >= 0 ? log : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return null; }
    }

    private bool Query(int index)
    {
        new Span<byte>((void*)_buffer, 4 + Sensors * 8).Clear();
        return ADL2_New_QueryPMLogData_Get(_ctx, index, _buffer) == 0 && Value(GfxClk) is not null;
    }

    private double? Value(int sensor)
    {
        int* p = (int*)_buffer + 1 + sensor * 2;   // {supported, value} pairs after the size
        return p[0] != 0 ? p[1] : null;
    }

    public GpuTelemetry Read()
    {
        if (!Query(_index)) return new(DateTimeOffset.UtcNow, null, null, null, null, null);
        return new(DateTimeOffset.UtcNow, Value(GfxClk), Value(MemClk), Value(TempEdge), Value(BoardPower) ?? Value(AsicPower), Value(FanPercent));
    }
}

/// <summary>Lists the AMD cards ADLX can tune. Only the first is offered: the log's adapter is picked the same way, and a machine with two Radeons is not one this has been tried on.</summary>
internal static class AdlxTuningDevices
{
    public static (IReadOnlyList<IGpuTuningDevice> Devices, string? Detail) Open(ILogger? log)
    {
        try
        {
            if (!Adlx.Succeeded(Adlx.Init(out var system))) return ([], "ADLX did not start");
            if (Adlx.Obj(system, Adlx.SysGetGpus, out var list) != Adlx.Ok || Adlx.Obj(list, Adlx.ListAt, 0u, out var gpu) != Adlx.Ok) return ([], "ADLX lists no graphics card");
            if (Adlx.Obj(system, Adlx.SysTuningServices, out var services) != Adlx.Ok) return ([], "ADLX has no tuning services");
            var device = AmdTuningDevice.TryCreate(services, gpu, log);
            return device is null ? ([], "the card's driver offers no manual tuning") : ([device], null);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return ([], e.Message); }
    }
}

/// <summary>
/// One Radeon through ADLX's manual tuning: the maximum graphics clock as an offset in MHz (<see cref="GpuTuningSettings.CoreOffsetMHz"/>), the
/// maximum memory clock as MHz above the stock one, and the graphics voltage as an offset in millivolts (never above 0). That voltage offset is how an
/// undervolt is made here, at the clock the card already reaches. A clock cap, a power limit and fan control are not offered. The driver drops all
/// of it on a reboot or a driver reset, like NVIDIA's.
/// </summary>
internal sealed class AmdTuningDevice : IGpuTuningDevice
{
    private readonly IntPtr _gfx, _vram; private readonly int _vramStock; private readonly AdlLog? _log;
    public string Id { get; }
    public string Name { get; }
    public GpuTuningLimits Limits { get; }

    private AmdTuningDevice(IntPtr gfx, IntPtr vram, string id, string name, GpuTuningLimits limits, int vramStock, AdlLog? log)
    { _gfx = gfx; _vram = vram; Id = id; Name = name; Limits = limits; _vramStock = vramStock; _log = log; }

    public static AmdTuningDevice? TryCreate(IntPtr services, IntPtr gpu, ILogger? log)
    {
        if (Adlx.Flag(services, Adlx.SvcSupportedGfx, gpu, out bool gfxOk) != Adlx.Ok || !gfxOk) return null;
        if (Adlx.Obj(services, Adlx.SvcGfx, gpu, out var gfx0) != Adlx.Ok || Adlx.Query(gfx0, "IADLXManualGraphicsTuning2", out var gfx) != Adlx.Ok) return null;
        if (Adlx.Range(gfx, Adlx.GfxMaxRange, out int coreMin, out int coreMax) != Adlx.Ok) return null;
        int voltMin = Adlx.Range(gfx, Adlx.GfxVoltRange, out int vMin, out int vMax) == Adlx.Ok && vMax <= 0 ? vMin : 0;
        // The memory clock's range starts at the stock clock: an offset is the distance above it.
        IntPtr vram = 0; int stock = 0, memMax = 0;
        if (Adlx.Flag(services, Adlx.SvcSupportedVram, gpu, out bool vramOk) == Adlx.Ok && vramOk && Adlx.Obj(services, Adlx.SvcVram, gpu, out var vram0) == Adlx.Ok
            && Adlx.Query(vram0, "IADLXManualVRAMTuning2", out var vram2) == Adlx.Ok && Adlx.Range(vram2, Adlx.VramRange, out stock, out memMax) == Adlx.Ok) vram = vram2;
        string name = Adlx.Text(gpu, Adlx.GpuName) ?? "AMD Radeon", pnp = Adlx.Text(gpu, Adlx.GpuPnp) ?? name;
        var limits = new GpuTuningLimits(coreMin, coreMax, 0, vram == 0 ? 0 : memMax - stock, null, null, null, null, 0, null, null, voltMin);
        var device = new AmdTuningDevice(gfx, vram, "amd:" + pnp, name, limits, stock, AdlLog.Open());
        log?.LogInformation("GPU tuning: {Name} {Id} limits {Limits}", name, device.Id, limits);
        return device;
    }

    public GpuTelemetry ReadTelemetry() => _log?.Read() ?? new(DateTimeOffset.UtcNow, null, null, null, null, null);

    public GpuTuningSettings ReadCurrent()
    {
        int core = Adlx.Get(_gfx, Adlx.GfxMaxGet, out int c) == Adlx.Ok ? c : 0;
        int volt = Limits.HasVoltage && Adlx.Get(_gfx, Adlx.GfxVoltGet, out int v) == Adlx.Ok ? v : 0;
        int memory = _vram != 0 && Adlx.Get(_vram, Adlx.VramGet, out int m) == Adlx.Ok ? m - _vramStock : 0;
        return new(core, memory, null, null, null, volt);
    }

    public TuningApplyResult Apply(GpuTuningSettings s)
    {
        var steps = new List<TuningStepResult>();
        // What this card has no control for is said so, not skipped: the profile asked for something it will not get.
        if (s.MaxClockMHz is not null) steps.Add(new("Tuning_MaxClock", false, "not offered for AMD cards", Unsupported: true));
        if (s.PowerLimitW is not null) steps.Add(new("Tuning_PowerLimit", false, "not offered for AMD cards", Unsupported: true));
        if (s.FanPercent is not null) steps.Add(new("Tuning_Fan", false, "not offered for AMD cards", Unsupported: true));
        // Order: the clock comes down before the voltage does, and the voltage comes up before the clock goes up, so the card never runs a clock on too little voltage.
        bool lowering = s.CoreOffsetMHz < ReadCurrent().CoreOffsetMHz;
        if (lowering) steps.Add(Step("Tuning_CoreOffset", _gfx, Adlx.GfxMaxSet, s.CoreOffsetMHz));
        if (Limits.HasVoltage) steps.Add(Step("Tuning_Voltage", _gfx, Adlx.GfxVoltSet, s.VoltageOffsetMv));
        if (!lowering) steps.Add(Step("Tuning_CoreOffset", _gfx, Adlx.GfxMaxSet, s.CoreOffsetMHz));
        if (_vram != 0) steps.Add(Step("Tuning_MemoryOffset", _vram, Adlx.VramSet, _vramStock + s.MemoryOffsetMHz));
        return new(steps);
    }

    /// <summary>Every part back to the driver's own value; a part the card does not have cannot have been changed.</summary>
    public TuningApplyResult Reset() => new([.. Apply(GpuTuningSettings.Stock).Steps.Where(s => s.Ok || !s.Unsupported)]);

    private static TuningStepResult Step(string key, IntPtr target, int slot, int value)
    {
        int rc = Adlx.Set(target, slot, value);
        return new(key, Adlx.Succeeded(rc), Adlx.Succeeded(rc) ? null : $"ADLX {rc} ({Describe(rc)})", rc == Adlx.NotSupported);
    }

    private static string Describe(int rc) => rc switch
    {
        3 => "failed", 4 => "invalid value", 5 => "wrong version", 12 => "not supported", 14 => "the card is inactive", 15 => "the card is in use by a program", 17 => "not active",
        18 => "needs a reset to the factory settings", _ => "refused",
    };
}
