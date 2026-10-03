using System.Collections.ObjectModel; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics;
namespace Mazesta.Desktop.ViewModels;

/// <summary>Test Center page: queue selection/configuration and live per-row progress on one page.
/// The singleton <see cref="TestEngine"/> owns the run; this view model only mirrors its state, so a page
/// rebuilt after navigating away and back stays correct.</summary>
public sealed partial class TestCenterViewModel : ObservableObject, IDisposable
{
    private readonly TestEngine _engine;
    private readonly Func<Action, object> _dispatch;

    public ObservableCollection<TestQueueRowViewModel> Rows { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasIncompleteSession))] private string? _incompleteSessionMessage;
    public bool HasIncompleteSession => IncompleteSessionMessage is not null;
    /// <summary>The running queue, in order, as the engine started it: the live monitor says "test 3 of 7" from it.</summary>
    public IReadOnlyList<TestId> RunQueue { get; private set; } = [];
    /// <summary>The position in <see cref="RunQueue"/> of the test running now (or last run), -1 before any.</summary>
    [ObservableProperty] private int _currentIndex = -1;
    /// <summary>When the current test started, for the elapsed time on the live monitor.</summary>
    public DateTimeOffset? CurrentStartedAt { get; private set; }
    public TestQueueRowViewModel? CurrentRow => CurrentIndex >= 0 && CurrentIndex < RunQueue.Count ? RowFor(RunQueue[CurrentIndex]) : null;
    /// <summary>Runs the processor's, the memory's and the graphics card's tests side by side instead of one by one (<see cref="TestEngine.LaneOf"/>).</summary>
    [ObservableProperty] private bool _together;
    /// <summary>The tests under way now, in the queue's order: several only in a run <see cref="Together"/>.</summary>
    public IReadOnlyList<TestQueueRowViewModel> RunningRows => [.. RunQueue.Select(RowFor).OfType<TestQueueRowViewModel>().Where(r => r.Outcome == TestOutcome.Running)];

    /// <summary>Why the last Start was refused (a benchmark or the GPU tuning is running), or null.</summary>
    [ObservableProperty] private string? _blockedMessage;

    private readonly IBreakEventSource? _breaks;

    public TestCenterViewModel(TestEngine engine, IEnumerable<ITestExecutor> executors, Func<Action, object> dispatch, IBreakEventSource? breaks = null)
    {
        _engine = engine; _dispatch = dispatch; _breaks = breaks;
        Rows = new(executors.Select(e => new TestQueueRowViewModel(e.Definition, e)));
        foreach (var row in Rows) row.PropertyChanged += OnRowChanged;
        IsRunning = engine.State == TestEngineState.Running;
        engine.StateChanged += OnStateChanged;
        engine.SessionStarted += OnSessionStarted;
        engine.TestStarted += OnTestStarted;
        engine.TestProgressChanged += OnTestProgress;
        engine.TestCompleted += OnTestCompleted;
        if (engine.FindIncompleteSession() is { } cp) IncompleteSessionMessage = Describe(cp);
    }

    /// <summary>Where the broken-off session stopped, what it had found by then, and why it stopped as far as Windows' logs say (never guessed).</summary>
    internal string Describe(TestSessionCheckpoint cp)
    {
        string Name(string id) => RowFor(new TestId(id))?.Name ?? id;
        string at = cp.CurrentIndex < cp.QueueTestIds.Count ? Name(cp.QueueTestIds[cp.CurrentIndex]) : "";
        string finished = cp.Finished.Count == 0 ? Loc.Get("Test_IncompleteSession_NoneFinished")
            : string.Join(Loc.IsRtl ? "، " : ", ", cp.Finished.Select(f => $"{Name(f.TestId)}: {Loc.Get("Test_Outcome_" + f.Outcome)}"));
        string text = Loc.Format("Test_IncompleteSession_Detail", cp.CurrentIndex + 1, cp.QueueTestIds.Count, at, (int)Math.Round(cp.CurrentPercent * 100), finished);
        if (_breaks is null) return text;
        try
        {
            var why = SessionBreak.Classify(cp.LastUpdatedAt, _breaks.BootTime, _breaks.Since(cp.LastUpdatedAt.AddSeconds(-15)));
            text += " " + (why.Code is { } code ? Loc.Format("Test_Break_BlueScreenCode", code) : Loc.Get($"Test_Break_{why.Cause}"));
            if (why.DisplayResets > 0) text += " " + Loc.Format("Test_Break_Tdr", why.DisplayResets);
        }
        catch (Exception e) when (e is System.Diagnostics.Eventing.Reader.EventLogException or UnauthorizedAccessException) { }   // no reason is better than a guessed one
        return text;
    }

    private TestQueueRowViewModel? RowFor(TestId id) => Rows.FirstOrDefault(r => r.Definition.Id == id);
    private void OnStateChanged(TestEngineState s) => _dispatch(() => IsRunning = s == TestEngineState.Running);
    private void OnSessionStarted(IReadOnlyList<QueuedTest> queue) { var ids = queue.Select(q => q.Definition.Id).ToList(); _dispatch(() => { RunQueue = ids; CurrentIndex = -1; CurrentStartedAt = null; }); }
    private void OnTestStarted(TestId id) => _dispatch(() =>
    {
        int next = -1;
        for (int i = Math.Max(0, CurrentIndex + 1); i < RunQueue.Count; i++) if (RunQueue[i] == id) { next = i; break; }   // the same test may be queued once only, but search forward anyway
        if (next < 0) for (int i = 0; i < RunQueue.Count; i++) if (RunQueue[i] == id) { next = i; break; }   // side by side, the lanes do not start in the queue's order
        CurrentStartedAt = DateTimeOffset.Now; CurrentIndex = next;
        if (RowFor(id) is { } row) { row.Outcome = TestOutcome.Running; row.PercentComplete = 0; row.StatusText = Loc.Get("Test_Status_Starting"); }
    });
    private void OnTestProgress(TestId id, TestProgress p) => _dispatch(() => { if (RowFor(id) is { } row) { row.PercentComplete = p.PercentComplete; row.StatusText = Loc.Get(p.StatusKey); } });
    private void OnTestCompleted(TestId id, TestRunResult r) => _dispatch(() =>
    {
        if (RowFor(id) is not { } row) return;
        row.Outcome = r.Outcome; row.ErrorCount = r.ErrorCount; row.Detail = r.Detail; row.StatusText = ""; row.Advice = TestAdvice.For(id.Value, r.Outcome);
        if (r.Outcome is TestOutcome.Passed or TestOutcome.Failed) row.PercentComplete = 1.0;
        // Side by side: when the test shown as current ends while another lane still runs, that one is the current test.
        if (CurrentRow == row && RunningRows is [var other, ..]) for (int i = 0; i < RunQueue.Count; i++) if (RunQueue[i] == other.Definition.Id) { CurrentIndex = i; break; }
    });

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(TestQueueRowViewModel.IsSelected)) StartCommand.NotifyCanExecuteChanged(); }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Start()
    {
        var queue = new List<QueuedTest>();
        foreach (var row in Rows.Where(r => r.IsSelected))
        {
            row.ResetRunState();
            if (row.TryBuildQueuedTest() is not { } q) return;   // the row now shows its own validation error
            queue.Add(q);
        }
        IncompleteSessionMessage = null; BlockedMessage = null;
        // The last session's queue is forgotten first: until the engine reports this one, its last test (the drives' SMART check) would read as the current one.
        RunQueue = []; CurrentIndex = -1; CurrentStartedAt = null;
        IsRunning = true;   // immediately, so a double-click cannot start twice before StateChanged is dispatched
        try { await _engine.RunAsync(queue, together: Together); }
        catch (WorkloadBusyException e) { IsRunning = false; BlockedMessage = Loc.Get($"Workload_Busy_{e.Holder}"); }
    }
    private bool CanStart() => !IsRunning && Rows.Any(r => r.IsSelected);

    /// <summary>The note of the profile last applied, shown under the profile buttons; null before any.</summary>
    [ObservableProperty] private string? _profileNote;

    /// <summary>Selects exactly a profile's tests (those this machine can run) with its lengths, once each; every other test is cleared.</summary>
    public void ApplyProfile(string id)
    {
        var profile = TestProfiles.All.FirstOrDefault(p => p.Id == id) ?? throw new ArgumentException("unknown profile");
        foreach (var row in Rows)
        {
            var entry = profile.Tests.FirstOrDefault(t => t.TestId == row.Definition.Id.Value);
            row.IsSelected = entry is not null && row.IsAvailable;
            if (entry is null) continue;
            row.DurationText = entry.Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture); row.Repeat = RepeatMode.Once;
        }
        ProfileNote = Loc.Get(profile.NoteKey);
    }

    [RelayCommand] private void SelectAll() { foreach (var row in Rows.Where(r => r.IsAvailable)) row.IsSelected = true; }
    [RelayCommand] private void ClearSelection() { foreach (var row in Rows) row.IsSelected = false; }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _engine.RequestCancel();

    [RelayCommand] private void DismissIncompleteSession() { _engine.DismissIncompleteSession(); IncompleteSessionMessage = null; }

    public void Dispose()
    {
        foreach (var row in Rows) row.PropertyChanged -= OnRowChanged;
        _engine.StateChanged -= OnStateChanged;
        _engine.SessionStarted -= OnSessionStarted;
        _engine.TestStarted -= OnTestStarted;
        _engine.TestProgressChanged -= OnTestProgress;
        _engine.TestCompleted -= OnTestCompleted;
    }
}
