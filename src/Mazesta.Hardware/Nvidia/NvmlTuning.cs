using Mazesta.Core.Tuning; using Microsoft.Extensions.Logging;
namespace Mazesta.Hardware.Nvidia;

/// <summary>Opens NVML once, on first use, and lists the NVIDIA GPUs. NVML is thread-safe and reference-counted, so sharing it with
/// LibreHardwareMonitor's own NVML use in the same process is fine; it is never shut down while the app runs.</summary>
public sealed class NvmlTuningProvider(ILogger log) : IGpuTuningProvider
{
    private readonly Lazy<(IReadOnlyList<IGpuTuningDevice> Devices, string? Reason, string? Detail)> _state = new(() => Open(log));
    public IReadOnlyList<IGpuTuningDevice> Devices => _state.Value.Devices;
    public string? UnavailableReasonKey => _state.Value.Reason;
    public string? UnavailableDetail => _state.Value.Detail;

    private static (IReadOnlyList<IGpuTuningDevice>, string?, string?) Open(ILogger log)
    {
        int init;
        try { init = Nvml.nvmlInit_v2(); }
        catch (DllNotFoundException) { return ([], "Tuning_Unavailable_NoNvidia", null); }
        if (init != Nvml.Success) { log.LogWarning("NVML init failed: {Error}", Nvml.Describe(init)); return ([], "Tuning_Unavailable_Nvml", Nvml.Describe(init)); }
        if (Nvml.nvmlDeviceGetCount_v2(out uint count) != Nvml.Success || count == 0) return ([], "Tuning_Unavailable_NoNvidia", null);
        var devices = new List<IGpuTuningDevice>();
        for (uint i = 0; i < count; i++)
            if (Nvml.nvmlDeviceGetHandleByIndex_v2(i, out var handle) == Nvml.Success) devices.Add(new NvmlTuningDevice(handle));
        foreach (var d in devices) log.LogInformation("GPU tuning: {Name} {Id} limits {Limits}", d.Name, d.Id, d.Limits);
        return (devices, null, null);
    }
}

/// <summary>
/// One NVIDIA GPU through NVML. Clock offsets are written to every performance state the card has, with the same value: NVML applies the most
/// restrictive offset across states, so an offset set on P0 alone could be silently overruled by another state's. All settings need
/// administrator rights (the app runs elevated) and are dropped by the driver on a reboot or driver reset.
/// </summary>
internal sealed class NvmlTuningDevice : IGpuTuningDevice
{
    private readonly IntPtr _h; private readonly int[] _pstates;
    public string Id { get; }
    public string Name { get; }
    public GpuTuningLimits Limits { get; }

    public NvmlTuningDevice(IntPtr handle)
    {
        _h = handle;
        Name = Nvml.Text(b => Nvml.nvmlDeviceGetName(_h, b, (uint)b.Length)) ?? "NVIDIA GPU";
        Id = Nvml.Text(b => Nvml.nvmlDeviceGetUUID(_h, b, (uint)b.Length)) ?? Name;
        var states = new int[16];
        _pstates = Nvml.Call(() => Nvml.nvmlDeviceGetSupportedPerformanceStates(_h, states, (uint)(states.Length * sizeof(int)))) == Nvml.Success
            ? [.. states.Where(s => s is >= 0 and < Nvml.PstateUnknown).Distinct()] : [0];
        if (_pstates.Length == 0) _pstates = [0];
        var core = Offset(Nvml.ClockGraphics); var memory = Offset(Nvml.ClockMemory);
        uint maxMHz = 0, pmin = 0, pmax = 0, pdef = 0, fans = 0, fmin = 0, fmax = 0;
        int? maxClock = Nvml.Call(() => Nvml.nvmlDeviceGetMaxClockInfo(_h, Nvml.ClockGraphics, out maxMHz)) == Nvml.Success && maxMHz > 0 ? (int)maxMHz : null;
        bool power = Nvml.Call(() => Nvml.nvmlDeviceGetPowerManagementLimitConstraints(_h, out pmin, out pmax)) == Nvml.Success;
        bool powerDefault = Nvml.Call(() => Nvml.nvmlDeviceGetPowerManagementDefaultLimit(_h, out pdef)) == Nvml.Success;
        if (Nvml.Call(() => Nvml.nvmlDeviceGetNumFans(_h, out fans)) != Nvml.Success) fans = 0;
        bool fanRange = fans > 0 && Nvml.Call(() => Nvml.nvmlDeviceGetMinMaxFanSpeed(_h, out fmin, out fmax)) == Nvml.Success;
        Limits = new(core?.MinOffsetMHz ?? 0, core?.MaxOffsetMHz ?? 0, memory?.MinOffsetMHz ?? 0, memory?.MaxOffsetMHz ?? 0, maxClock,
            power ? (int)(pmin / 1000) : null, power ? (int)(pmax / 1000) : null, powerDefault ? (int)(pdef / 1000) : null,
            (int)fans, fanRange ? (int)fmin : null, fanRange ? (int)fmax : null);
    }

    /// <summary>The offset of the highest performance state (P0 when the card has it), with the range the driver allows.</summary>
    private Nvml.ClockOffset? Offset(int type)
    {
        var info = new Nvml.ClockOffset { Version = Nvml.ClockOffsetVersion, Type = type, Pstate = _pstates.Min() };
        return Nvml.Call(() => Nvml.nvmlDeviceGetClockOffsets(_h, ref info)) == Nvml.Success ? info : null;
    }

