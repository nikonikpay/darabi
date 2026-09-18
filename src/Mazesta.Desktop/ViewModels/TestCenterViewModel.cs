using System.Collections.ObjectModel; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics;
namespace Mazesta.Desktop.ViewModels;

/// <summary>Test Center page: queue selection/configuration plus the live per-row progress, in one
/// page rather than a separate progress page (spec §8's queue and progress requirements, without the
/// extra page split some other UI references use - this app's own pages stay one-per-concern only
/// where the concern is actually independent).</summary>
public sealed partial class TestCenterViewModel : ObservableObject, IDisposable
{
    private readonly TestEngine _engine;
    private readonly Func<Action, object> _dispatch;

    public ObservableCollection<TestQueueRowViewModel> Rows { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty] private string? _incompleteSessionMessage;

    public TestCenterViewModel(TestEngine engine, IEnumerable<ITestExecutor> executors, Func<Action, object> dispatch)
    {
        _engine = engine; _dispatch = dispatch;
        Rows = new(executors.Select(e => new TestQueueRowViewModel(e.Definition)));
        // Reflects a run already in progress if this page is re-created after the technician
        // navigated away and back (TestEngine, not this view model, owns the running Task - see
        // TestEngine.RequestCancel's own note).
        IsRunning = engine.State == TestEngineState.Running;
        engine.TestStarted += OnTestStarted;
        engine.TestProgressChanged += OnTestProgress;
        engine.TestCompleted += OnTestCompleted;
        if (engine.FindIncompleteSession() is { } cp)
            IncompleteSessionMessage = Loc.Format("Test_IncompleteSession_Message", cp.CurrentIndex + 1, cp.QueueTestIds.Count);
    }

    private TestQueueRowViewModel? RowFor(TestId id) => Rows.FirstOrDefault(r => r.Definition.Id == id);
    private void OnTestStarted(TestId id) => _dispatch(() => { if (RowFor(id) is { } row) { row.Outcome = TestOutcome.Running; row.PercentComplete = 0; row.StatusText = Loc.Get("Test_Status_Starting"); } });
    private void OnTestProgress(TestId id, TestProgress p) => _dispatch(() => { if (RowFor(id) is { } row) { row.PercentComplete = p.PercentComplete; row.StatusText = Loc.Get(p.StatusKey); } });
    private void OnTestCompleted(TestId id, TestRunResult r) => _dispatch(() =>
    {
        if (RowFor(id) is not { } row) return;
        row.Outcome = r.Outcome; row.ErrorCount = r.ErrorCount; row.Detail = r.Detail; row.StatusText = "";
        if (r.Outcome is TestOutcome.Passed or TestOutcome.Failed) row.PercentComplete = 1.0;
    });

    /// <summary>Selection does not drive CanExecute here: Start simply does nothing if nothing was
    /// selected (checked below), which keeps this view model from having to re-subscribe to every
    /// row's PropertyChanged just to keep one button's enabled state current.</summary>
    [RelayCommand(CanExecute = nameof(CanStartOrCancel))]
    private async Task Start()
    {
        var queue = new List<QueuedTest>();
        foreach (var row in Rows.Where(r => r.IsSelected))
        {
            row.ResetRunState();
            if (row.TryBuildQueuedTest() is not { } q) return;   // the row now shows its own validation error
            queue.Add(q);
        }
        if (queue.Count == 0) return;
        IncompleteSessionMessage = null;
        IsRunning = true;
        try { await _engine.RunAsync(queue); }
        finally { IsRunning = false; }
    }
    private bool CanStartOrCancel() => !IsRunning;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _engine.RequestCancel();

    [RelayCommand] private void DismissIncompleteSession() { _engine.DismissIncompleteSession(); IncompleteSessionMessage = null; }

    public void Dispose()
    {
        _engine.TestStarted -= OnTestStarted;
        _engine.TestProgressChanged -= OnTestProgress;
        _engine.TestCompleted -= OnTestCompleted;
    }
}
