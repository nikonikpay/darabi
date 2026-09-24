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
    }
    public IBenchmark Benchmark { get; }
    public string Name => Loc.Get(Benchmark.Definition.NameKey);
    public IReadOnlyList<TestOptionViewModel> Options { get; }
    public bool HasOptions => Options.Count > 0;
    public ObservableCollection<MetricRow> Metrics { get; } = [];
    [ObservableProperty] private string _durationText;
    [ObservableProperty] private double _percentComplete;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDetail))] private string? _detail;
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}

/// <summary>The Benchmarks page: one row per benchmark, showing what <see cref="BenchmarkRunner"/> holds - the run in progress and the last
/// result of each - so leaving the page neither stops a run nor loses its numbers. Numbers only: no score, no pass or fail.</summary>
public sealed partial class BenchmarksViewModel : ObservableObject, IDisposable
{
    public const int MinSeconds = 4, MaxSeconds = 3600;
    private readonly BenchmarkRunner _runner; private readonly Func<Action, object> _dispatch;

    public ObservableCollection<BenchmarkRowViewModel> Rows { get; }
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RunCommand))] private bool _isRunning;
    private bool CanRun() => !IsRunning;

    public BenchmarksViewModel(BenchmarkRunner runner, Func<Action, object> dispatch)
    {
        _runner = runner; _dispatch = dispatch;
        Rows = [.. runner.Benchmarks.Select(b => new BenchmarkRowViewModel(b))];
        foreach (var row in Rows)
        {
            if (runner.Last(row.Benchmark.Definition.Id) is { } last) Show(row, last);
            if (runner.Running == row.Benchmark.Definition.Id) { row.StatusText = Loc.Get("Test_Status_Running"); IsRunning = true; }
        }
        runner.Progress += OnProgress; runner.Finished += OnFinished;
    }

    private BenchmarkRowViewModel Row(TestId id) => Rows.First(r => r.Benchmark.Definition.Id == id);
    private void OnProgress(TestId id, double fraction) => _dispatch(() => Row(id).PercentComplete = fraction * 100);
    private void OnFinished(RecordedBenchmark run) => _dispatch(() => { Show(Row(run.Definition.Id), run.Result); IsRunning = false; });

    private static void Show(BenchmarkRowViewModel row, BenchmarkResult result)
    {
        row.Metrics.Clear();
        foreach (var m in result.Metrics) row.Metrics.Add(new(Loc.Get(m.Key), Units.FormatMeasured(m.Value, m.Unit)));
        row.Detail = result.Detail;
        if (result.Status == BenchmarkStatus.Completed) { row.StatusText = Loc.Format("Bench_Status_CompletedAt", result.FinishedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)); row.PercentComplete = 100; }
        else { row.StatusText = Loc.Get("Bench_Status_" + result.Status); row.PercentComplete = 0; }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Run(BenchmarkRowViewModel row)
    {
        if (!PersianDigits.TryParseInt(row.DurationText, out int seconds) || seconds is < MinSeconds or > MaxSeconds) { row.StatusText = Loc.Format("Bench_Invalid_Duration", MinSeconds, MaxSeconds); return; }
        IsRunning = true; row.Metrics.Clear(); row.Detail = null; row.PercentComplete = 0; row.StatusText = Loc.Get("Test_Status_Running");
        // The result arrives through Finished; null means another run was already going.
        if (await _runner.RunAsync(row.Benchmark, seconds, row.Options.ToDictionary(o => o.Option.Key, o => o.Value)).ConfigureAwait(true) is null) IsRunning = _runner.Running is not null;
    }

    [RelayCommand] private void Cancel() => _runner.Cancel();

    public void Dispose() { _runner.Progress -= OnProgress; _runner.Finished -= OnFinished; }
}
