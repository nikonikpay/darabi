using System.Collections.ObjectModel; using System.Globalization; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input;
using Mazesta.Core.Hardware; using Mazesta.Core.Text; using Mazesta.Core.Time; using Mazesta.Desktop.Localization; using Mazesta.Desktop.Services; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Monitoring;
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

/// <summary>Runs one benchmark at a time and lists what it measured. Numbers only: no score, no pass or fail. The most recent
/// completed run of each is handed to <see cref="BenchmarkResults"/> so the next test report can include it.</summary>
public sealed partial class BenchmarksViewModel : ObservableObject, IDisposable
{
    public const int MinSeconds = 4, MaxSeconds = 3600;
    private readonly PollingEngine _engine; private readonly IClock _clock; private readonly BenchmarkResults _results; private readonly Func<Action, object> _dispatch;
    private CancellationTokenSource? _cts;

    public ObservableCollection<BenchmarkRowViewModel> Rows { get; }
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RunCommand))] private bool _isRunning;
    private bool CanRun() => !IsRunning;

    public BenchmarksViewModel(IEnumerable<IBenchmark> benchmarks, PollingEngine engine, IClock clock, BenchmarkResults results, Func<Action, object> dispatch)
    {
        _engine = engine; _clock = clock; _results = results; _dispatch = dispatch;
        Rows = [.. benchmarks.Select(b => new BenchmarkRowViewModel(b))];
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Run(BenchmarkRowViewModel row)
    {
        if (!PersianDigits.TryParseInt(row.DurationText, out int seconds) || seconds is < MinSeconds or > MaxSeconds) { row.StatusText = Loc.Format("Bench_Invalid_Duration", MinSeconds, MaxSeconds); return; }
        var chosen = row.Options.ToDictionary(o => o.Option.Key, o => o.Value);
        var request = new TestExecutionRequest(seconds, _clock, p => _dispatch(() => { row.PercentComplete = p.PercentComplete * 100; }), _engine, new TestOptions(row.Benchmark.Definition, chosen));
        _cts = new CancellationTokenSource(); IsRunning = true; row.Metrics.Clear(); row.Detail = null; row.PercentComplete = 0; row.StatusText = Loc.Get("Test_Status_Running");
        try
        {
            var result = await row.Benchmark.RunAsync(request, _cts.Token).ConfigureAwait(true);
            _results.Record(row.Benchmark.Definition, result);
            foreach (var m in result.Metrics) row.Metrics.Add(new(Loc.Get(m.Key), Units.FormatMeasured(m.Value, m.Unit)));
            row.Detail = result.Detail; row.StatusText = Loc.Get("Bench_Status_" + result.Status); row.PercentComplete = result.Status == BenchmarkStatus.Completed ? 100 : 0;
        }
        catch (Exception e) { row.StatusText = Loc.Get("Bench_Status_Failed"); row.Detail = e.Message; }
        finally { _cts.Dispose(); _cts = null; IsRunning = false; }
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();

    public void Dispose() => _cts?.Cancel();
}
