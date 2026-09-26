namespace Mazesta.Core.Tuning;

/// <summary>
/// What the app asks the GPU driver to run at. Every field is relative to the card's own factory behaviour: a zero offset, no clock lock,
/// the default power limit and automatic fan are "stock". <see cref="MaxClockMHz"/> caps the core clock; together with a positive
/// <see cref="CoreOffsetMHz"/> it is how an undervolt is expressed through NVIDIA's public interface - the offset shifts the whole
/// voltage/frequency curve up, the cap keeps the clock where it was, so the card reaches that clock at a lower voltage.
/// </summary>
public sealed record GpuTuningSettings(int CoreOffsetMHz, int MemoryOffsetMHz, int? MaxClockMHz, int? PowerLimitW, int? FanPercent)
{
    public static readonly GpuTuningSettings Stock = new(0, 0, null, null, null);
    public bool IsStock => this == Stock;
}

/// <summary>The ranges the driver reports for this card. Null means the driver did not report it, and that control is not offered.</summary>
public sealed record GpuTuningLimits(int CoreOffsetMin, int CoreOffsetMax, int MemoryOffsetMin, int MemoryOffsetMax, int? MaxClockMHz,
    int? PowerLimitMinW, int? PowerLimitMaxW, int? PowerLimitDefaultW, int FanCount, int? FanMinPercent, int? FanMaxPercent)
{
    public bool HasCoreOffset => CoreOffsetMax > CoreOffsetMin;
    public bool HasMemoryOffset => MemoryOffsetMax > MemoryOffsetMin;
    public bool HasPowerLimit => PowerLimitMinW is not null && PowerLimitMaxW is not null && PowerLimitMaxW > PowerLimitMinW;
    public bool HasFanControl => FanCount > 0 && FanMinPercent is not null && FanMaxPercent is not null;

    /// <summary>The reasons <paramref name="s"/> cannot be sent to this card, as resource keys with their argument; empty when it can.
    /// Values outside the driver's range are refused rather than clamped, so the technician never runs something other than what they typed.</summary>
    public IReadOnlyList<(string Key, string Range)> Check(GpuTuningSettings s)
    {
        var problems = new List<(string, string)>();
        if (s.CoreOffsetMHz != 0 && (!HasCoreOffset || s.CoreOffsetMHz < CoreOffsetMin || s.CoreOffsetMHz > CoreOffsetMax)) problems.Add(("Tuning_Bad_CoreOffset", $"{CoreOffsetMin}..{CoreOffsetMax}"));
        if (s.MemoryOffsetMHz != 0 && (!HasMemoryOffset || s.MemoryOffsetMHz < MemoryOffsetMin || s.MemoryOffsetMHz > MemoryOffsetMax)) problems.Add(("Tuning_Bad_MemoryOffset", $"{MemoryOffsetMin}..{MemoryOffsetMax}"));
        if (s.MaxClockMHz is { } clock && (clock < MinLockMHz || clock > MaxLockMHz)) problems.Add(("Tuning_Bad_MaxClock", $"{MinLockMHz}..{MaxLockMHz}"));
        if (s.PowerLimitW is { } w && (!HasPowerLimit || w < PowerLimitMinW || w > PowerLimitMaxW)) problems.Add(("Tuning_Bad_PowerLimit", $"{PowerLimitMinW}..{PowerLimitMaxW}"));
        if (s.FanPercent is { } f && (!HasFanControl || f < FanMinPercent || f > FanMaxPercent)) problems.Add(("Tuning_Bad_Fan", $"{FanMinPercent}..{FanMaxPercent}"));
        return problems;
    }

    /// <summary>A clock cap below a few hundred MHz would leave the desktop crawling; one far above the card's own maximum means nothing.</summary>
    public const int MinLockMHz = 300;
    public int MaxLockMHz => (MaxClockMHz ?? 3000) + Math.Max(0, CoreOffsetMax);
}

/// <summary>One reading of the card while it is being tuned. Null is "the driver did not report it", never zero.</summary>
public readonly record struct GpuTelemetry(DateTimeOffset At, double? CoreClockMHz, double? MemoryClockMHz, double? TemperatureC, double? PowerW, double? FanPercent);

/// <summary>What the driver said to each part of an apply; <see cref="Ok"/> only when every part was accepted.</summary>
public sealed record TuningApplyResult(IReadOnlyList<TuningStepResult> Steps)
{
    public bool Ok => Steps.All(s => s.Ok);
}

/// <summary>One driver call: which setting (a resource key), whether it was accepted, and the driver's own reason when not.
/// <see cref="Unsupported"/> separates "this card or driver has no such control" from a refusal of the value.</summary>
public sealed record TuningStepResult(string SettingKey, bool Ok, string? Error, bool Unsupported = false);

/// <summary>One GPU the app can tune. Settings are volatile: the driver drops them on a reboot or a driver reset, which is also what makes
/// a crash during the automatic search recoverable.</summary>
public interface IGpuTuningDevice
{
    /// <summary>Stable across reboots (the driver's GPU UUID), so a saved profile is only ever offered to the card it was made on.</summary>
    string Id { get; }
    string Name { get; }
    GpuTuningLimits Limits { get; }
    GpuTelemetry ReadTelemetry();
    /// <summary>What the driver reports now. The clock cap cannot be read back, so it is always null here.</summary>
    GpuTuningSettings ReadCurrent();
    TuningApplyResult Apply(GpuTuningSettings settings);
    TuningApplyResult Reset();
}

/// <summary>The tunable GPUs, or the reason there are none (no NVIDIA driver, no administrator rights, an unsupported vendor).</summary>
public interface IGpuTuningProvider
{
    IReadOnlyList<IGpuTuningDevice> Devices { get; }
    /// <summary>A resource key explaining why <see cref="Devices"/> is empty or incomplete; null when every GPU was opened.</summary>
    string? UnavailableReasonKey { get; }
    string? UnavailableDetail { get; }
}
