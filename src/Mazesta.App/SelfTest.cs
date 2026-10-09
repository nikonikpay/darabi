using Mazesta.Desktop.Composition; using Mazesta.Desktop.ViewModels; using Mazesta.Persistence;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

/// <summary>
/// <c>Mazesta.exe --selftest=undervolt|overclock|plus|scene</c>: runs that tuning feature on the real card with nobody at the screen and writes every step and the
/// outcome to <c>logs/selftest-&lt;kind&gt;.txt</c>. It only measures - the finding is not applied (see <see cref="TuningViewModel.SelfTest"/>). It opens no port and takes no input.
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

    private static Task<T> Ui<T>(UiDispatcher ui, Func<T> work)
    {
        var done = new TaskCompletionSource<T>();
        ui.BeginInvoke(() => { try { done.SetResult(work()); } catch (Exception e) { done.SetException(e); } });
        return done.Task;
    }
}
