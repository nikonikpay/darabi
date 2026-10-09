using System.Diagnostics; using Mazesta.Core.Tuning;
namespace Mazesta.Diagnostics.Tuning;

/// <summary>What one load run produced: its score, the wrong results it caught, and whether the device was lost (with the error, for the log).</summary>
public sealed record LoadRunResult(double Throughput, long Errors, bool DeviceLost, string? Error);

/// <summary>A GPU load the tuner can run for a while. <paramref name="settle"/> is warm-up the score must not include.</summary>
public interface IGpuLoad { LoadRunResult Run(GpuLoadKind kind, TimeSpan duration, TimeSpan settle, CancellationToken ct); }

public sealed record AutoTuneProgress(int StepNumber, TuneStep Step, double Fraction, GpuTelemetry Latest);
public sealed record AutoTuneStepLog(int StepNumber, TuneStep Step, LoadMeasurement Measurement, string? Error);

/// <summary>
/// Runs an <see cref="IAutoTuneSearch"/> on a real card: applies each step's settings, runs the load while sampling the card's clock, power and
/// temperature, and reports the measurement back. Before every apply the settings are written to a journal and after the run it is cleared, so a
/// run that takes the machine down is known on the next start. Whatever happens - finished, cancelled, a driver reset, an exception - the card is
/// put back to stock at the end: a found profile is applied only when the technician chooses to.
/// </summary>
public sealed class GpuAutoTuner(IGpuTuningDevice device, IGpuLoad load, Action<GpuTuningSettings?> journal, TimeSpan? sampleInterval = null, Func<(DateTimeOffset At, double Volts)?>? voltage = null)
{
    private readonly TimeSpan _interval = sampleInterval ?? TimeSpan.FromMilliseconds(500);
    public event Action<AutoTuneProgress>? Progress;
    public event Action<AutoTuneStepLog>? StepFinished;

    public Task<AutoTuneOutcome> RunAsync(IAutoTuneSearch search, CancellationToken ct) => Task.Run(() => Run(search, ct), CancellationToken.None);

    /// <summary>One run of a load on the card as it is now - nothing applied, nothing reset: what the settings the technician has put on it do under the load.</summary>
    public Task<(LoadMeasurement Measurement, string? Error)> MeasureCurrentAsync(GpuLoadKind kind, TimeSpan duration, TimeSpan settle, CancellationToken ct)
        => Task.Run(() => Measure(1, new TuneStep(TuneStepKind.Baseline, device.ReadCurrent(), kind, duration, settle), ct), CancellationToken.None);

    private AutoTuneOutcome Run(IAutoTuneSearch search, CancellationToken ct)
    {
        int number = 0;
        try
        {
            while (search.Next() is { } step)
            {
                ct.ThrowIfCancellationRequested();
                number++;
                journal(step.Settings);
                var applied = device.Apply(step.Settings);
                if (!applied.Ok)
                {
                    string refused = string.Join("; ", applied.Steps.Where(s => !s.Ok).Select(s => $"{s.SettingKey}: {s.Error}"));
                    return new(AutoTuneVerdict.Unsupported, "Tuning_Out_ApplyRefused", null, null, null, Detail: refused);
                }
                var (measurement, error) = Measure(number, step, ct);
                if (measurement.DeviceLost) device.Reset();   // the driver has usually dropped the settings already; this makes sure of it
                journal(null);
                StepFinished?.Invoke(new(number, step, measurement, error));
                search.Report(measurement);
            }
            return search.Outcome ?? new(AutoTuneVerdict.Failed, "Tuning_Out_NoResult", null, null, null);
        }
        catch (OperationCanceledException) { return new(AutoTuneVerdict.Cancelled, "Tuning_Out_Cancelled", null, null, null); }
        finally { device.Reset(); journal(null); }
    }

    private (LoadMeasurement, string?) Measure(int number, TuneStep step, CancellationToken ct)
    {
        var samples = new List<GpuTelemetry>(); var volts = new List<double>(); var clock = Stopwatch.StartNew();
        var settled = DateTimeOffset.UtcNow + step.Settle; DateTimeOffset lastVolt = default;
        using var done = new CancellationTokenSource();
        var sampler = Task.Run(() =>
        {
            while (!done.IsCancellationRequested)
            {
                var t = device.ReadTelemetry();
                if (clock.Elapsed >= step.Settle)
                {
                    lock (samples) samples.Add(t);
                    // The monitor reads the voltage about once a second: a reading from before the card settled is not this step's, and each counts once.
                    if (voltage?.Invoke() is { } v && v.At >= settled && v.At != lastVolt) { lastVolt = v.At; lock (samples) volts.Add(v.Volts); }
                }
                Progress?.Invoke(new(number, step, Math.Clamp(clock.Elapsed / step.Duration, 0, 1), t));
                done.Token.WaitHandle.WaitOne(_interval);
            }
        }, CancellationToken.None);
        LoadRunResult result;
        try { result = load.Run(step.Load, step.Duration, step.Settle, ct); }
        finally { done.Cancel(); sampler.Wait(CancellationToken.None); }
        lock (samples) return (LoadMeasurement.From(samples, result.Throughput, result.Errors, result.DeviceLost, volts), result.Error);
    }
}
