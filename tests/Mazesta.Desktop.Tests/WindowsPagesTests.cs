using System.Text; using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics.Windows; using Xunit;
namespace Mazesta.Desktop.Tests;

public class WindowsPagesTests
{
    private sealed class Runner(Func<string, string[]> output) : ICommandRunner
    {
        public List<string> Calls { get; } = [];
        public Task<CommandResult> RunAsync(string file, string arguments, Encoding encoding, Action<string>? line, CancellationToken ct)
        {
            Calls.Add($"{file} {arguments}"); var lines = output(arguments); foreach (var l in lines) line?.Invoke(l);
            return Task.FromResult(new CommandResult(0, lines));
        }
    }
    private sealed class NoWmi : Mazesta.Hardware.Wmi.IWmiQuery { public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string scope, string wql) => []; }
    private static readonly Func<Action, object> Now = a => { a(); return null!; };

    [Fact] public async Task A_repair_tool_shows_its_output_moves_the_bar_on_progress_lines_and_ends_with_its_result()
    {
        var runner = new Runner(_ => ["Beginning system scan.", "Verification 60% complete.", "Windows Resource Protection did not find any integrity violations."]);
        var vm = new WindowsToolsViewModel(runner, new NoWmi(), _ => { }, Now);
        await vm.SfcCommand.ExecuteAsync(null);
        Assert.Equal(["sfc.exe /scannow"], runner.Calls);
        Assert.Equal(["Beginning system scan.", "Windows Resource Protection did not find any integrity violations."], vm.Output);   // the progress line moved the bar instead
        Assert.Equal(Mazesta.Desktop.Localization.Loc.Get("Tools_Result_Healthy"), vm.Status); Assert.False(vm.IsBusy);
    }
    [Fact] public async Task Activating_a_plan_asks_powercfg_and_then_shows_what_windows_reports()
    {
        bool switched = false;
        var runner = new Runner(args =>
        {
            if (args.StartsWith("/setactive")) { switched = true; return []; }
            return ["Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)" + (switched ? "" : " *"), "Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)" + (switched ? " *" : "")];
        });
        var vm = new GamingViewModel(runner, _ => { }, new GamingStatus(true, null)); await vm.Loaded;
        Assert.True(vm.Plans[0].IsActive);
        await vm.ActivateCommand.ExecuteAsync(vm.Plans[1]);
        Assert.Contains("powercfg.exe /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", runner.Calls); Assert.True(vm.Plans[1].IsActive); Assert.False(vm.Plans[0].IsActive);
        Assert.Equal(Mazesta.Desktop.Localization.Loc.Get("Gaming_Default"), vm.GpuScheduling);   // not set in the registry: Windows' default, not "off"
    }
}
