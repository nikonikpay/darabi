namespace Mazesta.Core.Tuning;

public enum GpuLoadKind { Compute, Memory }
public enum TuneStepKind { Baseline, Probe, Confirm }

/// <summary>One run the automatic search asks for: these settings, this load, this long. The first <see cref="Settle"/> of it is warm-up and is
/// left out of the measurement, so a step is judged on the card's settled behaviour, not on the moment the clocks changed.</summary>
public sealed record TuneStep(TuneStepKind Kind, GpuTuningSettings Settings, GpuLoadKind Load, TimeSpan Duration, TimeSpan Settle);

/// <summary>What a run measured. <see cref="Throughput"/> is the load's own score (integer Gop/s for compute, GB/s for memory);
/// <see cref="Errors"/> counts results the GPU computed wrongly; <see cref="DeviceLost"/> is a driver reset or a removed device during the run.
/// The median clock is what the card <i>holds</i> under this load (a heavy one runs into the power limit); the peak is the highest it reached, which on
/// a factory-overclocked card is far above the held one - it is the card's real boost, so a search must not take the held clock for the factory clock.</summary>
public sealed record LoadMeasurement(double? MedianClockMHz, double? AveragePowerW, double? AverageTemperatureC, double? MaxTemperatureC, double Throughput, long Errors, bool DeviceLost,
    double? PeakClockMHz = null, double? PeakPowerW = null, double? AverageVoltageV = null)
{
    public bool Clean => Errors == 0 && !DeviceLost;

    /// <summary>The version of the scoring loads. A throughput is only comparable with one measured by the same load: version 2 keeps the GPU
    /// fully busy (version 1 left it idle about 30 % of the time between submissions), so a version-1 stock score would make any version-2 run
    /// look faster than it is.</summary>
    public const int CurrentLoadVersion = 2;

    public static LoadMeasurement From(IReadOnlyList<GpuTelemetry> samples, double throughput, long errors, bool deviceLost, IReadOnlyList<double>? volts = null)
    {
        static double? Median(IEnumerable<double?> values)
        {
            var v = values.OfType<double>().Order().ToArray();
            return v.Length == 0 ? null : v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2;
        }
        static double? Average(IEnumerable<double?> values) { var v = values.OfType<double>().ToArray(); return v.Length == 0 ? null : v.Average(); }
        static double? Max(IEnumerable<double?> values) { var v = values.OfType<double>().ToArray(); return v.Length == 0 ? null : v.Max(); }
        return new(Median(samples.Select(s => s.CoreClockMHz)), Average(samples.Select(s => s.PowerW)), Average(samples.Select(s => s.TemperatureC)),
            Max(samples.Select(s => s.TemperatureC)), throughput, errors, deviceLost, Max(samples.Select(s => s.CoreClockMHz)), Max(samples.Select(s => s.PowerW)),
            volts is { Count: > 0 } ? volts.Average() : null);
    }
}

