using Mazesta.Core.Time; using Mazesta.Diagnostics.Evidence; using Mazesta.Diagnostics.Whea; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Diagnostics;

public enum TestEngineState { Idle, Running, Stopped }

/// <summary>Queue runner (spec §8): one test after another, or the parts' load tests side by side (see <see cref="RunAsync"/>). A plain async pipeline over <see cref="ITestExecutor"/>
/// calls - no dedicated thread like PollingEngine, since there is no cadence to own; CPU-bound executors
/// do their own Task.Run. Registered as a singleton so a run survives page navigation.</summary>
public sealed class TestEngine(IEnumerable<ITestExecutor> executors, JsonStore<TestSessionCheckpoint> checkpoints, IClock clock, PollingEngine? liveEngine = null, IHardwareErrorSource? hardwareErrors = null, WorkloadGate? gate = null, Storage.IStorageEventSource? storageEvents = null,
    Core.Providers.INvmeHealthSource? nvme = null)
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
    /// <summary>An exception that escaped an executor, whole (type, message, stack), for the app's log: the result keeps only its message.</summary>
    public event Action<TestId, Exception>? Crashed;
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

    /// <summary>The part a load test keeps busy (cpu, memory, gpu), or null for a test that needs the machine to itself (drives, network, the
    /// combined power test, Windows' checks). In a run "together" each part's tests go one after another while the parts run side by side.</summary>
    public static string? LaneOf(TestId id) => id.Value.Split('.')[0] is "cpu" or "memory" or "gpu" ? id.Value.Split('.')[0] : null;

    /// <param name="together">Runs the processor's, the memory's and the graphics card's tests side by side (one lane per part), as a machine at
    /// work loads them, then the other tests one by one in the queue's order. With one lane or none it is the plain queue.</param>
    public async Task RunAsync(IReadOnlyList<QueuedTest> queue, CancellationToken external = default, bool together = false)
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
        var saving = new object();   // lanes share the checkpoint
        async Task RunItem(int i)
        {
            var item = queue[i];
            lock (saving) { checkpoint.CurrentIndex = i; checkpoint.LastUpdatedAt = clock.UtcNow; checkpoints.Save(checkpoint); }
            TestStarted?.Invoke(item.Definition.Id);
            string? options = item.Options is { Count: > 0 } o ? string.Join(", ", o.Where(x => x.Value.Length > 0).Select(x => $"{x.Key}={x.Value}")) : null;
            Emit(item.Definition.Id, TestLogLevel.Info, "Log_Test_Start", string.IsNullOrEmpty(options) ? null : options, i + 1, queue.Count, "@" + item.Definition.NameKey, item.DurationSeconds);
            var result = await RunQueuedAsync(item, ct, checkpoint, saving).ConfigureAwait(false);
            lock (saving)
            {
                checkpoint.Finished.Add(new() { TestId = item.Definition.Id.Value, Outcome = result.Outcome.ToString(), ErrorCount = result.ErrorCount });
                checkpoint.CurrentIteration = 0; checkpoint.CurrentPercent = 0; checkpoint.LastUpdatedAt = clock.UtcNow; checkpoints.Save(checkpoint);
            }
            Emit(item.Definition.Id, result.Outcome switch { TestOutcome.Passed => TestLogLevel.Info, TestOutcome.Failed => TestLogLevel.Error, _ => TestLogLevel.Warning },
                "Log_Test_End", result.Detail, "@" + item.Definition.NameKey, "@Test_Outcome_" + result.Outcome, result.ErrorCount);
            TestCompleted?.Invoke(item.Definition.Id, result);
        }
        try
        {
            var lanes = together ? Enumerable.Range(0, queue.Count).Where(i => LaneOf(queue[i].Definition.Id) is not null).GroupBy(i => LaneOf(queue[i].Definition.Id)).ToList() : [];
            if (lanes.Count > 1)
            {
                Emit(null, TestLogLevel.Info, "Log_Session_Together", null, lanes.Count);
                // Task.Run: an executor that works before its first await must not hold the other lanes back.
                await Task.WhenAll(lanes.Select(lane => Task.Run(async () => { foreach (int i in lane) await RunItem(i).ConfigureAwait(false); }))).ConfigureAwait(false);
                for (int i = 0; i < queue.Count; i++) if (LaneOf(queue[i].Definition.Id) is null) await RunItem(i).ConfigureAwait(false);
            }
            else
                for (int i = 0; i < queue.Count; i++) await RunItem(i).ConfigureAwait(false);
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
    private async Task<TestRunResult> RunQueuedAsync(QueuedTest item, CancellationToken ct, TestSessionCheckpoint? checkpoint = null, object? saving = null)
    {
        var id = item.Definition.Id;
        if (!_executors.TryGetValue(id, out var executor))
            return TestRunResult.Unsupported(id, clock.UtcNow, $"No executor registered for '{id}'.");

        var started = clock.UtcNow;
        var nvmeBefore = NvmeSnapshot(id);
        // The checkpoint follows the test's progress, saved at most every ten seconds: a crash later says how far the test had got.
        var lastSave = clock.UtcNow; int iteration = 0;
        void Progress(TestProgress p)
        {
            TestProgressChanged?.Invoke(id, p);
            if (checkpoint is null || clock.UtcNow - lastSave < TimeSpan.FromSeconds(10)) return;
            lastSave = clock.UtcNow;
            lock (saving ?? checkpoint)
            {
                checkpoint.CurrentIteration = iteration; checkpoint.CurrentPercent = p.PercentComplete; checkpoint.LastUpdatedAt = lastSave;
                try { checkpoints.Save(checkpoint); } catch (IOException) { }   // a checkpoint that cannot be written must not stop the test
            }
        }
        var request = new TestExecutionRequest(item.DurationSeconds, clock, Progress, liveEngine, new TestOptions(item.Definition, item.Options), e => Emit(e with { Test = id }));
        TestRunResult? total = null;
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
                catch (Exception e) { single = TestRunResult.Error(id, started, clock.UtcNow, e); Crashed?.Invoke(id, e); Emit(id, TestLogLevel.Error, "Log_Test_Crashed", $"{e.GetType().Name}: {e.Message}"); }
            }
            total = total?.Combine(single) ?? single;
            if (single.Outcome is TestOutcome.Cancelled or TestOutcome.Unsupported or TestOutcome.Error) break;
        }
        while (item.Repeat switch { RepeatMode.Once => false, RepeatMode.Count => iteration < item.RepeatCount, RepeatMode.Unlimited => true, _ => false });
        return WithNvmeLog(WithStorageEvents(WithHardwareErrors(total!, started), started), nvmeBefore);
    }

    /// <summary>The NVMe drives' health logs before a storage test, to compare with after it; null for other tests or when none can be read.</summary>
    private IReadOnlyList<Core.Providers.NvmeDriveHealth>? NvmeSnapshot(TestId id)
    {
        if (nvme is null || !id.Value.StartsWith("storage.", StringComparison.Ordinal)) return null;
        try { var all = nvme.Read(); return all.Count > 0 ? all : null; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
    }

    /// <summary>What each NVMe drive's own log says changed during a storage test. A drive that counted new media errors, or raised a critical
    /// warning, reported a fault itself: the test fails whatever its own checks found. New error-log entries are evidence only - drives also log
    /// commands they rejected, which is no fault of the media.</summary>
    private TestRunResult WithNvmeLog(TestRunResult result, IReadOnlyList<Core.Providers.NvmeDriveHealth>? before)
    {
        if (before is null || result.Outcome == TestOutcome.Unsupported) return result;
        IReadOnlyList<Core.Providers.NvmeDriveHealth> after;
        try { after = nvme!.Read(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return result with { Detail = SensorEvidence.Join(result.Detail, $"NVMe health log could not be read after the test ({ex.GetType().Name})") }; }
        var changes = NvmeChanges(before, after);
        Emit(result.Id, changes.Faults > 0 ? TestLogLevel.Error : changes.Notes.Count > 0 ? TestLogLevel.Warning : TestLogLevel.Info, "Log_NvmeLog",
            "NVMe log page 02h before and after: critical warning, media errors, error-log entries", changes.Notes.Count == 0 ? "-" : string.Join("; ", changes.Notes));
        if (changes.Notes.Count == 0) return result;
        var noted = result with { Detail = SensorEvidence.Join(result.Detail, "NVMe health log during the test: " + string.Join("; ", changes.Notes)) };
        return changes.Faults == 0 ? noted : noted.Combine(new(result.Id, TestOutcome.Failed, result.StartedAt, clock.UtcNow, changes.Faults, noted.Detail));
    }

    internal static (int Faults, List<string> Notes) NvmeChanges(IReadOnlyList<Core.Providers.NvmeDriveHealth> before, IReadOnlyList<Core.Providers.NvmeDriveHealth> after)
    {
        int faults = 0; var notes = new List<string>();
        foreach (var a in after)
        {
            if (before.FirstOrDefault(b => b.DiskNumber == a.DiskNumber) is not { } b) continue;
            string name = $"{a.Model ?? "disk"} (disk {a.DiskNumber})";
            if (a.Log.MediaErrors > b.Log.MediaErrors) { faults++; notes.Add($"{name}: media errors {b.Log.MediaErrors} -> {a.Log.MediaErrors}"); }
            byte raised = (byte)(a.Log.CriticalWarning & ~b.Log.CriticalWarning);
            if (raised != 0) { faults++; notes.Add($"{name}: critical warning raised: {string.Join(", ", (a.Log with { CriticalWarning = raised }).Warnings)}"); }
            if (a.Log.ErrorLogEntries > b.Log.ErrorLogEntries) notes.Add($"{name}: error-log entries {b.Log.ErrorLogEntries} -> {a.Log.ErrorLogEntries}");
        }
        return (faults, notes);
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
