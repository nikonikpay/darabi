using System.Collections.ObjectModel; using System.Globalization; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input;
using Mazesta.Core.Hardware; using Mazesta.Core.Text; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.Desktop.ViewModels;

public sealed record MetricRow(string Name, string Value);

/// <summary>One benchmark on the Benchmarks page: its duration and options before a run, its progress during one, and the numbers after.</summary>
public sealed partial class BenchmarkRowViewModel : ObservableObject
{
    public BenchmarkRowViewModel(IBenchmark benchmark)
    {
        Benchmark = benchmark; _durationText = benchmark.Definition.DefaultDurationSeconds.ToString(CultureInfo.InvariantCulture);
        Options = [.. benchmark.Definition.Options.Select(o => new TestOptionViewModel(o))];
        foreach (var o in Options) o.PropertyChanged += (_, _) => RefreshAvailability();
        RefreshAvailability();
    }
    /// <summary>Why this machine cannot run the benchmark (no hardware ray tracing...); null when it can.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsAvailable))] private string? _unavailableText;
    public bool IsAvailable => UnavailableText is null;
    public void RefreshAvailability()
    {
        var u = TestAvailability.Check(Benchmark, Benchmark.Definition, OptionValues());
        UnavailableText = u is null ? null : Loc.Get(u.ReasonKey);
        if (u is not null) IsSelected = false;
    }
    partial void OnIsSelectedChanged(bool value) { if (value && !IsAvailable) IsSelected = false; }
    public IBenchmark Benchmark { get; }
    public string Name => Loc.Get(Benchmark.Definition.NameKey);
    public IReadOnlyList<TestOptionViewModel> Options { get; }
    public bool HasOptions => Options.Count > 0;
    public ObservableCollection<MetricRow> Metrics { get; } = [];
    /// <summary>Ticked for the next queue.</summary>
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _durationText;
    [ObservableProperty] private double _percentComplete;
    [ObservableProperty] private string _statusText = "";
    /// <summary>Running now (the row is highlighted).</summary>
    [ObservableProperty] private bool _isActive;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDetail))] private string? _detail;
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    /// <summary>The chosen length in seconds, or null (with the row showing why) when it is not a whole number in range.</summary>
    internal int? Seconds()
    {
        if (PersianDigits.TryParseInt(DurationText, out int seconds) && seconds is >= BenchmarksViewModel.MinSeconds and <= BenchmarksViewModel.MaxSeconds) return seconds;
        StatusText = Loc.Format("Bench_Invalid_Duration", BenchmarksViewModel.MinSeconds, BenchmarksViewModel.MaxSeconds);
        return null;
    }
    internal IReadOnlyDictionary<string, string> OptionValues() => Options.ToDictionary(o => o.Option.Key, o => o.Value);
}

/// <summary>The Benchmarks page: one row per benchmark, showing what <see cref="BenchmarkRunner"/> holds - the run in progress and the last
/// result of each - so leaving the page neither stops a run nor loses its numbers. Numbers only: no score, no pass or fail. Rows can be
/// ticked and run as a queue, one after another, like the Test Center's tests.</summary>
public sealed partial class BenchmarksViewModel : ObservableObject, IDisposable
{
    public const int MinSeconds = 4, MaxSeconds = 3600;
    private readonly BenchmarkRunner _runner; private readonly Func<Action, object> _dispatch;