/// <summary>Durations and step sizes of the automatic search. The defaults take roughly 10-20 minutes on a desktop card; tests shrink them.</summary>
public sealed record AutoTuneOptions
{
    public TimeSpan BaselineDuration { get; init; } = TimeSpan.FromSeconds(180);
    public TimeSpan ProbeDuration { get; init; } = TimeSpan.FromSeconds(40);
    public TimeSpan ProbeSettle { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ConfirmDuration { get; init; } = TimeSpan.FromSeconds(180);
    /// <summary>Baseline and confirmation are measured over the same settled window, so their temperatures are comparable.</summary>
    public TimeSpan LongSettle { get; init; } = TimeSpan.FromSeconds(60);
    /// <summary>NVIDIA cards move their clock in bins of about 15 MHz; steps are whole bins.</summary>
    public int ClockBinMHz { get; init; } = 15;
    public int CoreOffsetStep { get; init; } = 30;
    /// <summary>The largest offset the undervolt tries, whatever the driver allows: beyond it a card is almost never stable, and each step costs a run.</summary>
    public int MaxCoreOffset { get; init; } = 300;
    /// <summary>How far the chosen setting is backed off from the last stable one, so a card that passed at the edge is not left there.</summary>
    public int SafetyMarginMHz { get; init; } = 15;
    public int ClockStep { get; init; } = 30;
    public int MaxClockRaise { get; init; } = 300;
    public int MemoryOffsetStep { get; init; } = 200;
    public int MaxMemoryOffset { get; init; } = 1500;
    /// <summary>An undervolt is only kept when it saves at least this much power, or at least <see cref="MinTemperatureDropC"/> of temperature.</summary>
    public double MinPowerSavingPercent { get; init; } = 2;
    public double MinTemperatureDropC { get; init; } = 1;
    /// <summary>An overclock is only kept when its measured score beats stock by at least this much.</summary>
    public double MinGainPercent { get; init; } = 1;
    /// <summary>Tuned throughput may drop this much below stock and still count as "the same performance" - run-to-run noise.</summary>
    public double ThroughputNoisePercent { get; init; } = 2;
    /// <summary>"Overclock Plus" keeps going while the hottest second of a step stays under this, whatever power it draws (up to the driver's own power limit).</summary>
    public double PlusMaxTemperatureC { get; init; } = 84;
    /// <summary>The options of an Overclock Plus search: a longer reach for the core clock and finer, longer memory steps.</summary>
    public static AutoTuneOptions Plus() => new() { MaxClockRaise = 450, MemoryOffsetStep = 100, MaxMemoryOffset = 2000 };
}

public enum AutoTuneVerdict { Improved, NoImprovement, Unsupported, Failed, Cancelled }

/// <summary>How a search ended. <see cref="Settings"/> is set only for <see cref="AutoTuneVerdict.Improved"/>; the reason is a resource key with
/// its argument. The measurements are what the verdict rests on and are shown to the technician next to it.</summary>
public sealed record AutoTuneOutcome(AutoTuneVerdict Verdict, string ReasonKey, GpuTuningSettings? Settings, LoadMeasurement? Baseline, LoadMeasurement? Tuned,
    LoadMeasurement? MemoryBaseline = null, LoadMeasurement? MemoryTuned = null, string? Detail = null);

/// <summary>A search as a sequence of steps: the runner asks <see cref="Next"/> for a step, runs it, and hands the measurement to <see cref="Report"/>,
/// until <see cref="Next"/> returns null and <see cref="Outcome"/> is set. Kept free of any GPU so every decision is unit-testable.</summary>
public interface IAutoTuneSearch
{
    TuneStep? Next();
    void Report(LoadMeasurement measurement);
    AutoTuneOutcome? Outcome { get; }
}

/// <summary>
/// Automatic undervolt: keep the card's top clock - the highest it reaches at stock, not the lower one it holds while running into its power
/// limit - and find the largest core offset (a lower voltage for that same clock) that still computes correctly and still holds at least the
/// clock the card held at stock. The winner, backed off by a safety margin, is confirmed with a run as long as the baseline, and then with a
/// light load that lets the card reach its top clock (a heavy load never gets there, so it cannot test the top of the curve). It is kept only when
/// it measured less power, a lower temperature or a higher score than stock without losing performance. A cap at the held clock would only be a
/// performance limit that is cooler because it is slower.
/// </summary>
public sealed class UndervoltSearch(GpuTuningLimits limits, AutoTuneOptions options) : IAutoTuneSearch
{
    private enum Phase { Baseline, Probe, Confirm, ConfirmTop, Done }
    private Phase _phase = Phase.Baseline;
    private LoadMeasurement? _baseline, _confirmed;
    private int _target, _held, _lastGood, _candidate, _confirmTries;
    private TuneStep? _pending;
    public AutoTuneOutcome? Outcome { get; private set; }
    public LoadMeasurement? Baseline => _baseline;
    public int TargetClockMHz => _target;

    public TuneStep? Next()
    {
        _pending = _phase switch
        {
            Phase.Baseline => new(TuneStepKind.Baseline, GpuTuningSettings.Stock, GpuLoadKind.Compute, options.BaselineDuration, options.LongSettle),
            Phase.Probe => new(TuneStepKind.Probe, At(_lastGood + options.CoreOffsetStep), GpuLoadKind.Compute, options.ProbeDuration, options.ProbeSettle),
            Phase.Confirm => new(TuneStepKind.Confirm, At(_candidate), GpuLoadKind.Compute, options.ConfirmDuration, options.LongSettle),
            Phase.ConfirmTop => new(TuneStepKind.Confirm, At(_candidate), GpuLoadKind.Memory, options.ProbeDuration, options.ProbeSettle),
            _ => null
        };
        return _pending;
    }

    private GpuTuningSettings At(int offset) => new(offset, 0, _target, null, null);
    private int OffsetCeiling => Math.Min(limits.CoreOffsetMax, options.MaxCoreOffset);

