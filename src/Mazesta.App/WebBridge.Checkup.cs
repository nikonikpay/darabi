using Mazesta.Core.Health.Checkup; using Mazesta.Desktop.Services; using Mazesta.Desktop.ViewModels; using Mazesta.Desktop.Localization;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>The benchmarks the checkup runs, in order: the CPU on all cores, then on one, the memory, then the graphics card.</summary>
    private static readonly string[] CheckupBenchmarks = ["bench.cpu.multi", "bench.cpu.single", "bench.memory", "bench.gpu.d3d"];
    /// <summary>The Benchmarks page's view model, which the checkup drives so a run shows on that page as any other would.</summary>
    private BenchmarksViewModel? _benchVm;

    private void RegisterCheckup()
    {
        var checkup = _sp.GetRequiredService<CheckupService>();
        object State() => new
        {
            running = _benchVm?.IsRunning ?? false,
            runs = checkup.Runs().Select(r => new { id = r.Id, name = Loc.Get(r.NameKey), at = r.At.ToLocalTime().ToString("HH:mm", Loc.Culture), findings = r.All.Select(FindingJson) }),
        };
        void OnChanged() => PushSoon("checkup", State);
        checkup.Changed += OnChanged; _cleanup.Add(() => checkup.Changed -= OnChanged);
        if (_benchVm is { } vm)
        {
            void OnVm(object? s, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(BenchmarksViewModel.IsRunning)) PushSoon("checkup", State); }
            vm.PropertyChanged += OnVm; _cleanup.Add(() => vm.PropertyChanged -= OnVm);
        }

        Method("checkup.state", _ => State());
        // The setup is read apart: it waits for the hardware details, which the first seconds after start-up may still be reading.
        MethodAsync("checkup.setup", async _ => (await checkup.SetupAsync()).Select(FindingJson).ToList());
        // Ticks the checkup's benchmarks (those this machine can run) and runs them as a queue; the call returns as soon as the queue starts.
        Method("checkup.run", p =>
        {
            if (_benchVm is not { } bench || bench.IsRunning) return false;
            foreach (var row in bench.Rows) row.IsSelected = CheckupBenchmarks.Contains(row.Benchmark.Definition.Id.Value) && row.IsAvailable;
            if (!bench.RunSelectedCommand.CanExecute(null)) return false;
            _ = bench.RunSelectedCommand.ExecuteAsync(null);
            return true;
        });
    }

    internal static object FindingJson(Finding f) => new
    {
        level = f.Level.ToString(), levelName = CheckupText.Level(f.Level), part = f.Part.ToString(), title = CheckupText.Title(f), text = CheckupText.Text(f), hint = CheckupText.Hint(f), subject = f.Subject,
        measures = f.Measures.Select(m => new { name = Loc.Get(m.Key), value = CheckupText.Value(m) }), source = f.Source,
    };
}
