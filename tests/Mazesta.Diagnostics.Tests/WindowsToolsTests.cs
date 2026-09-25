using System.Text; using Xunit; using Mazesta.Diagnostics.Windows; using Mazesta.Diagnostics.Tests.Fakes;
namespace Mazesta.Diagnostics.Tests;

public class WindowsToolsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
    /// <summary>Plays back a tool's output line by line, or throws as a cancelled or missing tool would.</summary>
    public sealed class FakeRunner(params string[] output) : ICommandRunner
    {
        public List<string> Calls { get; } = [];
        public Exception? Throw { get; init; }
        public Task<CommandResult> RunAsync(string file, string arguments, Encoding encoding, Action<string>? line, CancellationToken ct)
        {
            Calls.Add($"{file} {arguments}");
            if (Throw is not null) throw Throw;
            foreach (var l in output) line?.Invoke(l);
            return Task.FromResult(new CommandResult(0, output));
        }
    }
    private static Task<TestRunResult> Sfc(params string[] output) => new SfcExecutor(new FakeRunner(output)).RunAsync(new TestExecutionRequest(900, new FakeClock(T0), null, null), CancellationToken.None);

    [Fact] public async Task Sfc_passes_when_clean_or_repaired_and_fails_when_it_could_not_repair()
    {
        Assert.Equal(TestOutcome.Passed, (await Sfc("Verification 100% complete.", "Windows Resource Protection did not find any integrity violations.")).Outcome);
        Assert.Equal(TestOutcome.Passed, (await Sfc("Windows Resource Protection found corrupt files and successfully repaired them.")).Outcome);
        var damaged = await Sfc("Windows Resource Protection found corrupt files but was unable to fix some of them.");
        Assert.Equal(TestOutcome.Failed, damaged.Outcome); Assert.Equal(1, damaged.ErrorCount); Assert.Contains("unable to fix", damaged.Detail);
    }
    [Fact] public async Task An_unreadable_result_is_Unsupported_never_a_pass_or_a_fail()
        => Assert.Equal(TestOutcome.Unsupported, (await Sfc("Système de protection des ressources Windows : terminé.")).Outcome);
    [Fact] public async Task Dism_check_reports_a_repairable_store_as_a_failure()
    {
        var r = await new DismScanExecutor(new FakeRunner("[==========================100.0%==========================]", "The component store is repairable.")).RunAsync(new TestExecutionRequest(300, new FakeClock(T0), null, null), CancellationToken.None);
        Assert.Equal(TestOutcome.Failed, r.Outcome); Assert.DoesNotContain("100.0%", r.Detail);   // progress lines are not evidence
    }
    [Fact] public async Task A_cancelled_tool_is_Cancelled_and_a_missing_one_Unsupported()
    {
        var request = new TestExecutionRequest(900, new FakeClock(T0), null, null);
        Assert.Equal(TestOutcome.Cancelled, (await new SfcExecutor(new FakeRunner { Throw = new OperationCanceledException() }).RunAsync(request, CancellationToken.None)).Outcome);
        Assert.Equal(TestOutcome.Unsupported, (await new SfcExecutor(new FakeRunner { Throw = new System.ComponentModel.Win32Exception() }).RunAsync(request, CancellationToken.None)).Outcome);
    }
    [Theory, InlineData("Verification 45% complete.", 0.45), InlineData("[=====   10.0%   ]", 0.1), InlineData("Beginning system scan.", null)]
    public void Progress_is_read_from_the_tools_own_lines(string line, double? expected) => Assert.Equal(expected, WindowsTool.ProgressOf(line));

    [Fact] public void Power_plans_are_read_by_guid_with_the_active_one_marked()
    {
        var plans = PowerPlans.Parse(["Existing Power Schemes (* Active)", "-----------------------------------",
            "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)", "Power Scheme GUID: 9935e61f-1661-40c5-ae2f-8495027d5d5d  (AMD Ryzen™ High Performance) *"]);
        Assert.Equal(2, plans.Count); Assert.False(plans[0].IsActive);
        Assert.Equal((Guid.Parse("9935e61f-1661-40c5-ae2f-8495027d5d5d"), "AMD Ryzen™ High Performance", true), (plans[1].Id, plans[1].Name, plans[1].IsActive));
    }
}

/// <summary>Read-only checks against this machine's Windows (Category=Hardware): powercfg lists plans, one of them active.</summary>
[Trait("Category", "Hardware")]
public class WindowsToolsHardwareTests
{
    [Fact] public async Task Powercfg_lists_the_power_plans_with_exactly_one_active()
    {
        var plans = PowerPlans.Parse((await new ProcessCommandRunner().RunAsync("powercfg.exe", "/list", WindowsTool.Oem, null, CancellationToken.None)).Output);
        Assert.NotEmpty(plans); Assert.Single(plans, p => p.IsActive);
    }
    [Fact] public void Gaming_status_reads_without_throwing() => _ = GamingStatus.Read();
}