    public void Report(LoadMeasurement m)
    {
        var step = _pending ?? throw new InvalidOperationException("Report without a step.");
        _pending = null;
        switch (_phase)
        {
            case Phase.Baseline:
                _baseline = m;
                if (!m.Clean) { Finish(AutoTuneVerdict.Failed, "Tuning_Out_StockUnstable"); return; }
                if (m.MedianClockMHz is not { } clock || m.AveragePowerW is null) { Finish(AutoTuneVerdict.Unsupported, "Tuning_Out_NoTelemetry"); return; }
                if (!limits.HasCoreOffset || OffsetCeiling < options.CoreOffsetStep) { Finish(AutoTuneVerdict.Unsupported, "Tuning_Out_NoCoreOffset"); return; }
                _held = (int)(clock / options.ClockBinMHz) * options.ClockBinMHz;
                _target = (int)(Math.Max(clock, m.PeakClockMHz ?? 0) / options.ClockBinMHz) * options.ClockBinMHz;
                _phase = Phase.Probe;
                return;
            case Phase.Probe:
                if (Holds(m)) { _lastGood = step.Settings.CoreOffsetMHz; if (_lastGood + options.CoreOffsetStep <= OffsetCeiling) return; }
                StartConfirm(_lastGood - options.SafetyMarginMHz);
                return;
            case Phase.Confirm:
                if (!Holds(m))
                {
                    if (++_confirmTries >= 3) { Finish(AutoTuneVerdict.NoImprovement, "Tuning_Out_ConfirmFailed"); return; }
                    StartConfirm(_candidate - options.CoreOffsetStep);
                    return;
                }
                _confirmed = m; _phase = Phase.ConfirmTop;
                return;
            case Phase.ConfirmTop:
                // The top of the curve: with the light load the card reaches its top clock (as at stock), cleanly. A lowered voltage that fails here is
                // one a game or a light workload would meet, though the heavy load never did.
                if (!m.Clean || m.MedianClockMHz is not { } top || top < _target - 2 * options.ClockBinMHz)
                {
                    if (++_confirmTries >= 3) { Finish(AutoTuneVerdict.NoImprovement, "Tuning_Out_ConfirmFailed"); return; }
                    StartConfirm(_candidate - options.CoreOffsetStep);
                    return;
                }
                var c = _confirmed!; var b = _baseline!;
                bool samePerformance = c.Throughput >= b.Throughput * (1 - options.ThroughputNoisePercent / 100);
                bool faster = c.Throughput >= b.Throughput * (1 + options.MinGainPercent / 100);
                bool savesPower = c.AveragePowerW is { } p && p <= b.AveragePowerW!.Value * (1 - options.MinPowerSavingPercent / 100);
                bool cooler = c.AverageTemperatureC is { } t && b.AverageTemperatureC is { } bt && t <= bt - options.MinTemperatureDropC;
                if (!samePerformance) Finish(AutoTuneVerdict.NoImprovement, "Tuning_Out_SlowerThanStock", c);
                else if (savesPower || cooler || faster) Finish(AutoTuneVerdict.Improved, "Tuning_Out_UndervoltFound", c, At(_candidate));
                else Finish(AutoTuneVerdict.NoImprovement, "Tuning_Out_NoSaving", c);
                return;
        }
    }

    /// <summary>Stable: nothing computed wrongly, the driver did not reset, and the card still reached the clock it held at stock.</summary>
    private bool Holds(LoadMeasurement m) => m.Clean && m.MedianClockMHz is { } c && c >= _held - 2 * options.ClockBinMHz;

    private void StartConfirm(int offset)
    {
        if (offset <= 0) { Finish(AutoTuneVerdict.NoImprovement, "Tuning_Out_NoStableOffset"); return; }
        _candidate = offset; _phase = Phase.Confirm;
    }

    private void Finish(AutoTuneVerdict verdict, string key, LoadMeasurement? tuned = null, GpuTuningSettings? settings = null)
    {
        _phase = Phase.Done;
        Outcome = new(verdict, key, verdict == AutoTuneVerdict.Improved ? settings : null, _baseline, tuned);
    }
}

/// <summary>
/// Automatic overclock, started from an undervolt (or from the stock clock): raise the clock cap bin by bin while the card stays correct,
/// actually runs faster, and draws no more power than it did at stock; then raise the memory offset while the measured memory bandwidth keeps
/// rising (GDDR6X corrects its own errors by retrying, so an unstable memory clock shows as <i>lower</i> bandwidth long before it shows as wrong
/// data). Each part is backed off one step, the result is confirmed, and it is kept only when it measurably beats stock.
/// </summary>
public sealed class OverclockSearch : IAutoTuneSearch
{
    private enum Phase { Baseline, Core, MemoryBaseline, Memory, Confirm, ConfirmMemory, Done }
    private readonly GpuTuningLimits _limits; private readonly AutoTuneOptions _options;
    private readonly bool _plus, _fromCap; private readonly int? _power;
    private Phase _phase;
    private LoadMeasurement? _baseline, _memoryBaseline, _confirmed;
    private int _offset, _startClock, _clock, _bestClock, _memory, _bestMemory, _confirmTries;
    private double _bestClockSeen, _bestBandwidth;
    private TuneStep? _pending;
    public AutoTuneOutcome? Outcome { get; private set; }

