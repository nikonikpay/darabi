using System.Text.Json; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    private void RegisterTests()
    {
        // One Test Center for the session (the engine is a singleton): the page is rebuilt on every visit, its state is not.
        var tests = _sp.GetRequiredService<Func<TestCenterViewModel>>()();
        _cleanup.Add(tests.Dispose);
        object State() => new
        {
            running = tests.IsRunning, canStart = tests.StartCommand.CanExecute(null), incomplete = tests.IncompleteSessionMessage,
            repeatModes = TestQueueRowViewModel.RepeatModes.Select(m => new { value = m.ToString(), label = Loc.Get($"Test_Repeat_{m}") }),
            rows = tests.Rows.Select(r => new
            {
                id = r.Definition.Id.Value, name = r.Name, selected = r.IsSelected, duration = r.DurationText, repeat = r.Repeat.ToString(), count = r.RepeatCountText,
                options = r.Options.Select(Option), error = r.ValidationError, outcome = r.Outcome.ToString(), outcomeText = r.OutcomeText,
                percent = r.PercentComplete, status = r.StatusText, errors = r.HasErrors ? r.ErrorsText : null, detail = r.Detail,
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
