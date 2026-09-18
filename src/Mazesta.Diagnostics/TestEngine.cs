using Mazesta.Core.Time; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Diagnostics;

public enum TestEngineState { Idle, Running, Stopped }

/// <summary>Sequential queue runner (spec §8: queue, per-test and overall progress, quick cancel). One
/// RunAsync call owns one session end-to-end. Unlike PollingEngine there is no dedicated background
/// thread here: there is no continuous cadence to own, only a plain async pipeline over
/// ITestExecutor calls, so a CPU-bound executor does its own Task.Run internally and the caller (a WPF
/// async RelayCommand) can await RunAsync without blocking the UI thread.</summary>
public sealed class TestEngine(IEnumerable<ITestExecutor> executors, JsonStore<TestSessionCheckpoint> checkpoints, IClock clock, PollingEngine? liveEngine = null)
{
    private readonly IReadOnlyDictionary<TestId, ITestExecutor> _executors = executors.ToDictionary(e => e.Definition.Id);
    private CancellationTokenSource? _cts;
    public TestEngineState State { get; private set; } = TestEngineState.Idle;
    public event Action<TestEngineState>? StateChanged;
    public event Action<TestId>? TestStarted;
    public event Action<TestId, TestRunResult>? TestCompleted;
    public event Action<TestId, TestProgress>? TestProgressChanged;

    /// <summary>A checkpoint left over from a previous run that never reached Completed = true means
    /// the process ended mid-queue - crash, kill, forced reboot (spec §2.5/§8). Null when the last
    /// session finished cleanly or none has run yet.</summary>
    public TestSessionCheckpoint? FindIncompleteSession()
    {
        var loaded = checkpoints.Load();
        return loaded.Outcome != LoadOutcome.Defaulted && !loaded.Value.Completed && loaded.Value.QueueTestIds.Count > 0 ? loaded.Value : null;
    }

    /// <summary>Dismisses a stale checkpoint without running anything - the technician has seen the
    /// "did not finish last time" notice and chosen not to resume.</summary>
    public void DismissIncompleteSession() => checkpoints.Save(new TestSessionCheckpoint { Completed = true });

    /// <summary>Cancels the run in progress, if any. A no-op call from a view model that did not start
    /// this run still works: the engine, not the caller, owns the token, so navigating away from the
    /// Test Center page and back still leaves a live Cancel path to whatever is actually running
    /// (spec's own UX note that leaving a progress view must not itself stop the test, and a technician
    /// coming back must still be able to stop it).</summary>
    public void RequestCancel() => _cts?.Cancel();

    public async Task RunAsync(IReadOnlyList<QueuedTest> queue, CancellationToken external = default)
    {
        if (queue.Count == 0) throw new ArgumentException("Queue is empty.", nameof(queue));
        if (State == TestEngineState.Running) throw new InvalidOperationException("A queue is already running.");
        var checkpoint = new TestSessionCheckpoint { SessionId = Guid.NewGuid().ToString("N"), QueueTestIds = [.. queue.Select(q => q.Definition.Id.Value)], StartedAt = clock.UtcNow, LastUpdatedAt = clock.UtcNow };
        SetState(TestEngineState.Running);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(external);
        var ct = _cts.Token;
        try
        {
            for (int i = 0; i < queue.Count; i++)
            {
                checkpoint.CurrentIndex = i; checkpoint.LastUpdatedAt = clock.UtcNow; checkpoints.Save(checkpoint);
                var item = queue[i];
                TestStarted?.Invoke(item.Definition.Id);
                var result = ct.IsCancellationRequested
                    ? TestRunResult.Cancelled(item.Definition.Id, clock.UtcNow, clock.UtcNow)
                    : await RunQueuedAsync(item, ct).ConfigureAwait(false);
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

    /// <summary>Runs one queue entry to its own completion, including its repeat mode (spec §2.5/§8:
    /// limited count or unlimited loop, "some faults don't show on first pass"). A single iteration's
    /// Cancelled or Unsupported outcome stops the repeat loop immediately; a Failed iteration does not,
    /// so an intermittent fault a few loops in is still caught and reported, with its error count
    /// accumulated across every iteration actually run.</summary>
    private async Task<TestRunResult> RunQueuedAsync(QueuedTest item, CancellationToken ct)
    {
        if (!_executors.TryGetValue(item.Definition.Id, out var executor))
            return TestRunResult.Unsupported(item.Definition.Id, clock.UtcNow, $"No executor registered for '{item.Definition.Id}'.");

        var started = clock.UtcNow;
        long totalErrors = 0; var outcome = TestOutcome.Passed; string? detail = null; int iteration = 0;
        do
        {
            iteration++;
            if (ct.IsCancellationRequested) { outcome = TestOutcome.Cancelled; break; }
            var request = new TestExecutionRequest(item.DurationSeconds, clock, p => TestProgressChanged?.Invoke(item.Definition.Id, p), liveEngine);
            TestRunResult single;
            try { single = await executor.RunAsync(request, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { single = TestRunResult.Cancelled(item.Definition.Id, started, clock.UtcNow); }
            totalErrors += single.ErrorCount;
            if (single.Outcome != TestOutcome.Passed)
            {
                outcome = single.Outcome; detail = single.Detail;
                if (single.Outcome is TestOutcome.Cancelled or TestOutcome.Unsupported) break;
            }
        }
        while (item.Repeat switch { RepeatMode.Once => false, RepeatMode.Count => iteration < Math.Max(1, item.RepeatCount), RepeatMode.Unlimited => true, _ => false });
        return new TestRunResult(item.Definition.Id, outcome, started, clock.UtcNow, totalErrors, detail);
    }

    private void SetState(TestEngineState s) { if (State == s) return; State = s; StateChanged?.Invoke(s); }
}
