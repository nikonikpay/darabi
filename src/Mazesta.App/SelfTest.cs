using Mazesta.Desktop.Composition; using Mazesta.Desktop.ViewModels; using Mazesta.Persistence;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

/// <summary>
/// <c>Mazesta.exe --selftest=undervolt|overclock|plus|scene|scenert</c>: runs that tuning feature on the real card with nobody at the screen and writes every step and the
/// outcome to <c>logs/selftest-&lt;kind&gt;.txt</c>. It only measures - the finding is not applied (see <see cref="TuningViewModel.SelfTest"/>). It opens no port and takes no input.
/// <c>--selftest=tests[N]</c> runs every test this machine can, N seconds each (15 by default), and writes what each result's page would show: its figures and lines, a line
/// that is still the executor's English marked <c>[LATIN]</c>.
/// </summary>
internal static class SelfTest
{
    private const string Prefix = "--selftest=";
    public static string? Requested() => Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(Prefix, StringComparison.Ordinal))?[Prefix.Length..];

    public static void Run(string kind, IServiceProvider services, AppPaths paths, UiDispatcher ui, ILogger log) => _ = Task.Run(async () =>
    {
        string file = Path.Combine(paths.LogsDir, $"selftest-{kind}.txt");
        void Say(string line) { try { File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}"); } catch (IOException) { } }
        try
        {
            File.WriteAllText(file, "");
            if (kind.StartsWith("tests", StringComparison.Ordinal)) { await RunTests(kind, services, ui, Say); Say("DONE"); return; }
            TuningViewModel.SelfTest = true;
            await Task.Delay(TimeSpan.FromSeconds(25));   // the sensor scan has found the card's voltage by then
            var t = await Ui(ui, () => services.GetRequiredService<TuningViewModel>());
            if (!t.HasDevice) { Say("No tunable card: " + t.Unavailable); return; }
            Say("Card: " + t.Device!.Name);
            await Ui(ui, () => { t.AutoLog.CollectionChanged += (_, e) => { if (e.NewItems is not null) foreach (AutoLogRow r in e.NewItems) Say($"step {r.Step} {r.Kind} | {r.Settings} | {r.Result}{(r.Problem is null ? "" : " | PROBLEM " + r.Problem)}"); }; return 0; });
            Task run = await Ui(ui, () => kind switch
            {
                "undervolt" => t.AutoUndervoltCommand.ExecuteAsync(null),
                "overclock" => t.AutoOverclockCommand.ExecuteAsync(null),
                "plus" => t.AutoOverclockPlusCommand.ExecuteAsync(null),
                "scene" => t.SceneTestCommand.ExecuteAsync(null),
                "scenert" => RayTracedScene(t),
                _ => throw new ArgumentException("unknown self-test: " + kind),
            });
            await run;
            var (result, status, scene) = await Ui(ui, () => (t.AutoResult, t.Status, t.SceneTests.FirstOrDefault()));
            Say("RESULT: " + result.Replace("\n", " // "));
            if (status.Length > 0) Say("STATUS: " + status);
            if (scene is not null) Say($"SCENE: {scene.Settings} | {scene.Result} | clean={scene.Clean} {scene.Problem}");
            Say("DONE");
        }
        catch (Exception e) { log.LogError(e, "Self-test {Kind} failed", kind); Say("FAILED: " + e); }
    });

    private static Task RayTracedScene(TuningViewModel t) { t.SceneRayTracing = true; return t.SceneTestCommand.ExecuteAsync(null); }

    private static async Task RunTests(string kind, IServiceProvider services, UiDispatcher ui, Action<string> say)
    {
        await Task.Delay(TimeSpan.FromSeconds(25));   // the sensor scan has listed the parts by then
        string seconds = int.TryParse(kind[5..], out int n) && n > 0 ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "15";
        var center = await Ui(ui, () => services.GetRequiredService<Func<TestCenterViewModel>>()());
        var chosen = await Ui(ui, () =>
        {
            foreach (var r in center.Rows) r.RefreshAvailability();
            center.SelectAllCommand.Execute(null);
            foreach (var r in center.Rows.Where(r => r.IsSelected)) r.DurationText = seconds;
            _ = center.StartCommand.ExecuteAsync(null);
            return center.Rows.Where(r => r.IsSelected).Select(r => r.Definition.Id.Value).ToList();
        });
        say($"{chosen.Count} tests, {seconds} s each: {string.Join(", ", chosen)}");
        await Task.Delay(TimeSpan.FromSeconds(5));
        while (await Ui(ui, () => center.IsRunning)) await Task.Delay(TimeSpan.FromSeconds(2));
        foreach (var row in await Ui(ui, () => center.Rows.ToList()))
        {
            var (id, name, outcome, errors, detail, advice) = await Ui(ui, () => (row.Definition.Id.Value, row.Name, row.Outcome, row.ErrorCount, row.Detail, row.Advice));
            say($"=== {id} | {outcome} | errors {errors} | {name}");
            if (detail is null) { say("    (no detail)"); continue; }
            var view = Mazesta.Desktop.Services.TestDetailText.View(detail);
            foreach (var f in view.Figures) say($"    FIG  {f.Name} = {f.Value}{(f.Note is null ? "" : "  (" + f.Note + ")")}");
            foreach (var l in view.Lines) say($"    {(l.Latin ? "[LATIN] " : "")}{l.Text}");
            if (advice is not null) say("    ADVICE: " + advice);
        }
    }

    private static Task<T> Ui<T>(UiDispatcher ui, Func<T> work)
    {
        var done = new TaskCompletionSource<T>();
        ui.BeginInvoke(() => { try { done.SetResult(work()); } catch (Exception e) { done.SetException(e); } });
        return done.Task;
    }
}
