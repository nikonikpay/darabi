using Mazesta.Desktop.Composition; using Mazesta.Desktop.ViewModels; using Mazesta.Persistence;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

/// <summary>
/// <c>Mazesta.exe --selftest=undervolt|overclock|plus|scene|scenert</c>: runs that tuning feature on the real card with nobody at the screen and writes every step and the
/// outcome to <c>logs/selftest-&lt;kind&gt;.txt</c>. It only measures - the finding is not applied (see <see cref="TuningViewModel.SelfTest"/>). It opens no port and takes no input.
/// <c>--selftest=shield</c> blocks a copy of the program in the firewall and leaves a folder out of Defender's scans, reads both back and removes them (see <see cref="SelfTestShield"/>).
/// <c>--selftest=lights</c> puts the lights out and back from the note, ends and starts their server as a wake does, and runs the effects and a spectrum on every device (see <see cref="SelfTestLights"/>).
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
            if (kind.StartsWith("bench", StringComparison.Ordinal)) { await RunBenchmarks(kind, services, ui, Say); Say("DONE"); return; }
            if (kind == "shield") { await SelfTestShield.RunAsync(Say); Say("DONE"); return; }
            if (kind == "gpufan") { await GpuFan(Say); Say("DONE"); return; }
            if (kind == "lights") { await SelfTestLights.RunAsync(paths, Say); Say("DONE"); return; }
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
            if (kind == "manual") { await Manual(t, Say); Say("DONE"); return; }
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

    /// <summary>--selftest=gpufan: holds the card's fans on a flat curve at two duties, then on the ready-made curves, reads what the driver and nvidia-smi (another process) say after each, and gives the fans back.</summary>
    private static async Task GpuFan(Action<string> say)
    {
        await Task.Delay(TimeSpan.FromSeconds(20));   // the sensor scan has found the card by then
        var f = GpuFanController.Current; string Show() => System.Text.Json.JsonSerializer.Serialize(f.State());
        string Smi() { using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("nvidia-smi", "--query-gpu=fan.speed,temperature.gpu --format=csv,noheader") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!; var o = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); return o; }
        async Task Step(string title, Action act, int seconds = 7) { try { act(); } catch (Exception e) { say($"{title}: FAILED {e.Message}"); return; } await Task.Delay(TimeSpan.FromSeconds(seconds)); say($"{title}: nvidia-smi [{Smi()}] | {Show()}"); }
        say($"before: nvidia-smi [{Smi()}] | {Show()}");
        await Step("flat curve at 40 %", () => f.Set("curve", "core", [new(20, 40), new(100, 40)]));
        await Step("flat curve at 65 %", () => f.Set("curve", "core", [new(20, 65), new(100, 65)]));
        await Step("flat curve at 40 % on the hot spot", () => f.Set("curve", "hot", [new(20, 40), new(100, 40)]));
        await Step("performance profile", () => f.Apply("performance"));
        await Step("silent profile (slows 3 % every 2 s)", () => f.Apply("silent"), 25);
        await Step("saved profile test-night", () => { f.Save("test-night"); f.Apply("full"); f.Apply("test-night"); });
        await Step("profile test-night deleted, back to automatic", () => { f.Delete("test-night"); f.Reset(); });
    }

    /// <summary>--selftest=manual: applies a manual core offset, voltage offset, memory offset and fan duty to the card, reads each back from the driver and from the telemetry, and puts the card back as it was.</summary>
    private static async Task Manual(TuningViewModel t, Action<string> say)
    {
        var d = t.Device!; var l = d.Limits; say($"limits: {l}");
        string Show(Mazesta.Core.Tuning.GpuTuningSettings s) => $"core {s.CoreOffsetMHz} MHz, memory {s.MemoryOffsetMHz} MHz, voltage {s.VoltageOffsetMv} mV, fan {s.FanPercent?.ToString() ?? "auto"}";
        say("before: " + Show(d.ReadCurrent()));
        async Task Try(string title, Mazesta.Core.Tuning.GpuTuningSettings s, int wait = 8)
        {
            var r = d.Apply(s); say($"{title}: " + string.Join("; ", r.Steps.Select(x => $"{x.SettingKey}={(x.Ok ? "ok" : "FAILED " + x.Error)}")));
            await Task.Delay(TimeSpan.FromSeconds(wait)); var back = d.ReadCurrent(); var tel = d.ReadTelemetry();
            say($"  read back: {Show(back)} | telemetry: clock {tel.CoreClockMHz} MHz, fan {tel.FanPercent} %, temp {tel.TemperatureC} C, power {tel.PowerW} W");
        }
        var stock = Mazesta.Core.Tuning.GpuTuningSettings.Stock;
        await Try("core +60", stock with { CoreOffsetMHz = 60 });
        await Try("voltage -60 mV", stock with { VoltageOffsetMv = -60 });
        await Try("memory +40", stock with { MemoryOffsetMHz = 40 });
        if (l.FanMaxPercent is { } max) { await Try("fan 70 %", stock with { FanPercent = 70 }, 12); await Try("fan 35 %", stock with { FanPercent = 35 }, 12); }
        await Try("all together", stock with { CoreOffsetMHz = 60, VoltageOffsetMv = -60, MemoryOffsetMHz = 40, FanPercent = l.FanMaxPercent is null ? null : 50 }, 12);
        var back0 = d.Reset(); say("reset: " + string.Join("; ", back0.Steps.Select(x => $"{x.SettingKey}={(x.Ok ? "ok" : "FAILED " + x.Error)}"))); await Task.Delay(8000);
        say("after reset: " + Show(d.ReadCurrent()) + $" | telemetry fan {d.ReadTelemetry().FanPercent} %");
    }

    private static Task RayTracedScene(TuningViewModel t) { t.SceneRayTracing = true; return t.SceneTestCommand.ExecuteAsync(null); }

    private static async Task RunTests(string kind, IServiceProvider services, UiDispatcher ui, Action<string> say)
    {
        await Task.Delay(TimeSpan.FromSeconds(25));   // the sensor scan has listed the parts by then
        // tests180:gpu.vram@thermal=85 - the temperature guard on, with the VRAM limit at 85 C held for one minute (the other limits are the defaults).
        int? guardVram = null; if (kind.IndexOf("@thermal=", StringComparison.Ordinal) is var at and >= 0) { guardVram = int.Parse(kind[(at + 9)..], System.Globalization.CultureInfo.InvariantCulture); kind = kind[..at]; }
        // tests60:gpu.steady@cancel=8 - cancelled after 8 s; then the page's own question (is each test still available?) is asked for half a minute.
        int? cancelAfter = null; if (kind.IndexOf("@cancel=", StringComparison.Ordinal) is var cut and >= 0) { cancelAfter = int.Parse(kind[(cut + 8)..], System.Globalization.CultureInfo.InvariantCulture); kind = kind[..cut]; }
        // tests60:gpu.scene.d3d@opt=raytracing:off - that option of the chosen tests set to that value (here: the scene without rays, to compare its power with the ray-traced one).
        string? optKey = null, optValue = null; if (kind.IndexOf("@opt=", StringComparison.Ordinal) is var opt and >= 0) { var kv = kind[(opt + 5)..].Split(':'); optKey = kv[0]; optValue = kv[1]; kind = kind[..opt]; }
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
            if (optKey is not null) foreach (var r in center.Rows.Where(r => r.IsSelected))
                if (r.Options.FirstOrDefault(o => o.Option.Key == optKey) is { } o) { if (o.IsChoice) o.SelectedChoice = o.Choices.FirstOrDefault(c => c.Value == optValue) ?? o.SelectedChoice; else o.Text = optValue!; }
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

    /// <summary>--selftest=bench[:id,id]: runs the benchmarks (the graphics card's by default) at their own lengths and writes each one's status, figures and detail.</summary>
    private static async Task RunBenchmarks(string kind, IServiceProvider services, UiDispatcher ui, Action<string> say)
    {
        await Task.Delay(TimeSpan.FromSeconds(25));   // the sensor scan has listed the parts by then
        string[] only = kind.Contains(':') ? kind[(kind.IndexOf(':') + 1)..].Split(',') : [];
        var vm = await Ui(ui, () => services.GetRequiredService<Func<BenchmarksViewModel>>()());
        var chosen = await Ui(ui, () =>
        {
            foreach (var r in vm.Rows) { r.RefreshAvailability(); string id = r.Benchmark.Definition.Id.Value; r.IsSelected = r.IsAvailable && (only.Length > 0 ? only.Contains(id) : id.StartsWith("bench.gpu", StringComparison.Ordinal)); }
            foreach (var r in vm.Rows.Where(r => !r.IsAvailable)) say($"not available: {r.Benchmark.Definition.Id.Value}: {r.UnavailableText}");
            _ = vm.RunSelectedCommand.ExecuteAsync(null);
            return vm.Rows.Where(r => r.IsSelected).Select(r => r.Benchmark.Definition.Id.Value).ToList();
        });
        say($"{chosen.Count} benchmarks: {string.Join(", ", chosen)}");
        await Task.Delay(TimeSpan.FromSeconds(5));
        while (await Ui(ui, () => vm.IsRunning)) await Task.Delay(TimeSpan.FromSeconds(2));
        foreach (var row in await Ui(ui, () => vm.Rows.Where(r => chosen.Contains(r.Benchmark.Definition.Id.Value)).ToList()))
        {
            var (id, status, metrics, detail) = await Ui(ui, () => (row.Benchmark.Definition.Id.Value, row.StatusText, row.Metrics.Select(m => $"{m.Name} = {m.Value}").ToList(), row.Detail));
            say($"=== {id} | {status}");
            foreach (var m in metrics) say("    " + m);
            if (detail is not null) say("    DETAIL " + detail.Replace("\n", " // "));
        }
    }

    private static Task<T> Ui<T>(UiDispatcher ui, Func<T> work)
    {
        var done = new TaskCompletionSource<T>();
        ui.BeginInvoke(() => { try { done.SetResult(work()); } catch (Exception e) { done.SetException(e); } });
        return done.Task;
    }
}
