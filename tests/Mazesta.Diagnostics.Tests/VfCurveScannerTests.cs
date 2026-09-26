using Mazesta.Core.Tuning; using Mazesta.Diagnostics.Tuning;
using Xunit;
namespace Mazesta.Diagnostics.Tests;

public class VfCurveScannerTests
{
    /// <summary>A card that runs at its cap up to <paramref name="topMHz"/> (then no higher: power or its curve), at 0.7 V + 0.2 mV per MHz above 1200.</summary>
    private sealed class Card(int topMHz = 1950, bool refuse = false) : IGpuTuningDevice
    {
        public GpuTuningSettings Current { get; private set; } = GpuTuningSettings.Stock; public int Resets;
        public string Id => "GPU-1"; public string Name => "Fake";
        public GpuTuningLimits Limits { get; } = new(-1000, 1000, 0, 0, 2100, 100, 365, 350, 1, 30, 100);
        public double Clock => Math.Min(Current.MaxClockMHz ?? topMHz, topMHz);
        public GpuTelemetry ReadTelemetry() => new(DateTimeOffset.UtcNow, Clock, 9751, 60, 250, 40);
        public GpuTuningSettings ReadCurrent() => Current;
        public TuningApplyResult Apply(GpuTuningSettings s) { if (refuse) return new([new("Tuning_MaxClock", false, "Not Supported", true)]); Current = s; return new([]); }
        public TuningApplyResult Reset() { Resets++; Current = GpuTuningSettings.Stock; return new([]); }
    }
    private sealed class Load(bool lost = false) : IGpuLoad
    {
        public GpuLoadKind? Kind;
        public LoadRunResult Run(GpuLoadKind kind, TimeSpan duration, TimeSpan settle, CancellationToken ct) { Kind = kind; Thread.Sleep(duration); ct.ThrowIfCancellationRequested(); return new(1, 0, lost, lost ? "DXGI_ERROR_DEVICE_REMOVED" : null); }
    }
    private static readonly VfScanOptions Quick = new() { Duration = TimeSpan.FromMilliseconds(40), Settle = TimeSpan.Zero, SampleInterval = TimeSpan.FromMilliseconds(4) };
    private static Func<(DateTimeOffset, double)?> Volts(Card c) => () => (DateTimeOffset.UtcNow, 0.7 + Math.Max(0, c.Clock - 1200) * 0.0002);

    [Fact] public void The_plan_steps_from_half_the_maximum_clock_up_to_the_maximum()
    {
        var plan = VfCurveScanner.Plan(new(-1000, 1000, 0, 0, 2100, null, null, null, 0, null, null), 60);
        Assert.Equal(1020, plan[0]); Assert.Equal(2100, plan[^1]); Assert.All(plan.Zip(plan.Skip(1)), p => Assert.True(p.Second > p.First));
    }

    [Fact] public async Task Records_the_clock_and_voltage_the_card_ran_stops_at_its_top_and_leaves_it_at_stock()
    {
        var card = new Card(); var load = new Load(); var journal = new List<GpuTuningSettings?>();
        var result = await new VfCurveScanner(card, load, Volts(card), journal.Add, Quick).RunAsync(CancellationToken.None);
        Assert.True(result.Ok);
        Assert.Equal(1950, result.Points[^1].ClockMHz); Assert.Equal(0.85, result.Points[^1].VoltageV, 3);
        Assert.Equal(result.Points.Count, result.Points.Select(p => p.ClockMHz).Distinct().Count());   // a cap it could not reach adds nothing
        Assert.Equal(GpuLoadKind.Memory, load.Kind);
        Assert.Equal(GpuTuningSettings.Stock, card.Current); Assert.True(card.Resets >= 2); Assert.Null(journal[^1]);
    }

    [Fact] public async Task Without_a_voltage_sensor_there_is_no_curve()
    {
        var card = new Card();
        var result = await new VfCurveScanner(card, new Load(), () => null, _ => { }, Quick).RunAsync(CancellationToken.None);
        Assert.Equal("Tuning_Curve_NoVoltage", result.ReasonKey); Assert.Empty(result.Points);
    }

    [Fact] public async Task A_reading_from_before_the_card_settled_is_not_used()
    {
        var card = new Card(); var old = DateTimeOffset.UtcNow.AddSeconds(-5);
        var result = await new VfCurveScanner(card, new Load(), () => (old, 0.7), _ => { }, Quick).RunAsync(CancellationToken.None);
        Assert.Equal("Tuning_Curve_NoVoltage", result.ReasonKey);
    }

    [Fact] public async Task A_refused_cap_or_a_lost_device_ends_the_scan_with_the_reason_and_the_card_at_stock()
    {
        var refusing = new Card(refuse: true);
        Assert.Equal("Tuning_Curve_ApplyRefused", (await new VfCurveScanner(refusing, new Load(), Volts(refusing), _ => { }, Quick).RunAsync(CancellationToken.None)).ReasonKey);
        var card = new Card();
        var lost = await new VfCurveScanner(card, new Load(lost: true), Volts(card), _ => { }, Quick).RunAsync(CancellationToken.None);
        Assert.Equal("Tuning_Curve_Lost", lost.ReasonKey); Assert.Equal(GpuTuningSettings.Stock, card.Current);
    }
}
