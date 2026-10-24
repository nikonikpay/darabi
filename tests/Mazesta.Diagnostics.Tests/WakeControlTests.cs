using System.Text; using Mazesta.Core.Windows; using Mazesta.Diagnostics.Windows; using Xunit;
namespace Mazesta.Diagnostics.Tests;

public class WakeControlTests
{
    /// <summary>powercfg answers from a script; every command is kept so the test can see what was run.</summary>
    private sealed class Fake : ICommandRunner
    {
        public readonly List<string> Ran = [];
        public Task<CommandResult> RunAsync(string file, string arguments, Encoding output, Action<string>? line, CancellationToken ct)
        {
            Ran.Add(arguments);
            return Task.FromResult(arguments.StartsWith("/devicequery wake_programmable", StringComparison.Ordinal) ? new CommandResult(0, ["HID Keyboard Device", "HID-compliant mouse"]) : new CommandResult(0, []));
        }
    }

    [Fact] public async Task A_device_that_windows_does_not_list_is_refused_and_nothing_is_run()
    {
        var fake = new Fake();
        Assert.Equal("unknown device", await WakeControl.SetDeviceAsync(fake, "x\" /deviceenablewake \"y", false, CancellationToken.None));
        Assert.DoesNotContain(fake.Ran, a => a.Contains("devicedisablewake", StringComparison.Ordinal));
    }

    [Fact] public async Task Turning_a_listed_device_off_runs_powercfg_with_its_quoted_name()
    {
        var fake = new Fake();
        Assert.Null(await WakeControl.SetDeviceAsync(fake, "HID-compliant mouse", false, CancellationToken.None));
        Assert.Contains("/devicedisablewake \"HID-compliant mouse\"", fake.Ran);
    }

    [Fact] public async Task Turn_everything_off_keeps_exactly_the_devices_that_were_on()
    {
        var fake = new Fake();
        var snapshot = new WakeSnapshot([new("HID Keyboard Device", WakeKind.Input, true), new("HID-compliant mouse", WakeKind.Input, false)], 0, 0, 0, [], [], [], [], null);
        var (undo, errors) = await WakeControl.DisableAllAsync(fake, snapshot, CancellationToken.None);
        Assert.Empty(errors);
        Assert.Equal(["HID Keyboard Device"], undo.Devices);
        Assert.Null(undo.TimersAc);   // the timers were off already: nothing to put back
    }

    [Fact] public async Task A_timer_value_outside_zero_to_two_is_refused()
        => Assert.Equal("bad value", await WakeControl.SetTimersAsync(new Fake(), 3, 0, CancellationToken.None));
}
