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
        public GpuTelemetry ReadTelemetry() => new(DateTimeOffset.UtcNow, Current.MaxClockMHz ?? 1905, 9751, 65, 340 - 0.2 * Current.CoreOffsetMHz, 40);
        public GpuTuningSettings ReadCurrent() => Current;
        public TuningApplyResult Apply(GpuTuningSettings s)
        {
            Applied.Add(s);
            if (refuseLock && s.MaxClockMHz is not null) return new([new("Tuning_MaxClock", false, "Not Supported", true)]);
            Current = s; return new([]);
        }
        public TuningApplyResult Reset() { Resets++; Current = GpuTuningSettings.Stock; return new([]); }
    }

    private sealed class FakeLoad(Func<GpuTuningSettings> current, int stableTo) : IGpuLoad
    {
        public LoadRunResult Run(GpuLoadKind kind, TimeSpan duration, TimeSpan settle, CancellationToken ct)
        {
            Thread.Sleep(duration); ct.ThrowIfCancellationRequested();
            return new(100, current().CoreOffsetMHz > stableTo ? 1 : 0, false, null);
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
        var tuner = new GpuAutoTuner(card, new FakeLoad(() => card.Current, stableTo: 120), journal.Add, TimeSpan.FromMilliseconds(5));
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
        var outcome = await new GpuAutoTuner(card, new FakeLoad(() => card.Current, 120), _ => { }, TimeSpan.FromMilliseconds(5)).RunAsync(new UndervoltSearch(card.Limits, Quick), CancellationToken.None);
        Assert.Equal(AutoTuneVerdict.Unsupported, outcome.Verdict); Assert.Equal("Tuning_Out_ApplyRefused", outcome.ReasonKey);
        Assert.Contains("Not Supported", outcome.Detail); Assert.True(card.Current.IsStock);
    }

    [Fact] public async Task Cancelling_puts_the_card_back_to_stock()
    {
        var card = new FakeCard(); using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        var outcome = await new GpuAutoTuner(card, new FakeLoad(() => card.Current, 1000), _ => { }, TimeSpan.FromMilliseconds(5))
            .RunAsync(new UndervoltSearch(card.Limits, Quick with { ProbeDuration = TimeSpan.FromMilliseconds(50) }), cts.Token);
        Assert.Equal(AutoTuneVerdict.Cancelled, outcome.Verdict); Assert.True(card.Current.IsStock);
    }
}
