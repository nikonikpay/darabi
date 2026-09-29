using Mazesta.Core.Time; using Mazesta.Diagnostics.Evidence; using Mazesta.Diagnostics.Whea; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Diagnostics;

public enum TestEngineState { Idle, Running, Stopped }

/// <summary>Sequential queue runner (spec §8). A plain async pipeline over <see cref="ITestExecutor"/>
/// calls - no dedicated thread like PollingEngine, since there is no cadence to own; CPU-bound executors
/// do their own Task.Run. Registered as a singleton so a run survives page navigation.</summary>
public sealed class TestEngine(IEnumerable<ITestExecutor> executors, JsonStore<TestSessionCheckpoint> checkpoints, IClock clock, PollingEngine? liveEngine = null, IHardwareErrorSource? hardwareErrors = null, WorkloadGate? gate = null, Storage.IStorageEventSource? storageEvents = null)
{
    private readonly IReadOnlyDictionary<TestId, ITestExecutor> _executors = executors.ToDictionary(e => e.Definition.Id);
    private CancellationTokenSource? _cts;
    public TestEngineState State { get; private set; } = TestEngineState.Idle;
    public event Action<TestEngineState>? StateChanged;
    /// <summary>Raised once when a queue starts, with the queue as it was asked for (definitions, durations, option values) - what a report needs to describe tests that never got to run.</summary>
    public event Action<IReadOnlyList<QueuedTest>>? SessionStarted;
    public event Action<TestId>? TestStarted;
    public event Action<TestId, TestRunResult>? TestCompleted;
    public event Action<TestId, TestProgress>? TestProgressChanged;
    /// <summary>Every line of the live log as it is written, on the thread that wrote it (the engine's or a test's worker).</summary>
    public event Action<TestLogEntry>? Logged;

    private const int LogCapacity = 500;
    private readonly Queue<TestLogEntry> _log = new();
    private readonly object _logLock = new();

    /// <summary>The current (or last) session's log, oldest first, up to the last <see cref="LogCapacity"/> lines: a page opened mid-run shows what came before.</summary>
    public IReadOnlyList<TestLogEntry> RecentLog() { lock (_logLock) return [.. _log]; }

    private void Emit(TestLogEntry entry)
    {
        lock (_logLock) { _log.Enqueue(entry); while (_log.Count > LogCapacity) _log.Dequeue(); }
        Logged?.Invoke(entry);
    }
    private void Emit(TestId? test, TestLogLevel level, string key, string? formula, params object?[] args) => Emit(new(clock.UtcNow, test, level, key, TestLog.Format(args), formula));

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
    public void RequestCancel()
    {
        if (_cts is not { IsCancellationRequested: false } cts) return;
        Emit(null, TestLogLevel.Warning, "Log_Cancel_Requested", null);
        cts.Cancel();
    }

