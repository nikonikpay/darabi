using Mazesta.Core.Time; using Mazesta.Diagnostics.Evidence; using Mazesta.Diagnostics.Whea; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Diagnostics;

public enum TestEngineState { Idle, Running, Stopped }

/// <summary>Sequential queue runner (spec §8). A plain async pipeline over <see cref="ITestExecutor"/>
/// calls - no dedicated thread like PollingEngine, since there is no cadence to own; CPU-bound executors
/// do their own Task.Run. Registered as a singleton so a run survives page navigation.</summary>
public sealed class TestEngine(IEnumerable<ITestExecutor> executors, JsonStore<TestSessionCheckpoint> checkpoints, IClock clock, PollingEngine? liveEngine = null, IHardwareErrorSource? hardwareErrors = null)
{
    private readonly IReadOnlyDictionary<TestId, ITestExecutor> _executors = executors.ToDictionary(e => e.Definition.Id);
    private CancellationTokenSource? _cts;
    public TestEngineState State { get; private set; } = TestEngineState.Idle;
    public event Action<TestEngineState>? StateChanged;
    public event Action<TestId>? TestStarted;
    public event Action<TestId, TestRunResult>? TestCompleted;
    public event Action<TestId, TestProgress>? TestProgressChanged;

    /// <summary>A checkpoint that never reached Completed means the process ended mid-queue (crash, kill,
    /// reboot - spec §2.5/§8). Null when the last session finished cleanly or none has run.</summary>
    public TestSessionCheckpoint? FindIncompleteSession()
    {
        var loaded = checkpoints.Load();
        return loaded.Outcome != LoadOutcome.Defaulted && !loaded.Value.Completed && loaded.Value.QueueTestIds.Count > 0 ? loaded.Value : null;
    }

    public void DismissIncompleteSession() => checkpoints.Save(new TestSessionCheckpoint { Completed = true });

    /// <summary>Cancels the run in progress, if any. The engine owns the token, so any view model - including
    /// one built after the technician navigated away and back - can still stop a run it did not start.</summary>
    public void RequestCancel() => _cts?.Cancel();

    public async Task RunAsync(IReadOnlyList<QueuedTest> queue, CancellationToken external = default)
    {
        if (queue.Count == 0) throw new ArgumentException("Queue is empty.", nameof(queue));
        if (State == TestEngineState.Running) throw new InvalidOperationException("A queue is already running.");
        var checkpoint = new TestSessionCheckpoint { SessionId = Guid.NewGuid().ToString("N"), QueueTestIds = [.. queue.Select(q => q.Definition.Id.Value)], StartedAt = clock.UtcNow, LastUpdatedAt = clock.UtcNow };
        _cts = CancellationTokenSource.CreateLinkedTokenSource(external);   // before Running, so a Cancel that follows the state change always finds it
        var ct = _cts.Token;
        SetState(TestEngineState.Running);
        try
        {
            for (int i = 0; i < queue.Count; i++)
            {
                checkpoint.CurrentIndex = i; checkpoint.LastUpdatedAt = clock.UtcNow; checkpoints.Save(checkpoint);
                var item = queue[i];
                TestStarted?.Invoke(item.Definition.Id);
                var result = await RunQueuedAsync(item, ct).ConfigureAwait(false);
                TestCompleted?.Invoke(item.Definition.Id, result);
            }
        }
        finally
        {
            checkpoint.CurrentIndex = queue.Count; checkpoint.Completed = true; checkpoint.LastUpdatedAt = clock.UtcNow; checkpoints.Save(checkpoint);
            _cts.Dispose(); _cts = null;
            SetState(TestEngineState.Stopped);
        }
    }

    /// <summary>Runs one queue entry through its repeat mode (spec §2.5/§8: "some faults don't show on
    /// first pass") and folds every iteration into one result via <see cref="TestRunResult.Combine"/>.
    /// Cancelled/Unsupported stop the loop; a Failed iteration does not, so an intermittent fault a few
    /// loops in is still caught.</summary>
    private async Task<TestRunResult> RunQueuedAsync(QueuedTest item, CancellationToken ct)
    {
        var id = item.Definition.Id;
        if (!_executors.TryGetValue(id, out var executor))
            return TestRunResult.Unsupported(id, clock.UtcNow, $"No executor registered for '{id}'.");

        var started = clock.UtcNow;
        var request = new TestExecutionRequest(item.DurationSeconds, clock, p => TestProgressChanged?.Invoke(id, p), liveEngine, new TestOptions(item.Definition, item.Options));
        TestRunResult? total = null; int iteration = 0;
        do
        {
            iteration++;
            TestRunResult single;
            if (ct.IsCancellationRequested) single = TestRunResult.Cancelled(id, started, clock.UtcNow);
            else
            {
                try { single = await executor.RunAsync(request, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { single = TestRunResult.Cancelled(id, started, clock.UtcNow); }
            }
            total = total?.Combine(single) ?? single;
            if (single.Outcome is TestOutcome.Cancelled or TestOutcome.Unsupported) break;
        }
        while (item.Repeat switch { RepeatMode.Once => false, RepeatMode.Count => iteration < item.RepeatCount, RepeatMode.Unlimited => true, _ => false });
        return WithHardwareErrors(total!, started);
    }

    /// <summary>Windows hardware errors (WHEA) logged while the test ran turn it into a Failed one: the machine
    /// itself reported a fault, whatever the test's own checksums said. An unreadable log is noted, never a failure.</summary>
    private TestRunResult WithHardwareErrors(TestRunResult result, DateTimeOffset since)
    {
        if (hardwareErrors is null || result.Outcome == TestOutcome.Unsupported) return result;   // nothing ran, so nothing to blame
        try
        {
            var events = hardwareErrors.Since(since);
            if (events.Count == 0) return result;
            string ids = string.Join(", ", events.Select(e => e.EventId).Distinct().Order());
            return result.Combine(new(result.Id, TestOutcome.Failed, since, clock.UtcNow, events.Count, $"WHEA logged {events.Count} hardware error record(s) during this test (event ids {ids}): {events[0].Summary}"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return result with { Detail = SensorEvidence.Join(result.Detail, $"WHEA log could not be read ({ex.GetType().Name})") };
        }
    }

    private void SetState(TestEngineState s) { if (State == s) return; State = s; StateChanged?.Invoke(s); }
}
