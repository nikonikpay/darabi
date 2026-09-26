using Mazesta.Core.Tuning;
namespace Mazesta.Diagnostics.Tuning;

public sealed record VfScanProgress(int Step, int Steps, int RequestedMHz, VfPoint? Measured);

/// <summary>How a curve scan ended: the measured points (clock-ordered, one per clock the card really reached), or why there are none.</summary>
public sealed record VfScanResult(IReadOnlyList<VfPoint> Points, string? ReasonKey, string? Detail = null)
{
    public bool Ok => ReasonKey is null;
}

/// <summary>
/// Measures a card's stock voltage/frequency curve: the card is reset to stock, and for each clock from about half its maximum up to the maximum
/// the clock is capped there while the GPU runs the memory load (it keeps the core at full clock but draws far less power than the compute load,
/// so the power limit does not hide the top of the curve). The point recorded is the clock the card actually ran and the voltage it chose, both
/// medians of the settled samples - a measured point, never the requested clock. When two caps in a row no longer raise the clock, the card has
/// reached the top it can hold and the scan stops there. The settings go through the crash journal like the automatic search, and the card is
/// back at stock at the end whatever happened.
/// </summary>
public sealed class VfCurveScanner(IGpuTuningDevice device, IGpuLoad load, Func<(DateTimeOffset At, double Volts)?> voltage, Action<GpuTuningSettings?> journal, VfScanOptions? options = null)
{
    private readonly VfScanOptions _o = options ?? new();
    public event Action<VfScanProgress>? Progress;

    /// <summary>The caps the scan steps through: from half the card's maximum clock, rounded down to a step, up to the maximum.</summary>
    public static IReadOnlyList<int> Plan(GpuTuningLimits limits, int stepMHz)
    {
        int top = limits.MaxClockMHz ?? 2000, low = Math.Max(GpuTuningLimits.MinLockMHz, top / 2 / stepMHz * stepMHz);
        var caps = new List<int>();
        for (int c = low; c < top; c += stepMHz) caps.Add(c);
        caps.Add(top);
        return caps;
    }

    public Task<VfScanResult> RunAsync(CancellationToken ct) => Task.Run(() => Run(ct), CancellationToken.None);

    private VfScanResult Run(CancellationToken ct)
    {
        var plan = Plan(device.Limits, _o.StepMHz); var points = new List<VfPoint>(); int stalled = 0;
        try
        {
            device.Reset();
            for (int i = 0; i < plan.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var settings = GpuTuningSettings.Stock with { MaxClockMHz = plan[i] };
                journal(settings);
                var applied = device.Apply(settings);
                if (!applied.Ok) return new([], "Tuning_Curve_ApplyRefused", string.Join("; ", applied.Steps.Where(s => !s.Ok).Select(s => $"{s.SettingKey}: {s.Error}")));
                Progress?.Invoke(new(i + 1, plan.Count, plan[i], null));
                var (point, lost, error) = Measure(ct);
                journal(null);
                if (lost) return new(points, "Tuning_Curve_Lost", error);
                if (point is null) { if (points.Count == 0) return new([], "Tuning_Curve_NoVoltage"); continue; }
                Progress?.Invoke(new(i + 1, plan.Count, plan[i], point));
                // The card could not go higher than the last point (power, temperature, or the top of its curve): a repeat is not a new point.
                if (points.Count > 0 && point.Value.ClockMHz < points[^1].ClockMHz + _o.StepMHz / 2.0) { if (++stalled >= 2) break; continue; }
                stalled = 0; points.Add(point.Value);
            }
            return points.Count >= 2 ? new(points, null) : new(points, "Tuning_Curve_TooFew");
        }
        catch (OperationCanceledException) { return new(points, "Tuning_Out_Cancelled"); }
        finally { device.Reset(); journal(null); }
    }

    private (VfPoint? Point, bool Lost, string? Error) Measure(CancellationToken ct)
    {
        var clocks = new List<double>(); var volts = new List<double>(); var settled = DateTimeOffset.UtcNow + _o.Settle; DateTimeOffset lastVolt = default;
        using var done = new CancellationTokenSource();
        var sampler = Task.Run(() =>
        {
            while (!done.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= settled)
                {
                    if (device.ReadTelemetry().CoreClockMHz is { } c) lock (clocks) clocks.Add(c);
                    // A reading taken before the card settled on this cap (the monitor reads about once a second) is not this point's; each reading counts once.
                    if (voltage() is { } v && v.At >= settled && v.At != lastVolt) { lastVolt = v.At; lock (clocks) volts.Add(v.Volts); }
                }
                done.Token.WaitHandle.WaitOne(_o.SampleInterval);
            }
        }, CancellationToken.None);
        LoadRunResult result;
        try { result = load.Run(GpuLoadKind.Memory, _o.Duration, _o.Settle, ct); }
        finally { done.Cancel(); sampler.Wait(CancellationToken.None); }
        if (result.DeviceLost) return (null, true, result.Error);
        lock (clocks) return clocks.Count == 0 || volts.Count == 0 ? (null, false, null) : (new VfPoint(Median(clocks), Median(volts)), false, null);
    }

    private static double Median(List<double> values)
    {
        values.Sort(); int n = values.Count;
        return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2;
    }
}

/// <summary>Step and timing of a curve scan. The defaults take about a minute and a half on a desktop card; tests shrink them. The voltage comes
/// from the sensor monitor, which reads about once a second, so a step lasts long enough for a few settled readings.</summary>
public sealed record VfScanOptions
{
    public int StepMHz { get; init; } = 60;
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromMilliseconds(250);
}