    public async Task RunAsync(IReadOnlyList<QueuedTest> queue, CancellationToken external = default)
    {
        if (queue.Count == 0) throw new ArgumentException("Queue is empty.", nameof(queue));
        if (State == TestEngineState.Running) throw new InvalidOperationException("A queue is already running.");
        using var lease = gate is null ? null : gate.TryEnter(Workload.Tests) ?? throw new WorkloadBusyException(gate.Holder ?? Workload.Tests);
        var checkpoint = new TestSessionCheckpoint { SessionId = Guid.NewGuid().ToString("N"), QueueTestIds = [.. queue.Select(q => q.Definition.Id.Value)], StartedAt = clock.UtcNow, LastUpdatedAt = clock.UtcNow };
        _cts = CancellationTokenSource.CreateLinkedTokenSource(external);   // before Running, so a Cancel that follows the state change always finds it
        var ct = _cts.Token;
        lock (_logLock) _log.Clear();
        SetState(TestEngineState.Running); SessionStarted?.Invoke(queue);
        Emit(null, TestLogLevel.Info, "Log_Session_Start", null, queue.Count);
        bool reachedEnd = false;
        try
        {
            for (int i = 0; i < queue.Count; i++)
            {
                checkpoint.CurrentIndex = i; checkpoint.LastUpdatedAt = clock.UtcNow; checkpoints.Save(checkpoint);
                var item = queue[i];
                TestStarted?.Invoke(item.Definition.Id);
                string? options = item.Options is { Count: > 0 } o ? string.Join(", ", o.Where(x => x.Value.Length > 0).Select(x => $"{x.Key}={x.Value}")) : null;
                Emit(item.Definition.Id, TestLogLevel.Info, "Log_Test_Start", string.IsNullOrEmpty(options) ? null : options, i + 1, queue.Count, "@" + item.Definition.NameKey, item.DurationSeconds);
                var result = await RunQueuedAsync(item, ct).ConfigureAwait(false);
                Emit(item.Definition.Id, result.Outcome switch { TestOutcome.Passed => TestLogLevel.Info, TestOutcome.Failed => TestLogLevel.Error, _ => TestLogLevel.Warning },
                    "Log_Test_End", result.Detail, "@" + item.Definition.NameKey, "@Test_Outcome_" + result.Outcome, result.ErrorCount);
                TestCompleted?.Invoke(item.Definition.Id, result);
            }
            reachedEnd = true;
            Emit(null, TestLogLevel.Info, "Log_Session_End", null);
        }
        finally
        {
            // Only a queue that reached its end (every item has a result, cancelled ones included) is marked complete. A fault that escaped
            // the loop leaves the checkpoint at the item it was on, so the next start says the session broke off there instead of hiding it.
            if (reachedEnd) { checkpoint.CurrentIndex = queue.Count; checkpoint.Completed = true; }
            checkpoint.LastUpdatedAt = clock.UtcNow; checkpoints.Save(checkpoint);
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
        var request = new TestExecutionRequest(item.DurationSeconds, clock, p => TestProgressChanged?.Invoke(id, p), liveEngine, new TestOptions(item.Definition, item.Options), e => Emit(e with { Test = id }));
        TestRunResult? total = null; int iteration = 0;
        do
        {
            iteration++;
            if (item.Repeat != RepeatMode.Once) Emit(id, TestLogLevel.Info, "Log_Test_Iteration", null, iteration, item.Repeat == RepeatMode.Count ? item.RepeatCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : "∞");
            TestRunResult single;
            if (ct.IsCancellationRequested) single = TestRunResult.Cancelled(id, started, clock.UtcNow);
            else
            {
                try { single = await executor.RunAsync(request, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { single = TestRunResult.Cancelled(id, started, clock.UtcNow); }
                // An exception out of an executor is the app's fault, never the part's: the test is Error (the report is then Incomplete),
                // the queue goes on with the next test, and nothing is swallowed into a pass.
                catch (Exception e) { single = TestRunResult.Error(id, started, clock.UtcNow, e); Emit(id, TestLogLevel.Error, "Log_Test_Crashed", $"{e.GetType().Name}: {e.Message}"); }
            }
            total = total?.Combine(single) ?? single;
            if (single.Outcome is TestOutcome.Cancelled or TestOutcome.Unsupported or TestOutcome.Error) break;
        }
        while (item.Repeat switch { RepeatMode.Once => false, RepeatMode.Count => iteration < item.RepeatCount, RepeatMode.Unlimited => true, _ => false });
        return WithStorageEvents(WithHardwareErrors(total!, started), started);
    }

    /// <summary>Windows hardware errors (WHEA) logged while the test ran turn it into a Failed one: the machine
    /// itself reported a fault, whatever the test's own checksums said. An unreadable log is noted, never a failure.</summary>
    private TestRunResult WithHardwareErrors(TestRunResult result, DateTimeOffset since)
    {
        if (hardwareErrors is null || result.Outcome == TestOutcome.Unsupported) return result;   // nothing ran, so nothing to blame
        try
        {
            var events = hardwareErrors.Since(since);
            Emit(result.Id, events.Count > 0 ? TestLogLevel.Error : TestLogLevel.Info, "Log_Whea", $"System log, provider Microsoft-Windows-WHEA-Logger, since {since.ToLocalTime():HH:mm:ss}", events.Count);
            if (events.Count == 0) return result;
            string ids = string.Join(", ", events.Select(e => e.EventId).Distinct().Order());
            return result.Combine(new(result.Id, TestOutcome.Failed, since, clock.UtcNow, events.Count, $"WHEA logged {events.Count} hardware error record(s) during this test (event ids {ids}): {events[0].Summary}"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return result with { Detail = SensorEvidence.Join(result.Detail, $"WHEA log could not be read ({ex.GetType().Name})") };
        }
    }

    /// <summary>Storage events Windows logged while the test ran are added to its evidence and the log, whatever the test was: a paging error
    /// during a RAM test matters too. They never change the outcome (see <see cref="Storage.StorageEventSource"/>); an unreadable log is noted.</summary>
    private TestRunResult WithStorageEvents(TestRunResult result, DateTimeOffset since)
    {
        if (storageEvents is null || result.Outcome == TestOutcome.Unsupported) return result;
        try
        {
            var events = storageEvents.Since(since);
            string summary = Storage.StorageEventSource.Summarize(events);
            Emit(result.Id, events.Count > 0 ? TestLogLevel.Warning : TestLogLevel.Info, "Log_StorageEvents", events.Count > 0 ? summary : "System log: disk, storport, stornvme, storahci, Intel RST, NTFS", events.Count);
            return events.Count == 0 ? result : result with { Detail = SensorEvidence.Join(result.Detail,
                $"Windows logged {events.Count} storage event(s) during this test: {summary}; not proof of a failing drive on its own - check the cable, controller, driver and power too") };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return result with { Detail = SensorEvidence.Join(result.Detail, $"storage event log could not be read ({ex.GetType().Name})") };
        }
    }

    private void SetState(TestEngineState s) { if (State == s) return; State = s; StateChanged?.Invoke(s); }
}
