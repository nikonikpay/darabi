using Mazesta.Core.Health.Checkup; using Mazesta.Desktop.Services; using Mazesta.Desktop.ViewModels; using Mazesta.Desktop.Localization;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>The diagnosis is made from the Tests page's own tests, a short sample of each part: the processor under full load (its three
    /// stages, half a minute each) and core by core, the memory, the graphics card at a steady full load and its memory, the system drive, and
    /// the drives' own health record. One to two minutes each, so the whole of it is a matter of minutes, not of an afternoon. The network is
    /// left out: it measures the line as much as the computer.</summary>
    internal static readonly (string Id, int Seconds, (string Key, string Value)[] Options)[] CheckupTests =
    [
        ("cpu.stress", 90, [("stageSeconds", "30"), ("pattern", "steady")]), ("cpu.singlecore", 60, [("cores", "1"), ("secondsPerCore", "5")]), ("memory.pattern", 90, [("sizeMb", "8192")]),
        ("gpu.steady", 90, []), ("gpu.vram", 60, []), ("storage.sequential", 60, []), ("storage.smart", 5, []),
    ];
    /// <summary>Whether the diagnosis' own queue is the one running (the Tests page's queue, borrowed: its settings are put back afterwards).</summary>
    private bool _checkupRunning;

    /// <summary>Runs the diagnosis' tests through the Tests page's queue, on the UI thread: the rows are set, the queue runs to its end, and the
    /// page's own selection, lengths and options are put back however it ended. False when it could not start (something else is running).
    /// <paramref name="keep"/> leaves tests out (the assistant's user may untick some).</summary>
    internal async Task<bool> RunCheckupTests(Func<string, bool>? keep = null)
    {
        if (_testVm is not { } tests || tests.IsRunning || _checkupRunning) return false;
        var before = tests.Rows.Select(RowState.Of).ToList(); bool wasTogether = tests.Together;
        _checkupRunning = true;
        try
        {
            tests.Together = false;
            foreach (var r in tests.Rows)
            {
                var want = CheckupTests.FirstOrDefault(x => x.Id == r.Definition.Id.Value);
                r.IsSelected = want.Id is not null && r.IsAvailable && (keep?.Invoke(want.Id) ?? true);
                if (!r.IsSelected) continue;
                r.DurationText = want.Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture); r.Repeat = Diagnostics.RepeatMode.Once; r.RepeatCountText = "1";
                foreach (var (key, value) in want.Options) if (r.Options.FirstOrDefault(o => o.Option.Key == key) is { } o) RowState.Set(o, value);
            }
            if (!tests.StartCommand.CanExecute(null)) return false;
            await tests.StartCommand.ExecuteAsync(null);
            return tests.BlockedMessage is null;
        }
        finally
        {
            _checkupRunning = false;
            tests.Together = wasTogether;
            foreach (var s in before) if (tests.Rows.FirstOrDefault(r => r.Definition.Id.Value == s.Id) is { } r) s.Restore(r);
            PushSoon("checkup", () => CheckupState());
        }
    }
    private Func<object> CheckupState = () => new { };

    /// <summary>The benchmarks the assistant's older diagnosis ran; kept for the benchmark runs the page still judges when they are run.</summary>
    private static readonly string[] CheckupBenchmarks = ["bench.cpu.multi", "bench.cpu.single", "bench.memory", "bench.gpu.d3d"];
    /// <summary>The Benchmarks page's view model, which the checkup drives so a run shows on that page as any other would.</summary>
    private BenchmarksViewModel? _benchVm;

    private void RegisterCheckup()
    {
        var checkup = _sp.GetRequiredService<CheckupService>();
        object TestJson(TestCheck c) => new
        {
            id = c.Id, name = Loc.Get(c.NameKey), at = c.At.ToLocalTime().ToString("HH:mm", Loc.Culture), outcome = c.Outcome.ToString(), outcomeText = Loc.Get($"Test_Outcome_{c.Outcome}"), findings = c.Findings.Select(FindingJson),
        };
        object State() => new
        {
            running = (_benchVm?.IsRunning ?? false) || (_testVm?.IsRunning ?? false), own = _checkupRunning,
            plan = CheckupTests.Select(x => _testVm?.Rows.FirstOrDefault(r => r.Definition.Id.Value == x.Id)).OfType<TestQueueRowViewModel>().Where(r => r.IsAvailable).Select(r => r.Name),
            minutes = Math.Round(CheckupTests.Sum(x => x.Seconds) / 60.0),
            tests = checkup.TestRuns().Select(TestJson),
            runs = checkup.Runs().Select(r => new { id = r.Id, name = Loc.Get(r.NameKey), at = r.At.ToLocalTime().ToString("HH:mm", Loc.Culture), findings = r.All.Select(FindingJson) }),
        };
        CheckupState = State;
        void OnChanged() => PushSoon("checkup", State);
        checkup.Changed += OnChanged; _cleanup.Add(() => checkup.Changed -= OnChanged);
        if (_testVm is { } tvm)
        {
            void OnTests(object? s, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(TestCenterViewModel.IsRunning)) PushSoon("checkup", State); }
            tvm.PropertyChanged += OnTests; _cleanup.Add(() => tvm.PropertyChanged -= OnTests);
        }
        if (_benchVm is { } vm)
        {
            void OnVm(object? s, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(BenchmarksViewModel.IsRunning)) PushSoon("checkup", State); }
            vm.PropertyChanged += OnVm; _cleanup.Add(() => vm.PropertyChanged -= OnVm);
        }

        Method("checkup.state", _ => State());
        // The setup is read apart: it waits for the hardware details, which the first seconds after start-up may still be reading.
        MethodAsync("checkup.setup", async _ => (await checkup.SetupAsync()).Select(FindingJson).ToList());
        // Runs the diagnosis' tests as a queue of the Tests page; the call returns as soon as the queue has started (or could not).
        Method("checkup.run", p =>
        {
            if (_benchVm?.IsRunning == true) return false;
            var run = RunCheckupTests();
            return !run.IsCompleted || run.Result;
        });
    }

    internal static object FindingJson(Finding f) => new
    {
        level = f.Level.ToString(), levelName = CheckupText.Level(f.Level), part = f.Part.ToString(), title = CheckupText.Title(f), text = CheckupText.Text(f), hint = CheckupText.Hint(f), subject = f.Subject,
        measures = f.Measures.Select(m => new { name = Loc.Get(m.Key), value = CheckupText.Value(m) }), source = f.Source,
    };
}