    /// <param name="start">The undervolt to build on (its offset and clock cap), or null to start from the stock clock with no offset.</param>
    /// <param name="baseline">Stock measured earlier in this session (the undervolt's baseline); null to measure it first.</param>
    /// <param name="plus">Above the factory ceiling: the power limit is raised to the most the driver allows and the search is bound by that limit and by
    /// <see cref="AutoTuneOptions.PlusMaxTemperatureC"/> instead of by the power the card drew at stock. Needs a card with a power limit to raise.</param>
    public OverclockSearch(GpuTuningLimits limits, AutoTuneOptions options, GpuTuningSettings? start, LoadMeasurement? baseline, bool plus = false)
    {
        _limits = limits; _options = options; _baseline = baseline; _plus = plus; _fromCap = start?.MaxClockMHz is not null;
        if (plus && limits.HasPowerLimit) _power = limits.PowerLimitMaxW;
        if (plus && _power is null) { Finish(AutoTuneVerdict.Unsupported, "Tuning_Out_NoPowerLimit"); return; }
        _offset = start?.CoreOffsetMHz ?? 0;
        if (start?.MaxClockMHz is { } clock) _startClock = clock;
        _phase = baseline is null || _startClock == 0 ? Phase.Baseline : Phase.Core;
        if (_phase == Phase.Core) BeginCore(baseline!);
    }

    private int ClockCeiling => Math.Min(_limits.MaxLockMHz, _startClock + _options.MaxClockRaise);
    private int MemoryCeiling => Math.Min(_limits.MemoryOffsetMax, _options.MaxMemoryOffset);
    /// <summary>A cap is set only above the clock the search started from (or on top of an undervolt's own cap): at the card's factory top it would only
    /// take away boost it already has when the load is lighter.</summary>
    private GpuTuningSettings At(int clock, int memory) => new(_offset, memory, clock > _startClock || _fromCap ? clock : null, _power, null);
    /// <summary>Whether a step stayed inside what the search allows: no more power than stock drew (+2 %), or - Plus - than the raised limit, and, Plus, a cool enough card.</summary>
    private bool InBudget(LoadMeasurement m)
        => _plus ? m.AveragePowerW is { } pw && pw <= _power!.Value && (m.MaxTemperatureC ?? 0) <= _options.PlusMaxTemperatureC
        : m.AveragePowerW is { } p && p <= _baseline!.AveragePowerW!.Value * 1.02;

    public TuneStep? Next()
    {
        _pending = _phase switch
        {
            Phase.Baseline => new(TuneStepKind.Baseline, GpuTuningSettings.Stock, GpuLoadKind.Compute, _options.BaselineDuration, _options.LongSettle),
            Phase.Core => new(TuneStepKind.Probe, At(_clock + _options.ClockStep, 0), GpuLoadKind.Compute, _options.ProbeDuration, _options.ProbeSettle),
            Phase.MemoryBaseline => new(TuneStepKind.Baseline, At(_bestClock, 0), GpuLoadKind.Memory, _options.ProbeDuration, _options.ProbeSettle),
            Phase.Memory => new(TuneStepKind.Probe, At(_bestClock, _memory + _options.MemoryOffsetStep), GpuLoadKind.Memory, _options.ProbeDuration, _options.ProbeSettle),
            Phase.Confirm => new(TuneStepKind.Confirm, At(_bestClock, _bestMemory), GpuLoadKind.Compute, _options.ConfirmDuration, _options.LongSettle),
            Phase.ConfirmMemory => new(TuneStepKind.Confirm, At(_bestClock, _bestMemory), GpuLoadKind.Memory, _options.ProbeDuration, _options.ProbeSettle),
            _ => null
        };
        return _pending;
    }

    private void BeginCore(LoadMeasurement baseline)
    {
        // The factory clock is the highest the card reached at stock, not the one it held while running into the power limit.
        if (_startClock == 0 && baseline.MedianClockMHz is { } c) _startClock = (int)(Math.Max(c, baseline.PeakClockMHz ?? 0) / _options.ClockBinMHz) * _options.ClockBinMHz;
        _clock = _bestClock = _startClock; _bestClockSeen = baseline.MedianClockMHz ?? 0;
        _phase = _clock + _options.ClockStep <= ClockCeiling ? Phase.Core : Phase.MemoryBaseline;
    }

