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

    public TestCenterViewModel(TestEngine engine, IEnumerable<ITestExecutor> executors, Func<Action, object> dispatch)
    {
        _engine = engine; _dispatch = dispatch;
        Rows = new(executors.Select(e => new TestQueueRowViewModel(e.Definition)));
        IsRunning = engine.State == TestEngineState.Running;
        engine.StateChanged += OnStateChanged;
        engine.TestStarted += OnTestStarted;
        engine.TestProgressChanged += OnTestProgress;
        engine.TestCompleted += OnTestCompleted;
        if (engine.FindIncompleteSession() is { } cp)
            IncompleteSessionMessage = Loc.Format("Test_IncompleteSession_Message", cp.CurrentIndex + 1, cp.QueueTestIds.Count);
    }

    private TestQueueRowViewModel? RowFor(TestId id) => Rows.FirstOrDefault(r => r.Definition.Id == id);
    private void OnStateChanged(TestEngineState s) => _dispatch(() => IsRunning = s == TestEngineState.Running);
    private void OnTestStarted(TestId id) => _dispatch(() => { if (RowFor(id) is { } row) { row.Outcome = TestOutcome.Running; row.PercentComplete = 0; row.StatusText = Loc.Get("Test_Status_Starting"); } });
    private void OnTestProgress(TestId id, TestProgress p) => _dispatch(() => { if (RowFor(id) is { } row) { row.PercentComplete = p.PercentComplete; row.StatusText = Loc.Get(p.StatusKey); } });
    private void OnTestCompleted(TestId id, TestRunResult r) => _dispatch(() =>
    {
        if (RowFor(id) is not { } row) return;
        row.Outcome = r.Outcome; row.ErrorCount = r.ErrorCount; row.Detail = r.Detail; row.StatusText = "";
        if (r.Outcome is TestOutcome.Passed or TestOutcome.Failed) row.PercentComplete = 1.0;
    });

    /// <summary>Start with nothing selected is a no-op rather than a disabled button: re-subscribing to every
    /// row's PropertyChanged just to drive one button's enabled state is not worth it at this size.</summary>
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
        if (queue.Count == 0) return;
        IncompleteSessionMessage = null;
        IsRunning = true;   // immediately, so a double-click cannot start twice before StateChanged is dispatched
        await _engine.RunAsync(queue);
    }
    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _engine.RequestCancel();

    [RelayCommand] private void DismissIncompleteSession() { _engine.DismissIncompleteSession(); IncompleteSessionMessage = null; }

    public void Dispose()
    {
        _engine.StateChanged -= OnStateChanged;
        _engine.TestStarted -= OnTestStarted;
        _engine.TestProgressChanged -= OnTestProgress;
        _engine.TestCompleted -= OnTestCompleted;
    }
}
