using Mazesta.Desktop.Composition; using Mazesta.Desktop.ViewModels; using Mazesta.Persistence;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

/// <summary>
/// <c>Mazesta.exe --selftest=undervolt|overclock|plus|scene|scenert</c>: runs that tuning feature on the real card with nobody at the screen and writes every step and the
/// outcome to <c>logs/selftest-&lt;kind&gt;.txt</c>. It only measures - the finding is not applied (see <see cref="TuningViewModel.SelfTest"/>). It opens no port and takes no input.
/// <c>--selftest=report</c> sends the usage statistics not sent yet, as the Settings button does, and writes whether the site took them.
/// <c>--selftest=tests[N][:id,id]</c> runs every test this machine can (or just the listed ones), N seconds each (15 by default), and writes what each result's page would show: its figures and lines, a line
/// that is still the executor's English marked <c>[LATIN]</c>.
/// </summary>
internal static class SelfTest
{
    private const string Prefix = "--selftest=";
    public static string? Requested() => Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(Prefix, StringComparison.Ordinal))?[Prefix.Length..];

    public static void Run(string kind, IServiceProvider services, AppPaths paths, UiDispatcher ui, ILogger log) => _ = Task.Run(async () =>
    {
        string file = Path.Combine(paths.LogsDir, $"selftest-{kind.Replace(':', '-').Replace(',', '+')}.txt");   // (a colon would name a stream of another file)
        void Say(string line) { try { File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}"); } catch (IOException) { } }
        try
        {
            File.WriteAllText(file, "");
            if (kind.StartsWith("tests", StringComparison.Ordinal)) { await RunTests(kind, services, ui, Say); Say("DONE"); return; }
            if (kind == "report")
            {
                // What the Settings button does for the statistics: send everything not sent yet, switch or no switch, and say whether the site took it.
                var log = services.GetRequiredService<Mazesta.Desktop.Services.UsageRecorder>().Log;
                int waiting = log.Unsent(100000).Count; bool sent = Program.Uploader is { } up && await up.SendAsync(force: true);
                Say($"{waiting} events were waiting; sent = {sent}; still unsent: {log.Unsent(100000).Count}"); Say("DONE"); return;
            }
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
        // tests180:gpu.vram@thermal=85 - the temperature guard on, with the VRAM limit at 85 C held for one minute (the other limits are the defaults).
        int? guardVram = null; if (kind.IndexOf("@thermal=", StringComparison.Ordinal) is var at and >= 0) { guardVram = int.Parse(kind[(at + 9)..], System.Globalization.CultureInfo.InvariantCulture); kind = kind[..at]; }
        // tests60:gpu.steady@cancel=8 - cancelled after 8 s; then the page's own question (is each test still available?) is asked for half a minute.
        int? cancelAfter = null; if (kind.IndexOf("@cancel=", StringComparison.Ordinal) is var cut and >= 0) { cancelAfter = int.Parse(kind[(cut + 8)..], System.Globalization.CultureInfo.InvariantCulture); kind = kind[..cut]; }
        string[] only = kind.Contains(':') ? kind[(kind.IndexOf(':') + 1)..].Split(',') : [];   // tests15:gpu.vram,gpu.render - just these
        string count = kind.Contains(':') ? kind[5..kind.IndexOf(':')] : kind[5..];
        string seconds = int.TryParse(count, out int n) && n > 0 ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "15";
        services.GetRequiredService<Mazesta.Desktop.Services.CheckupService>().SessionJudged += j =>
        {
            if (j.Trip is { } t) say($"THERMAL STOP: {t.Sensor} {t.ValueC:F0} C, {(t.Sustained ? "held at the limit " : "over the ceiling ")}{t.LimitC:F0} C, running: {string.Join(", ", (j.TripTests ?? []).Select(x => x.Value))}");
            foreach (var p in j.Parts) say($"POPUP {p.Part} {p.Name}: " + string.Join(", ", p.Temps?.Select(x => $"{x.Key} {x.C:F0} C") ?? []));
        };
        var center = await Ui(ui, () => services.GetRequiredService<Func<TestCenterViewModel>>()());
        var chosen = await Ui(ui, () =>
        {
            foreach (var r in center.Rows) r.RefreshAvailability();
            center.SelectAllCommand.Execute(null);
            if (only.Length > 0) foreach (var r in center.Rows) r.IsSelected &= only.Contains(r.Definition.Id.Value);
            foreach (var r in center.Rows.Where(r => r.IsSelected)) r.DurationText = seconds;
            if (guardVram is { } vram) { center.ThermalStop = true; center.ThermalLimits = () => new(98, 90, 98, vram, 70, TimeSpan.FromMinutes(1)); }
            _ = center.StartCommand.ExecuteAsync(null);
            return center.Rows.Where(r => r.IsSelected).Select(r => r.Definition.Id.Value).ToList();
        });
        say($"{chosen.Count} tests, {seconds} s each: {string.Join(", ", chosen)}");
        if (cancelAfter is { } stopAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(stopAt)); await Ui(ui, () => { center.CancelCommand.Execute(null); return 0; });
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                var seen = await Ui(ui, () => { bool running = center.IsRunning; if (!running) foreach (var r in center.Rows) r.RefreshAvailability(); return (running, string.Join(" | ", center.Rows.Where(r => r.Definition.Id.Value.StartsWith("gpu.", StringComparison.Ordinal)).Select(r => $"{r.Definition.Id.Value}:{r.UnavailableText ?? "ok"}"))); });
                say($"after cancel +{(i + 1) * 2} s: running={seen.running} {seen.Item2}");
            }
        }
        await Task.Delay(TimeSpan.FromSeconds(5));
        while (await Ui(ui, () => center.IsRunning)) await Task.Delay(TimeSpan.FromSeconds(2));
        foreach (var row in await Ui(ui, () => center.Rows.Where(r => chosen.Contains(r.Definition.Id.Value)).ToList()))
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