    public void Report(LoadMeasurement m)
    {
        var step = _pending ?? throw new InvalidOperationException("Report without a step.");
        _pending = null;
        switch (_phase)
        {
            case Phase.Baseline:
                _baseline = m;
                if (!m.Clean) { Finish(AutoTuneVerdict.Failed, "Tuning_Out_StockUnstable"); return; }
                if (m.MedianClockMHz is null || m.AveragePowerW is null) { Finish(AutoTuneVerdict.Unsupported, "Tuning_Out_NoTelemetry"); return; }
                BeginCore(m);
                return;
            case Phase.Core:
                int tried = step.Settings.MaxClockMHz!.Value;
                bool faster = m.MedianClockMHz is { } c && c >= _bestClockSeen + _options.ClockBinMHz / 2.0;
                if (m.Clean && faster && InBudget(m))
                {
                    _clock = tried; _bestClockSeen = m.MedianClockMHz!.Value;
                    if (_clock + _options.ClockStep <= ClockCeiling) return;
                }
                // A clean step that did not run faster hit the power or temperature limit: the card cannot use a higher cap, so nothing is backed off.
                _bestClock = _clock > _startClock && !m.Clean ? Math.Max(_startClock, _clock - _options.SafetyMarginMHz) : _clock;
                _phase = Phase.MemoryBaseline;
                if (!_limits.HasMemoryOffset || MemoryCeiling < _options.MemoryOffsetStep) _phase = Phase.Confirm;
                return;
            case Phase.MemoryBaseline:
                _memoryBaseline = m;
                if (!m.Clean) { _phase = Phase.Confirm; return; }
                _bestBandwidth = m.Throughput; _phase = Phase.Memory;
                return;
            case Phase.Memory:
                if (m.Clean && m.Throughput >= _bestBandwidth * 1.005)
                {
                    _memory = step.Settings.MemoryOffsetMHz; _bestBandwidth = m.Throughput;
                    if (_memory + _options.MemoryOffsetStep <= MemoryCeiling) return;
                    _bestMemory = _memory;
                }
                else _bestMemory = Math.Max(0, _memory - _options.MemoryOffsetStep);   // the last gain may already have been near the edge
                _phase = Phase.Confirm;
                return;
            case Phase.Confirm:
                if (!m.Clean || !InBudget(m)) { Retry(); return; }
                _confirmed = m;
                _phase = _bestMemory > 0 ? Phase.ConfirmMemory : Phase.Done;
                if (_phase == Phase.Done) Judge(null);
                return;
            case Phase.ConfirmMemory:
                if (!m.Clean) { Retry(); return; }
                Judge(m);
                return;
        }
    }

    /// <summary>A failed confirmation drops the memory offset first (the likelier culprit, and the smaller gain), then one clock step.</summary>
    private void Retry()
    {
        if (++_confirmTries >= 3) { Finish(AutoTuneVerdict.NoImprovement, "Tuning_Out_ConfirmFailed"); return; }
        if (_bestMemory > 0) _bestMemory = 0;
        else if (_bestClock > _startClock) _bestClock = Math.Max(_startClock, _bestClock - _options.ClockStep);
        else { Finish(AutoTuneVerdict.NoImprovement, "Tuning_Out_ConfirmFailed"); return; }
        _phase = Phase.Confirm;
    }

    private void Judge(LoadMeasurement? memory)
    {
        var b = _baseline!; var c = _confirmed!;
        bool computeGain = c.Throughput >= b.Throughput * (1 + _options.MinGainPercent / 100);
        bool memoryGain = memory is not null && _memoryBaseline is { Clean: true } mb && memory.Throughput >= mb.Throughput * (1 + _options.MinGainPercent / 100);
        var settings = At(_bestClock, _bestMemory);
        if (_plus && !settings.PowerLimitW.HasValue) { Finish(AutoTuneVerdict.NoImprovement, "Tuning_Out_NoGain", c, null, memory); return; }
        if (computeGain || memoryGain) Finish(AutoTuneVerdict.Improved, "Tuning_Out_OverclockFound", c, settings, memory);
        else Finish(AutoTuneVerdict.NoImprovement, "Tuning_Out_NoGain", c, null, memory);
    }

    private void Finish(AutoTuneVerdict verdict, string key, LoadMeasurement? tuned = null, GpuTuningSettings? settings = null, LoadMeasurement? memory = null)
    {
        _phase = Phase.Done;
        Outcome = new(verdict, key, verdict == AutoTuneVerdict.Improved ? settings : null, _baseline, tuned, _memoryBaseline, memory);
    }
}