    public ObservableCollection<BenchmarkRowViewModel> Rows { get; }
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(RunSelectedCommand), nameof(CancelCommand))] private bool _isRunning;
    /// <summary>"2 of 5" while a queue runs, empty otherwise.</summary>
    [ObservableProperty] private string _queueText = "";
    private bool CanRun() => !IsRunning;

    /// <summary>All benchmarks, or with <paramref name="component"/> only that part's (the CPU, GPU, Storage and Network pages). Only one
    /// benchmark runs at a time anywhere, so Run is disabled while any is running.</summary>
    public BenchmarksViewModel(BenchmarkRunner runner, Func<Action, object> dispatch, HardwareKind? component = null)
    {
        _runner = runner; _dispatch = dispatch; IsRunning = runner.IsBusy;
        Rows = [.. runner.Benchmarks.Where(b => component is null || b.Component == component).Select(b => new BenchmarkRowViewModel(b))];
        foreach (var row in Rows)
        {
            if (runner.Last(row.Benchmark.Definition.Id) is { } last) Show(row, last);
            if (runner.Running == row.Benchmark.Definition.Id) { row.StatusText = Loc.Get("Test_Status_Running"); row.IsActive = true; }
            row.PropertyChanged += OnRowChanged;
        }
        runner.Progress += OnProgress; runner.Finished += OnFinished; runner.QueueAdvanced += OnQueueAdvanced; runner.QueueFinished += OnQueueFinished;
    }

    private BenchmarkRowViewModel? Row(TestId id) => Rows.FirstOrDefault(r => r.Benchmark.Definition.Id == id);   // null: another page's benchmark
    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(BenchmarkRowViewModel.IsSelected)) RunSelectedCommand.NotifyCanExecuteChanged(); }
    private void OnProgress(TestId id, double fraction) => _dispatch(() => { if (Row(id) is { } row) row.PercentComplete = fraction * 100; });
    private void OnFinished(RecordedBenchmark run) => _dispatch(() => { if (Row(run.Definition.Id) is { } row) { Show(row, run.Result); row.IsActive = false; } IsRunning = _runner.IsBusy; if (!IsRunning) foreach (var r in Rows) r.RefreshAvailability(); });
    private void OnQueueAdvanced(TestId id, int index, int count) => _dispatch(() =>
    {
        QueueText = Loc.Format("Bench_Queue_Position", index + 1, count);
        if (Row(id) is { } row) { row.IsActive = true; row.Metrics.Clear(); row.Detail = null; row.PercentComplete = 0; row.StatusText = Loc.Get("Test_Status_Running"); }
    });
    private void OnQueueFinished(IReadOnlyList<RecordedBenchmark> runs) => _dispatch(() =>
    {
        QueueText = ""; IsRunning = _runner.IsBusy;
        foreach (var row in Rows.Where(r => r.StatusText == Loc.Get("Bench_Status_Queued"))) row.StatusText = Loc.Get("Bench_Status_Skipped");   // cancelled before its turn
    });

    private static void Show(BenchmarkRowViewModel row, BenchmarkResult result)
    {
        row.Metrics.Clear();
        foreach (var m in result.Metrics) row.Metrics.Add(new(Loc.Get(m.Key), Units.FormatMeasured(m.Value, m.Unit)));
        foreach (var s in result.Setup ?? []) row.Metrics.Add(new(Loc.Get(s.Key), s.Value));   // how the run was set up: its length, version, resolution
        row.Detail = result.Detail;
        if (result.Status == BenchmarkStatus.Completed) { row.StatusText = Loc.Format("Bench_Status_CompletedAt", result.FinishedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)); row.PercentComplete = 100; }
        else { row.StatusText = Loc.Get("Bench_Status_" + result.Status); row.PercentComplete = 0; }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Run(BenchmarkRowViewModel row)
    {
        if (!row.IsAvailable || row.Seconds() is not { } seconds) return;
        IsRunning = true; row.Metrics.Clear(); row.Detail = null; row.PercentComplete = 0; row.StatusText = Loc.Get("Test_Status_Running"); row.IsActive = true;
        // The result arrives through Finished; null means another run was already going.
        if (await _runner.RunAsync(row.Benchmark, seconds, row.OptionValues()).ConfigureAwait(true) is null)
        { IsRunning = _runner.IsBusy; row.IsActive = false; row.StatusText = _runner.BlockedBy is { } h ? Loc.Get($"Workload_Busy_{h}") : ""; }
    }

    private bool CanRunSelected() => !IsRunning && Rows.Any(r => r.IsSelected);

    /// <summary>The ticked benchmarks, top to bottom, one after another. Every ticked row's length is checked first: one bad field starts nothing.</summary>
    [RelayCommand(CanExecute = nameof(CanRunSelected))]
    private async Task RunSelected()
    {
        var jobs = new List<BenchmarkJob>(); bool valid = true;
        foreach (var row in Rows.Where(r => r.IsSelected))
        {
            if (row.Seconds() is { } seconds) jobs.Add(new(row.Benchmark, seconds, row.OptionValues())); else valid = false;
        }
        if (!valid || jobs.Count == 0) return;
        IsRunning = true;
        foreach (var row in Rows.Where(r => r.IsSelected)) { row.StatusText = Loc.Get("Bench_Status_Queued"); row.PercentComplete = 0; }
        if ((await _runner.RunQueueAsync(jobs).ConfigureAwait(true)).Count == 0)   // refused: something else was running
        {
            IsRunning = _runner.IsBusy;
            foreach (var row in Rows.Where(r => r.IsSelected)) row.StatusText = _runner.BlockedBy is { } h ? Loc.Get($"Workload_Busy_{h}") : "";
        }
    }

    [RelayCommand] private void SelectAll() { foreach (var row in Rows.Where(r => r.IsAvailable)) row.IsSelected = true; }
    [RelayCommand] private void ClearSelection() { foreach (var row in Rows) row.IsSelected = false; }

    [RelayCommand(CanExecute = nameof(IsRunning))] private void Cancel() => _runner.Cancel();

    public void Dispose()
    {
        foreach (var row in Rows) row.PropertyChanged -= OnRowChanged;
        _runner.Progress -= OnProgress; _runner.Finished -= OnFinished; _runner.QueueAdvanced -= OnQueueAdvanced; _runner.QueueFinished -= OnQueueFinished;
    }
}
