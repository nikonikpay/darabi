using System.Text.Json; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.Web;

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
        void OnLogged(TestLogEntry e) => Push("testlog", LogLine(e));
        engine.Logged += OnLogged; _cleanup.Add(() => engine.Logged -= OnLogged);
        Method("tests.log", _ => engine.RecentLog().Select(LogLine));
        object State() => new
        {
            running = tests.IsRunning, together = tests.Together, current = Current(), profileNote = tests.ProfileNote,
            profiles = TestProfiles.All.Select(p => new { id = p.Id, name = Loc.Get(p.NameKey) }), canStart = tests.StartCommand.CanExecute(null), incomplete = tests.IncompleteSessionMessage, blocked = tests.BlockedMessage,
            repeatModes = TestQueueRowViewModel.RepeatModes.Select(m => new { value = m.ToString(), label = Loc.Get($"Test_Repeat_{m}") }),
            rows = tests.Rows.Select(r => new
            {
                id = r.Definition.Id.Value, name = r.Name, selected = r.IsSelected, duration = r.DurationText, repeat = r.Repeat.ToString(), count = r.RepeatCountText,
                options = r.Options.Select(Option), error = r.ValidationError, outcome = r.Outcome.ToString(), outcomeText = r.OutcomeText,
                percent = r.PercentComplete, status = r.StatusText, errors = r.HasErrors ? r.ErrorsText : null, detail = r.Detail, advice = r.Advice, unavailable = r.UnavailableText,
            }),
        };
        Mirror("tests", tests, State, tests.Rows);
        foreach (var o in tests.Rows.SelectMany(r => r.Options)) o.PropertyChanged += (_, _) => PushSoon("tests", State);   // the rows' options are not in a collection the mirror sees

        Method("tests.state", _ => State());
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
        choices = o.IsChoice ? o.Choices.Select(c => new { value = c.Value, label = c.Label }) : null,
    };

    private static void SetOption(IReadOnlyList<TestOptionViewModel> options, string key, string value)
    {
        var o = options.FirstOrDefault(x => x.Option.Key == key) ?? throw new ArgumentException("unknown option");
        if (o.IsChoice) o.SelectedChoice = o.Choices.FirstOrDefault(c => c.Value == value); else o.Text = value;
    }
}