    public GpuTelemetry ReadTelemetry()
    {
        uint core = 0, memory = 0, temp = 0, mw = 0, fan = 0;
        return new(DateTimeOffset.UtcNow,
            Nvml.Call(() => Nvml.nvmlDeviceGetClockInfo(_h, Nvml.ClockGraphics, out core)) == Nvml.Success ? core : null,
            Nvml.Call(() => Nvml.nvmlDeviceGetClockInfo(_h, Nvml.ClockMemory, out memory)) == Nvml.Success ? memory : null,
            Nvml.Call(() => Nvml.nvmlDeviceGetTemperature(_h, Nvml.TemperatureGpu, out temp)) == Nvml.Success ? temp : null,
            Nvml.Call(() => Nvml.nvmlDeviceGetPowerUsage(_h, out mw)) == Nvml.Success ? mw / 1000.0 : null,
            Limits.FanCount > 0 && Nvml.Call(() => Nvml.nvmlDeviceGetFanSpeed_v2(_h, 0, out fan)) == Nvml.Success ? fan : null);
    }

    public GpuTuningSettings ReadCurrent()
    {
        uint limit = 0, policy = 0, fan = 0;
        int? powerW = Limits.PowerLimitDefaultW is { } def && Nvml.Call(() => Nvml.nvmlDeviceGetPowerManagementLimit(_h, out limit)) == Nvml.Success && (int)(limit / 1000) != def ? (int)(limit / 1000) : null;
        int? fanPercent = Limits.FanCount > 0 && Nvml.Call(() => Nvml.nvmlDeviceGetFanControlPolicy_v2(_h, 0, out policy)) == Nvml.Success && policy == Nvml.FanPolicyManual
            && Nvml.Call(() => Nvml.nvmlDeviceGetFanSpeed_v2(_h, 0, out fan)) == Nvml.Success ? (int)fan : null;
        return new(Offset(Nvml.ClockGraphics)?.OffsetMHz ?? 0, Offset(Nvml.ClockMemory)?.OffsetMHz ?? 0, null, powerW, fanPercent);
    }

    public TuningApplyResult Apply(GpuTuningSettings s) => Apply(s, reset: false);

    /// <summary>Everything back to the driver's defaults, each part sent whether or not this process changed it: after a crash the new process
    /// cannot know what the old one left behind. A part the driver does not support cannot have been changed, so its refusal is not a failure here.</summary>
    public TuningApplyResult Reset() => new([.. Apply(GpuTuningSettings.Stock, reset: true).Steps.Where(s => s.Ok || !s.Unsupported)]);

    private bool _locked;

    /// <summary>Only the parts that differ from the driver default, or that this process changed, are sent: a card that refuses one control
    /// (a laptop GPU without fan control, say) must not make every apply look failed.</summary>
    private TuningApplyResult Apply(GpuTuningSettings s, bool reset)
    {
        var current = reset ? null : ReadCurrent();
        var steps = new List<TuningStepResult>();
        if (Limits.PowerLimitDefaultW is { } def && (reset || s.PowerLimitW is not null || current!.PowerLimitW is not null))
            steps.Add(Step("Tuning_PowerLimit", () => Nvml.nvmlDeviceSetPowerManagementLimit(_h, (uint)((s.PowerLimitW ?? def) * 1000))));
        // The cap goes on before a raised offset takes effect, so the card never runs the new curve up to its old maximum clock in between.
        if (reset || s.MaxClockMHz is not null || _locked)
        {
            var lockStep = Step("Tuning_MaxClock", () => s.MaxClockMHz is { } cap ? Nvml.nvmlDeviceSetGpuLockedClocks(_h, 0, (uint)cap) : Nvml.nvmlDeviceResetGpuLockedClocks(_h));
            if (lockStep.Ok) _locked = s.MaxClockMHz is not null;
            steps.Add(lockStep);
        }
        if (reset || s.CoreOffsetMHz != current!.CoreOffsetMHz) steps.Add(Step("Tuning_CoreOffset", () => SetOffset(Nvml.ClockGraphics, s.CoreOffsetMHz)));
        if (reset || s.MemoryOffsetMHz != current!.MemoryOffsetMHz) steps.Add(Step("Tuning_MemoryOffset", () => SetOffset(Nvml.ClockMemory, s.MemoryOffsetMHz)));
        if (reset || s.FanPercent is not null || current!.FanPercent is not null)
            for (uint f = 0; f < Limits.FanCount; f++)
            {
                uint fan = f;
                steps.Add(Step("Tuning_Fan", () => s.FanPercent is { } pct ? Nvml.nvmlDeviceSetFanSpeed_v2(_h, fan, (uint)pct) : Nvml.nvmlDeviceSetDefaultFanSpeed_v2(_h, fan)));
            }
        return new(steps);
    }

    private int SetOffset(int type, int mhz)
    {
        foreach (int pstate in _pstates)
        {
            var info = new Nvml.ClockOffset { Version = Nvml.ClockOffsetVersion, Type = type, Pstate = pstate, OffsetMHz = mhz };
            int rc = Nvml.nvmlDeviceSetClockOffsets(_h, ref info);
            if (rc != Nvml.Success) return rc;
        }
        return Nvml.Success;
    }

    private static TuningStepResult Step(string key, Func<int> call)
    {
        int rc = Nvml.Call(call);
        return new(key, rc == Nvml.Success, rc == Nvml.Success ? null : Nvml.Describe(rc), rc is Nvml.NotSupported or Nvml.FunctionMissing);
    }
}
