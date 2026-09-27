using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics.Benchmarks;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    private void RegisterBenchmarks()
    {
        var bench = _sp.GetRequiredService<Func<BenchmarksViewModel>>()();
        _cleanup.Add(bench.Dispose);
        object State() => new
        {
            running = bench.IsRunning, queue = bench.QueueText, canRunSelected = bench.RunSelectedCommand.CanExecute(null),
            rows = bench.Rows.Select(r => new
            {
                id = r.Benchmark.Definition.Id.Value, name = r.Name, component = r.Benchmark.Component.ToString(), selected = r.IsSelected, duration = r.DurationText,
                percent = r.PercentComplete, status = r.StatusText, active = r.IsActive, detail = r.Detail,
                options = r.Options.Select(Option), metrics = r.Metrics.Select(m => new { name = m.Name, value = m.Value }),
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
}
