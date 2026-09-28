using System.Globalization;
using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Monitoring;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    private void RegisterBenchmarks()
    {
        var bench = _sp.GetRequiredService<Func<BenchmarksViewModel>>()();
        _cleanup.Add(bench.Dispose);
        // The best result of each benchmark on this machine, kept across runs and restarts; a run is compared with it when it finishes.
        var records = new BenchmarkRecords(_paths.DataRoot); var runner = _sp.GetRequiredService<BenchmarkRunner>(); var engine = _sp.GetRequiredService<PollingEngine>();
        var (system, systemName) = SystemKey(engine);
        var compared = new Dictionary<string, BenchmarkComparison?>();
        string Key(BenchmarkRowViewModel r) => BenchmarkRecords.RecordKey(r.Benchmark.Definition.Id.Value, r.OptionValues());
        void OnFinished(RecordedBenchmark run) => _window.Dispatcher.BeginInvoke(() =>
        {
            // The options are read from the row as it is now: they cannot be changed while its run is going.
            var row = bench.Rows.FirstOrDefault(r => r.Benchmark.Definition.Id == run.Definition.Id);
            if (row is null) return;
            var c = records.Offer(system, systemName, Key(row), run.Result);
            compared[run.Definition.Id.Value] = c;
            if (c is { Saved: false }) _log.LogInformation("Benchmark {Id}: {Value} is below the record {Best}; not kept", run.Definition.Id.Value, c.Current.Value, c.Previous?.Value);
            PushSoon("bench", State);
        });
        runner.Finished += OnFinished; _cleanup.Add(() => runner.Finished -= OnFinished);
        object? Best(BenchmarkRowViewModel r) => records.Best(system, Key(r)) is { } b ? Record(b) : null;
        object? Compared(BenchmarkRowViewModel r) => compared.GetValueOrDefault(r.Benchmark.Definition.Id.Value) is { } c
            ? new { now = Record(c.Current), previous = c.Previous is { } p ? Record(p) : null, change = c.ChangePercent, saved = c.Saved } : null;
        object State() => new
        {
            running = bench.IsRunning, queue = bench.QueueText, canRunSelected = bench.RunSelectedCommand.CanExecute(null),
            rows = bench.Rows.Select(r => new
            {
                id = r.Benchmark.Definition.Id.Value, name = r.Name, component = r.Benchmark.Component.ToString(), selected = r.IsSelected, duration = r.DurationText,
                percent = r.PercentComplete, status = r.StatusText, active = r.IsActive, detail = r.Detail, unavailable = r.UnavailableText,
                options = r.Options.Select(Option), metrics = r.Metrics.Select(m => new { name = m.Name, value = m.Value }),
                best = Best(r), compared = Compared(r),
            }),
        };
        Mirror("bench", bench, State, [bench.Rows, .. bench.Rows.Select(r => r.Metrics)]);
        foreach (var o in bench.Rows.SelectMany(r => r.Options)) o.PropertyChanged += (_, _) => PushSoon("bench", State);

        BenchmarkRowViewModel Row(System.Text.Json.JsonElement p) => bench.Rows.FirstOrDefault(r => r.Benchmark.Definition.Id.Value == Str(p, "id")) ?? throw new ArgumentException("unknown benchmark");
        Method("bench.state", _ => State());
        Method("bench.set", p =>
        {
            var row = Row(p);
            switch (Str(p, "field"))
            {
                case "selected": row.IsSelected = Bool(p, "value"); break;
                case "duration": row.DurationText = Str(p, "value"); break;
                case "option": SetOption(row.Options, Str(p, "key"), Str(p, "value")); break;
                default: throw new ArgumentException("unknown field");
            }
            return null;
        });
        MethodAsync("bench.exec", async p =>
        {
            switch (Str(p, "cmd"))
            {
                case "run": var row = Row(p); if (bench.RunCommand.CanExecute(row)) await bench.RunCommand.ExecuteAsync(row); break;
                case "runSelected": if (bench.RunSelectedCommand.CanExecute(null)) await bench.RunSelectedCommand.ExecuteAsync(null); break;
                case "cancel": if (bench.CancelCommand.CanExecute(null)) bench.CancelCommand.Execute(null); break;
                case "selectAll": bench.SelectAllCommand.Execute(null); break;
                case "clear": bench.ClearSelectionCommand.Execute(null); break;
                default: throw new ArgumentException("unknown command");
            }
            return null;
        });
    }

    private static object Record(BenchmarkRecord r) => new
    {
        name = Loc.Get(r.Key), value = Units.FormatMeasured(r.Value, r.Unit), raw = r.Value,
        at = r.At.ToLocalTime().ToString("yyyy/MM/dd HH:mm", Loc.Culture),
    };

    /// <summary>What "this system" means for the records: the machine's name with its CPU and GPUs, so a portable copy carried from machine to
    /// machine keeps each one's records apart, and a changed CPU or GPU starts fresh ones.</summary>
    private static (string Key, string Name) SystemKey(PollingEngine engine)
    {
        string cpu = engine.Hardware.FirstOrDefault(n => n.Kind == HardwareKind.Cpu && n.ParentId is null)?.Name ?? "";
        var gpus = engine.Hardware.Where(n => n.Kind == HardwareKind.Gpu && n.ParentId is null).Select(n => n.Name).Order(StringComparer.Ordinal);
        string key = string.Join(" | ", [Environment.MachineName, cpu, .. gpus]);
        return (key, string.Join(" · ", new[] { Environment.MachineName, cpu }.Where(x => x.Length > 0)));
    }
}
