using Microsoft.Extensions.Logging;
using System.Text.Json; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    private void RegisterTests()
    {
        // One Test Center for the session (the engine is a singleton): the page is rebuilt on every visit, its state is not.
        var tests = _testVm = _sp.GetRequiredService<Func<TestCenterViewModel>>()();
        _cleanup.Add(tests.Dispose);
        var engine = _sp.GetRequiredService<TestEngine>();
        // The live monitor's "now": which test of the queue is running, how far it is, and since when.
        // Side by side, "now" is every test under way: their names together, and the progress of the one furthest behind.
        object? Current() => tests.RunningRows is { Count: > 1 } live ? new
        {
            id = live[0].Definition.Id.Value, name = string.Join(" + ", live.Select(x => x.Name)), index = tests.CurrentIndex + 1, total = tests.RunQueue.Count, percent = live.Min(x => x.PercentComplete),
            status = live[0].StatusText, outcome = live[0].Outcome.ToString(), outcomeText = live[0].OutcomeText, startedAt = tests.CurrentStartedAt?.ToUnixTimeMilliseconds(), parallel = (bool?)true,
        } : tests.CurrentRow is { } r ? new
        {
            id = r.Definition.Id.Value, name = r.Name, index = tests.CurrentIndex + 1, total = tests.RunQueue.Count, percent = r.PercentComplete, status = r.StatusText,
            outcome = r.Outcome.ToString(), outcomeText = r.OutcomeText, startedAt = tests.CurrentStartedAt?.ToUnixTimeMilliseconds(), parallel = (bool?)null,
        } : null;
        // A log line as key and arguments: the page words it in its language (numbers in its digits); '@' arguments are keys themselves (a
        // test's name, an outcome). The formula or command stays exactly as written.
        static object LogLine(TestLogEntry e) => new
        {
            at = e.At.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), test = e.Test?.Value, level = e.Level.ToString(),
            key = e.Key, args = e.Args, formula = e.Formula,
        };
        // The live log is kept in memory: the lines that say where a session is (a test begins or ends, a stage begins) also go to the
        // app's log file, which is still there after a reset.
        void OnLogged(TestLogEntry e)
        {
            Push("testlog", LogLine(e));
            if (e.Level != TestLogLevel.Step || e.Key.EndsWith("_Stage", StringComparison.Ordinal)) _log.LogInformation("Test log: {Test} {Key} [{Args}]", e.Test?.Value, e.Key, string.Join(", ", e.Args));
        }
        engine.Logged += OnLogged; _cleanup.Add(() => engine.Logged -= OnLogged);
        Method("tests.log", _ => engine.RecentLog().Select(LogLine));
        var checkupService = _sp.GetRequiredService<Desktop.Services.CheckupService>();
        var checks = new Dictionary<string, object[]>();
        void ReadChecks() { checks = checkupService.TestRuns().Where(c => c.Findings.Count > 0).ToDictionary(c => c.Id, c => c.Findings.Select(FindingJson).ToArray()); }
        ReadChecks();
        static Desktop.Services.TestDetailText.DetailView View(string? detail) => Desktop.Services.TestDetailText.View(detail);
        object State() => new
        {
            running = tests.IsRunning, together = tests.Together, current = Current(), profileNote = tests.ProfileNote,
            profiles = TestProfiles.All.Select(p => new { id = p.Id, name = Loc.Get(p.NameKey), minutes = (int)Math.Round(p.Tests.Sum(x => x.Seconds) / 60.0) }), canStart = tests.StartCommand.CanExecute(null), incomplete = tests.IncompleteSessionMessage, blocked = tests.BlockedMessage,
            repeatModes = TestQueueRowViewModel.RepeatModes.Select(m => new { value = m.ToString(), label = Loc.Get($"Test_Repeat_{m}") }),
            rows = tests.Rows.Select(r => new
            {
                id = r.Definition.Id.Value, name = r.Name, selected = r.IsSelected, duration = r.DurationText, repeat = r.Repeat.ToString(), count = r.RepeatCountText,
                options = r.Options.Select(Option), error = r.ValidationError, outcome = r.Outcome.ToString(), outcomeText = r.OutcomeText,
                percent = r.PercentComplete, status = r.StatusText, errors = r.HasErrors ? r.ErrorsText : null, detail = r.Detail, figures = View(r.Detail).Figures.Select(f => new { name = f.Name, value = f.Value, note = f.Note }), detailLines = View(r.Detail).Lines.Select(l => new { text = l.Text, lat = l.Latin }), advice = r.Advice, unavailable = r.UnavailableText,
                // What the monitor's record of this test's last run says about the part it loaded (the diagnosis' rules), once the session has ended.
                checkup = !tests.IsRunning && checks.TryGetValue(r.Definition.Id.Value, out var found) && r.Outcome is not (TestOutcome.NotRun or TestOutcome.Running) ? found : null,
            }),
        };
        Mirror("tests", tests, State, tests.Rows);
        void OnChecked() { ReadChecks(); PushSoon("tests", State); }
        checkupService.Changed += OnChecked; _cleanup.Add(() => checkupService.Changed -= OnChecked);
        void OnJudged(Desktop.Services.CheckupService.SessionJudgment j) => Push("testdone", SessionVerdict(j));
        checkupService.SessionJudged += OnJudged; _cleanup.Add(() => checkupService.SessionJudged -= OnJudged);
        foreach (var o in tests.Rows.SelectMany(r => r.Options)) o.PropertyChanged += (_, _) => PushSoon("tests", State);   // the rows' options are not in a collection the mirror sees

        Method("tests.state", _ => { if (!tests.IsRunning) foreach (var r in tests.Rows) r.RefreshAvailability(); return State(); });   // (asked again: a card back from a driver reset is runnable again)
        Method("tests.set", p =>
        {
            var row = tests.Rows.FirstOrDefault(r => r.Definition.Id.Value == Str(p, "id")) ?? throw new ArgumentException("unknown test");
            string value = Str(p, "value");
            switch (Str(p, "field"))
            {
                case "selected": row.IsSelected = Bool(p, "value"); break;
                case "duration": row.DurationText = value; break;
                case "repeat": row.Repeat = Enum.Parse<RepeatMode>(value); break;
                case "count": row.RepeatCountText = value; break;
                case "option": SetOption(row.Options, Str(p, "key"), value); break;
                default: throw new ArgumentException("unknown field");
            }
            return null;
        });
        MethodAsync("tests.exec", async p =>
        {
            switch (Str(p, "cmd"))
            {
                case "start": if (tests.StartCommand.CanExecute(null)) await tests.StartCommand.ExecuteAsync(null); break;
                case "cancel": if (tests.CancelCommand.CanExecute(null)) tests.CancelCommand.Execute(null); break;
                case "selectAll": tests.SelectAllCommand.Execute(null); break;
                case "durationAll": if (!tests.IsRunning) tests.SetAllDurations(Str(p, "value")); break;
                case "clear": tests.ClearSelectionCommand.Execute(null); break;
                case "dismissIncomplete": tests.DismissIncompleteSessionCommand.Execute(null); break;
                case "together": if (!tests.IsRunning) tests.Together = Bool(p, "value"); break;
                case "profile": if (!tests.IsRunning) tests.ApplyProfile(Str(p, "id")); break;
                default: throw new ArgumentException("unknown command");
            }
            return null;
        });
    }

    private static object Option(TestOptionViewModel o) => new
    {
        key = o.Option.Key, label = o.Label, kind = o.Option.Kind.ToString(), value = o.IsChoice ? o.SelectedChoice?.Value : o.Text,
        choices = o.IsChoice ? o.Choices.Select(c => new { value = c.Value, label = c.Label }) : null, when = o.Option.When,
    };

    private static void SetOption(IReadOnlyList<TestOptionViewModel> options, string key, string value)
    {
        var o = options.FirstOrDefault(x => x.Option.Key == key) ?? throw new ArgumentException("unknown option");
        if (o.IsChoice) o.SelectedChoice = o.Choices.FirstOrDefault(c => c.Value == value); else o.Text = value;
    }

    /// <summary>What the page's end-of-session window says: per part (processor, memory, graphics card, drives, the rest) how many of its tests passed,
    /// what failed, its hottest reading and load where they were judged, and the diagnosis' findings, with a level the worst of them sets.</summary>
    private static object SessionVerdict(Desktop.Services.CheckupService.SessionJudgment j)
    {
        static string PartOf(string id) => id.Split('.')[0] switch { "cpu" => "Cpu", "memory" => "Memory", "gpu" => "Gpu", "storage" => "Storage", _ => "System" };
        string Name(TestId id) => j.Queue.FirstOrDefault(q => q.Definition.Id == id)?.Definition.NameKey is { } k ? Loc.Get(k) : id.Value;
        var parts = j.Results.GroupBy(r => PartOf(r.Id.Value)).OrderBy(g => Array.IndexOf(new[] { "Cpu", "Memory", "Gpu", "Storage", "System" }, g.Key)).Select(g =>
        {
            var judged = j.Parts.FirstOrDefault(p => p.Part.ToString() == g.Key);
            var findings = judged?.Findings ?? [];
            int passed = g.Count(r => r.Outcome == TestOutcome.Passed), failed = g.Count(r => r.Outcome is TestOutcome.Failed or TestOutcome.Error);
            bool unsure = g.Any(r => r.Outcome is TestOutcome.Inconclusive or TestOutcome.Cancelled or TestOutcome.Unsupported);
            string level = failed > 0 || findings.Any(f => f.Level == Mazesta.Core.Health.Checkup.FindingLevel.Problem) ? "Problem"
                : unsure || findings.Any(f => f.Level == Mazesta.Core.Health.Checkup.FindingLevel.Attention) ? "Attention" : "Good";
            return new
            {
                // The sentence about heat and clocks only when the monitor's record of them was judged (a run too short, or no sensors: only the tests' word).
                part = g.Key, level, name = judged?.Name, say = level == "Good" && g.Key is "Cpu" or "Gpu" && judged is null ? "Done_Passed_Good" : $"Done_{g.Key}_{level}", passed, failed, total = g.Count(), errors = g.Sum(r => r.ErrorCount),
                tempMax = judged?.TempMaxC, load = judged?.LoadPercent,
                tests = g.Select(r => new { name = Name(r.Id), outcome = r.Outcome.ToString(), outcomeText = Loc.Get("Test_Outcome_" + r.Outcome) }),
                findings = findings.Select(FindingJson),
            };
        }).ToList();
        return new { parts, level = parts.Any(p => p.level == "Problem") ? "Problem" : parts.Any(p => p.level == "Attention") ? "Attention" : "Good" };
    }
}
