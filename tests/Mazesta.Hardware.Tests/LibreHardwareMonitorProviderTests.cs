using Mazesta.Core.Providers; using LibreHardwareMonitor.Hardware; using Mazesta.Core.Hardware; using Mazesta.Core.Time; using Mazesta.Hardware.Lhm; using Mazesta.Hardware.Tests.Fakes; using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace Mazesta.Hardware.Tests;
public class LibreHardwareMonitorProviderTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; set; } = now; }
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
    private static (LibreHardwareMonitorProvider p, FakeLhmComputer c, FixedClock clock) Build(bool pawn = true, bool elevated = true, string? serial = "SER1")
    {
        var c = new FakeLhmComputer(); var clock = new FixedClock(T0);
        var p = new LibreHardwareMonitorProvider(c, () => pawn, () => elevated, _ => serial, clock, NullLogger<LibreHardwareMonitorProvider>.Instance);
        return (p, c, clock);
    }
    private static FakeHardware Gpu(float? temp = 45) { var g = new FakeHardware(HardwareType.GpuNvidia, "/gpu-nvidia/0", "RTX"); g.Add("GPU Core", SensorType.Temperature, 0, temp); return g; }
    private static FakeHardware Disk() { var d = new FakeHardware(HardwareType.Storage, "/nvme/0", "SSD"); d.Add("Temperature", SensorType.Temperature, 0, 38); return d; }

    [Fact] public void Start_ready_when_open_succeeds_and_driver_present()
    {
        var (p, c, _) = Build(); c.Roots.Add(Gpu()); p.Start();
        Assert.True(c.Opened); Assert.Equal(ProviderState.Ready, p.Status.State); Assert.Equal(1, p.Status.SensorCount); Assert.Single(p.Hardware);
    }
    private static FakeHardware CpuWithTemp() { var c = new FakeHardware(HardwareType.Cpu, "/intelcpu/0", "Intel Core i9-14900K"); c.Add("CPU Package", SensorType.Temperature, 0, 41); c.Add("CPU Total", SensorType.Load, 0, 5); return c; }
    [Fact] public void Start_ready_when_cpu_temperatures_exist_even_if_pawnio_registry_check_fails()
    {
        var (p, c, _) = Build(pawn: false); c.Roots.Add(CpuWithTemp()); c.Roots.Add(Gpu()); p.Start();
        Assert.Equal(ProviderState.Ready, p.Status.State);
    }
    [Fact] public void Start_degraded_when_pawnio_missing_and_cpu_has_no_temperatures()
    {
        var (p, c, _) = Build(pawn: false); var cpu = new FakeHardware(HardwareType.Cpu, "/intelcpu/0", "i9"); cpu.Add("CPU Total", SensorType.Load, 0, 5); c.Roots.Add(cpu); p.Start();
        Assert.Equal((ProviderState.Degraded, LibreHardwareMonitorProvider.ReasonPawnIoMissing), (p.Status.State, p.Status.ReasonKey));
    }
    [Fact] public void Start_degraded_when_pawnio_missing()
    {
        var (p, c, _) = Build(pawn: false); c.Roots.Add(Gpu()); p.Start();
        Assert.Equal((ProviderState.Degraded, LibreHardwareMonitorProvider.ReasonPawnIoMissing), (p.Status.State, p.Status.ReasonKey));
    }
    [Fact] public void Start_degraded_when_not_elevated_takes_precedence()
    {
        var (p, c, _) = Build(pawn: false, elevated: false); c.Roots.Add(Gpu()); p.Start();
        Assert.Equal(LibreHardwareMonitorProvider.ReasonNotElevated, p.Status.ReasonKey);
    }
    [Fact] public void Start_failed_when_open_throws_and_never_propagates()
    {
        var (p, c, _) = Build(); c.ThrowOnOpen = new InvalidOperationException("boom"); p.Start();
        Assert.Equal((ProviderState.Failed, LibreHardwareMonitorProvider.ReasonOpenFailed), (p.Status.State, p.Status.ReasonKey)); Assert.Contains("boom", p.Status.Detail);
        Assert.Empty(p.Hardware); Assert.Equal(PollResult.Empty.Readings.Count, p.Poll(new PollRequest(T0, new HashSet<HardwareId>())).Readings.Count);
    }
    [Fact] public void Poll_updates_only_requested_nodes_but_emits_all_readings()
    {
        var (p, c, clock) = Build(); var gpu = Gpu(); var disk = Disk(); c.Roots.AddRange([gpu, disk]); p.Start();
        var all = p.Hardware.Select(h => h.Id).ToHashSet();
        var r1 = p.Poll(new PollRequest(clock.UtcNow, all));
        Assert.Equal((1, 1), (gpu.UpdateCalls, disk.UpdateCalls)); Assert.Equal(2, r1.Readings.Count);
        clock.UtcNow = T0.AddSeconds(2);
        var r2 = p.Poll(new PollRequest(clock.UtcNow, new HashSet<HardwareId> { p.Hardware[0].Id }));
        Assert.Equal((2, 1), (gpu.UpdateCalls, disk.UpdateCalls)); Assert.Equal(2, r2.Readings.Count);
        var diskReading = r2.Readings.Single(x => x.Id.Hardware.Value == "storage/SER1");
        Assert.Equal(T0, diskReading.Timestamp);                       // timestamp of the last actual read
        Assert.Equal(T0, r2.NodeStatus[diskReading.Id.Hardware].LastSuccessfulUpdate);
    }
    [Fact] public void Failing_node_is_isolated_and_marked_stale()
    {
        var (p, c, clock) = Build(); var gpu = Gpu(); var disk = Disk(); disk.ThrowOnUpdate = new IOException("smart failed"); c.Roots.AddRange([gpu, disk]); p.Start();
        var r = p.Poll(new PollRequest(clock.UtcNow, p.Hardware.Select(h => h.Id).ToHashSet()));
        var g = r.Readings.Single(x => x.Id.Hardware.Value.StartsWith("gpu/")); var d = r.Readings.Single(x => x.Id.Hardware.Value.StartsWith("storage/"));
        Assert.Equal(DataQuality.Ok, g.Quality); Assert.Equal(DataQuality.Stale, d.Quality);
        var st = r.NodeStatus[d.Id.Hardware]; Assert.False(st.IsOk); Assert.Contains("smart failed", st.FailureReason); Assert.Null(st.LastSuccessfulUpdate);
    }
    [Fact] public void Null_value_is_missing_and_zero_temperature_is_invalid()
    {
        var (p, c, clock) = Build(); var gpu = Gpu(null); gpu.Add("GPU Hot Spot", SensorType.Temperature, 1, 0); c.Roots.Add(gpu); p.Start();
        var r = p.Poll(new PollRequest(clock.UtcNow, p.Hardware.Select(h => h.Id).ToHashSet()));
        Assert.Equal(DataQuality.Missing, r.Readings[0].Quality); Assert.Equal(DataQuality.Invalid, r.Readings[1].Quality);
    }
    [Fact] public void Status_change_raises_event_and_dispose_closes()
    {
        var (p, c, _) = Build(); c.Roots.Add(Gpu()); var seen = new List<ProviderState>(); p.StatusChanged += s => seen.Add(s.State); p.Start(); p.Dispose();
        Assert.Equal([ProviderState.Starting, ProviderState.Ready], seen); Assert.True(c.Closed && c.Disposed);
    }
    [Fact] public void Dispose_never_throws_even_if_computer_dispose_throws()
    {
        var (p, c, _) = Build(); c.Roots.Add(Gpu()); p.Start(); c.ThrowOnDispose = new InvalidOperationException("x");
        var ex = Record.Exception(() => p.Dispose());
        Assert.Null(ex); Assert.True(c.Closed);
    }
}
