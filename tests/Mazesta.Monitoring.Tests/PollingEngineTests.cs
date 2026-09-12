using Xunit;
using Mazesta.Core.Hardware; using Mazesta.Core.Time; using Mazesta.Core.Providers; using Mazesta.Monitoring; using Mazesta.Monitoring.Tests.Fakes; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Monitoring.Tests;
public class PollingEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static (PollingEngine e, FakeSensorProvider p, FakeClock c, BoundedEventLog log) Build()
    {
        var c = new FakeClock(T0); var p = new FakeSensorProvider();
        p.Nodes.Add(FakeSensorProvider.Node(HardwareKind.Cpu, "cpu/x", "temperature/0")); p.Nodes.Add(FakeSensorProvider.Node(HardwareKind.Storage, "storage/S1", "temperature/0"));
        var log = new BoundedEventLog(c, NullLogger.Instance);
        return (new PollingEngine(p, c, new MonitoringOptions { FastInterval = TimeSpan.FromSeconds(2), StorageInterval = TimeSpan.FromMinutes(15) }, log), p, c, log);
    }
    [Fact] public void First_tick_polls_every_node_then_storage_waits_for_slow_cadence()
    {
        var (e, p, c, _) = Build(); e.PrepareForManualTicks();   // no thread: every poll below comes from an explicit TickOnce()
        e.TickOnce();
        Assert.Equal(2, p.Requests[^1].NodesToUpdate.Count);
        c.Advance(TimeSpan.FromSeconds(2)); e.TickOnce();
        Assert.Equal(["cpu/x"], p.Requests[^1].NodesToUpdate.Select(x => x.Value));
        c.Advance(TimeSpan.FromMinutes(15)); e.TickOnce();
        Assert.Equal(2, p.Requests[^1].NodesToUpdate.Count);
    }
    [Fact] public void Snapshot_is_published_with_history_and_stats_applied()
    {
        var (e, p, c, _) = Build(); SensorSnapshot? got = null; e.SnapshotPublished += s => got = s;
        e.PrepareForManualTicks(); e.TickOnce();
        Assert.NotNull(got); Assert.Equal(2, got!.Readings.Count);
        Assert.Equal(42.0, e.Statistics.Get(p.Nodes[0].Sensors[0].Id).Max); Assert.NotEmpty(e.History.GetRaw(p.Nodes[0].Sensors[0].Id).Seconds);
    }
    [Fact] public void Stale_detection_uses_node_cadence()
    {
        var (e, p, c, _) = Build(); e.PrepareForManualTicks();
        p.OnPoll = r => new PollResult([new SensorReading(p.Nodes[1].Sensors[0].Id, 30, T0, DataQuality.Ok, "fake"), new SensorReading(p.Nodes[0].Sensors[0].Id, 50, T0, DataQuality.Ok, "fake")],
                                       new Dictionary<HardwareId, NodeStatus> { [p.Nodes[0].Id] = NodeStatus.Healthy(T0), [p.Nodes[1].Id] = NodeStatus.Healthy(T0) });
        c.Advance(TimeSpan.FromSeconds(10)); var s = e.TickOnce()!;
        Assert.Equal(DataQuality.Stale, s.Readings.Single(r => r.Id.Hardware.Value == "cpu/x").Quality);          // 10 s > 3 × 2 s
        Assert.Equal(DataQuality.Ok, s.Readings.Single(r => r.Id.Hardware.Value == "storage/S1").Quality);        // 10 s < 3 × 15 min
    }
    [Fact] public void Interval_change_applies_and_rejects_invalid()
    {
        var (e, _, _, _) = Build(); e.SetFastInterval(TimeSpan.FromSeconds(5)); Assert.Equal(TimeSpan.FromSeconds(5), e.FastInterval);
        Assert.Throws<ArgumentOutOfRangeException>(() => e.SetFastInterval(TimeSpan.FromSeconds(3)));
    }
    [Fact] public void Pause_stops_ticks_and_resume_continues()
    {
        var (e, p, c, _) = Build(); e.PrepareForManualTicks();
        e.Pause(); Assert.Equal(EngineState.Paused, e.State); c.Advance(TimeSpan.FromSeconds(4)); Assert.Null(e.TickOnce()); Assert.Empty(p.Requests);
        e.Resume(); Assert.Equal(EngineState.Running, e.State); Assert.NotNull(e.TickOnce());
    }
    [Fact] public void Overrun_is_logged_once_per_minute()
    {
        var (e, p, c, log) = Build(); e.PrepareForManualTicks();
        p.OnPollSideEffect = () => c.Advance(TimeSpan.FromSeconds(3));   // poll "takes" 3 s > 2 s interval
        e.TickOnce(); e.TickOnce(); e.TickOnce();
        Assert.Single(log.Snapshot(), x => x.Key == PollingEngine.KeyPollOverrun);
        c.Advance(TimeSpan.FromSeconds(61)); e.TickOnce();
        Assert.Equal(2, log.Snapshot().Count(x => x.Key == PollingEngine.KeyPollOverrun));
    }
    [Fact] public void Provider_exception_outside_contract_fails_engine_not_process()
    {
        var (e, p, _, log) = Build(); e.PrepareForManualTicks(); p.OnPoll = _ => throw new InvalidOperationException("bug");
        Assert.Null(e.TickOnce()); Assert.Equal(EngineState.Failed, e.State); Assert.Contains(log.Snapshot(), x => x.Key == PollingEngine.KeyEngineFailed);
    }
    [Fact] public void Real_thread_publishes_and_stop_disposes_provider()
    {
        var (e, p, _, _) = Build(); var published = new ManualResetEventSlim(); e.SnapshotPublished += _ => published.Set();
        e.Start(); Assert.True(published.Wait(TimeSpan.FromSeconds(5))); e.Stop(); e.Dispose();
        Assert.True(p.Started && p.Disposed); Assert.Equal(EngineState.Stopped, e.State);
    }
    [Fact] public void Throwing_subscriber_does_not_fail_engine()
    {
        var (e, p, _, log) = Build(); e.PrepareForManualTicks();
        e.SnapshotPublished += _ => throw new InvalidOperationException("ui bug");
        var s = e.TickOnce();
        Assert.NotNull(s); Assert.Equal(EngineState.Running, e.State);
        Assert.Contains(log.Snapshot(), x => x.Key == PollingEngine.KeySubscriberFailed);
        Assert.DoesNotContain(log.Snapshot(), x => x.Key == PollingEngine.KeyEngineFailed);
    }
    [Fact] public void Dispose_twice_is_safe_and_disposes_provider_once()
    {
        var (e, p, _, _) = Build(); e.PrepareForManualTicks();
        e.Dispose();
        var ex = Record.Exception(() => e.Dispose());
        Assert.Null(ex);
        Assert.True(p.Disposed);
        Assert.Equal(1, p.DisposeCalls);
    }
    [Fact] public void Provider_start_failure_fails_engine_not_process()
    {
        var (e, p, _, log) = Build(); p.ThrowOnStart = new InvalidOperationException("boom");
        e.PrepareForManualTicks();
        Assert.Null(e.TickOnce()); Assert.Equal(EngineState.Failed, e.State);
        Assert.Contains(log.Snapshot(), x => x.Key == PollingEngine.KeyEngineFailed);
    }
    [Fact] public void Pause_stops_publishing_and_resume_continues()
    {
        var clock = new SystemClock(); var p = new FakeSensorProvider();
        p.Nodes.Add(FakeSensorProvider.Node(HardwareKind.Cpu, "cpu/x", "temperature/0")); p.Nodes.Add(FakeSensorProvider.Node(HardwareKind.Storage, "storage/S1", "temperature/0"));
        var log = new BoundedEventLog(clock, NullLogger.Instance);
        var e = new PollingEngine(p, clock, new MonitoringOptions { FastInterval = TimeSpan.FromSeconds(1), StorageInterval = TimeSpan.FromMinutes(15) }, log);
        int count = 0; var firstArrived = new ManualResetEventSlim();
        e.SnapshotPublished += _ => { Interlocked.Increment(ref count); firstArrived.Set(); };
        try
        {
            e.Start();
            Assert.True(firstArrived.Wait(TimeSpan.FromSeconds(5)));

            e.Pause();
            Thread.Sleep(300);                 // let any in-flight tick finish
            int countAtPause = count;
            Thread.Sleep(2500);                // >= 2 intervals with nothing happening
            Assert.Equal(countAtPause, count);
            Assert.Equal(EngineState.Paused, e.State);

            var resumed = new ManualResetEventSlim();
            e.SnapshotPublished += _ => { if (count > countAtPause) resumed.Set(); };
            e.Resume();
            Assert.True(resumed.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal(EngineState.Running, e.State);
        }
        finally { e.Stop(); e.Dispose(); }
    }
    [Fact] public void Provider_start_throwing_on_real_thread_fails_engine_not_process()
    {
        // Provider.Start() runs on the polling thread. An exception there must land in the engine's
        // own handler and flip State to Failed - an unhandled exception on a background thread
        // takes the whole process down, and this app is the customer's diagnostic tool.
        var clock = new SystemClock(); var p = new FakeSensorProvider { ThrowOnStart = new InvalidOperationException("driver refused") };
        var log = new BoundedEventLog(clock, NullLogger.Instance);
        var e = new PollingEngine(p, clock, new MonitoringOptions { FastInterval = TimeSpan.FromSeconds(1) }, log);
        var failed = new ManualResetEventSlim();
        e.StateChanged += st => { if (st == EngineState.Failed) failed.Set(); };
        try
        {
            e.Start();
            Assert.True(failed.Wait(TimeSpan.FromSeconds(5)), "engine did not report Failed within 5 s");
            Assert.Equal(EngineState.Failed, e.State);
            Assert.False(System.Diagnostics.Process.GetCurrentProcess().HasExited);
            Assert.Contains(log.Snapshot(), ev => ev.Key == PollingEngine.KeyEngineFailed && ev.Detail.Contains("driver refused"));
        }
        finally { e.Stop(); e.Dispose(); }
    }
    [Fact] public void Rearm_storage_nodes_makes_storage_due_on_the_next_tick()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero)); var p = new FakeSensorProvider();
        p.Nodes.Add(FakeSensorProvider.Node(HardwareKind.Cpu, "cpu/x", "temperature/0"));
        p.Nodes.Add(FakeSensorProvider.Node(HardwareKind.Storage, "storage/S1", "temperature/0"));
        var e = new PollingEngine(p, clock, new MonitoringOptions { FastInterval = TimeSpan.FromSeconds(1), StorageInterval = TimeSpan.FromMinutes(15) }, new BoundedEventLog(clock, NullLogger.Instance));
        e.PrepareForManualTicks();
        e.TickOnce();                                                     // first tick updates everything
        clock.Advance(TimeSpan.FromSeconds(1)); e.TickOnce();
        Assert.DoesNotContain(new HardwareId("storage/S1"), p.Requests[^1].NodesToUpdate);
        e.RearmStorageNodes();
        clock.Advance(TimeSpan.FromSeconds(1)); e.TickOnce();
        Assert.Contains(new HardwareId("storage/S1"), p.Requests[^1].NodesToUpdate);
        e.Dispose();
    }
}
