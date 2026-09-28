using Mazesta.Core.Tuning; using Mazesta.Diagnostics.Tuning;
using Xunit;
namespace Mazesta.Diagnostics.Tests;

public class GpuAutoTunerTests
{
    private sealed class FakeCard(bool refuseLock = false) : IGpuTuningDevice
    {
        public List<GpuTuningSettings> Applied { get; } = [];
        public int Resets { get; private set; }
        public GpuTuningSettings Current { get; private set; } = GpuTuningSettings.Stock;
        public string Id => "GPU-1"; public string Name => "Fake";
        public GpuTuningLimits Limits { get; } = new(-1000, 1000, 0, 0, 2100, 100, 365, 350, 1, 30, 100);
        private int _reads; public int Reads => Volatile.Read(ref _reads);
        public GpuTelemetry ReadTelemetry() { Interlocked.Increment(ref _reads); return Telemetry(); }
        private GpuTelemetry Telemetry() => new(DateTimeOffset.UtcNow, Current.MaxClockMHz ?? 1905, 9751, 65, 340 - 0.2 * Current.CoreOffsetMHz, 40);
        public GpuTuningSettings ReadCurrent() => Current;
        public TuningApplyResult Apply(GpuTuningSettings s)
        {
            Applied.Add(s);
            if (refuseLock && s.MaxClockMHz is not null) return new([new("Tuning_MaxClock", false, "Not Supported", true)]);
            Current = s; return new([]);
        }
        public TuningApplyResult Reset() { Resets++; Current = GpuTuningSettings.Stock; return new([]); }
    }

    /// <summary>Ends a run once the tuner's sampler has read the card, not after a wall-clock sleep: a sleep of a few milliseconds raced the
    /// sampler's thread, and on a busy thread pool a run could end unsampled and the search stop with no telemetry.</summary>
    private sealed class FakeLoad(FakeCard card, int stableTo, Action<int>? onRun = null) : IGpuLoad
    {
        private int _runs;
        public LoadRunResult Run(GpuLoadKind kind, TimeSpan duration, TimeSpan settle, CancellationToken ct)
        {
            int reads = card.Reads; onRun?.Invoke(++_runs);
            SpinWait.SpinUntil(() => card.Reads > reads || ct.IsCancellationRequested, TimeSpan.FromSeconds(30));
            ct.ThrowIfCancellationRequested();
            return new(100, card.Current.CoreOffsetMHz > stableTo ? 1 : 0, false, null);
        }
    }

    private static readonly AutoTuneOptions Quick = new()
    {
        BaselineDuration = TimeSpan.FromMilliseconds(60), ProbeDuration = TimeSpan.FromMilliseconds(30), ConfirmDuration = TimeSpan.FromMilliseconds(60),
        ProbeSettle = TimeSpan.Zero, LongSettle = TimeSpan.Zero
    };

    [Fact] public async Task Finds_an_undervolt_journals_every_step_and_leaves_the_card_at_stock()
    {
        var card = new FakeCard(); var journal = new List<GpuTuningSettings?>();
        var tuner = new GpuAutoTuner(card, new FakeLoad(card, stableTo: 120), journal.Add, TimeSpan.FromMilliseconds(5));
        var outcome = await tuner.RunAsync(new UndervoltSearch(card.Limits, Quick), CancellationToken.None);
        Assert.Equal(AutoTuneVerdict.Improved, outcome.Verdict);
        Assert.Equal(105, outcome.Settings!.CoreOffsetMHz);
        Assert.True(card.Current.IsStock); Assert.True(card.Resets >= 1);
        Assert.Equal(card.Applied, journal.Where(j => j is not null));   // each apply was journalled first
        Assert.Null(journal[^1]);
    }

    [Fact] public async Task A_driver_that_refuses_the_clock_cap_ends_the_search_as_unsupported()
    {
        var card = new FakeCard(refuseLock: true);
        var outcome = await new GpuAutoTuner(card, new FakeLoad(card, 120), _ => { }, TimeSpan.FromMilliseconds(5)).RunAsync(new UndervoltSearch(card.Limits, Quick), CancellationToken.None);
        Assert.Equal(AutoTuneVerdict.Unsupported, outcome.Verdict); Assert.Equal("Tuning_Out_ApplyRefused", outcome.ReasonKey);
        Assert.Contains("Not Supported", outcome.Detail); Assert.True(card.Current.IsStock);
    }

    [Fact] public async Task Cancelling_puts_the_card_back_to_stock()
    {
        var card = new FakeCard(); using var cts = new CancellationTokenSource();
        var load = new FakeLoad(card, 1000, run => { if (run == 2) cts.Cancel(); });   // cancelled during the first probe, with an offset applied
        var outcome = await new GpuAutoTuner(card, load, _ => { }, TimeSpan.FromMilliseconds(5)).RunAsync(new UndervoltSearch(card.Limits, Quick), cts.Token);
        Assert.Equal(AutoTuneVerdict.Cancelled, outcome.Verdict); Assert.Equal(30, card.Applied[^1].CoreOffsetMHz); Assert.True(card.Current.IsStock);
    }
}
