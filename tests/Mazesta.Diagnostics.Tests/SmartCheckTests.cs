using Xunit; using Mazesta.Core.Providers; using Mazesta.Diagnostics.Storage; using Mazesta.Diagnostics.Tests.Fakes;
namespace Mazesta.Diagnostics.Tests;

public class SmartCheckTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
    private sealed class Drives(params DriveHealth[] drives) : IDriveHealthProvider { public IReadOnlyList<DriveHealth> Read() => drives; }
    private static Task<TestRunResult> Run(params DriveHealth[] drives) => new SmartCheckExecutor(new Drives(drives)).RunAsync(new TestExecutionRequest(5, new FakeClock(T0), null, null), CancellationToken.None);
    private static DriveHealth Ssd(string status = "Healthy", long? readErrors = 0) => new("MSI M390 1TB", "S1", status, 3, 38, 61, readErrors, 0, 1234, "NVMe");

    [Fact] public async Task Healthy_drives_pass_with_their_counters_as_evidence()
    {
        var r = await Run(Ssd(), new DriveHealth("WDC WD20PURZ", "W2", "Healthy", null, null, null, null, null, null));
        Assert.Equal(TestOutcome.Passed, r.Outcome); Assert.Equal(0, r.ErrorCount);
        Assert.Contains("MSI M390 1TB (S1): Healthy, wear 3%, 38 °C (max 61 °C), power-on 1234 h, uncorrected read/write errors 0/0", r.Detail);
        Assert.Contains("WDC WD20PURZ (W2): Healthy", r.Detail); Assert.DoesNotContain("wear 0%", r.Detail);   // an unreported counter is left out, never 0
    }
    [Theory, InlineData("Warning", 0L), InlineData("Unhealthy", 0L), InlineData("Healthy", 7L)]
    public async Task A_warning_an_unhealthy_drive_or_uncorrected_errors_fail_one_error_per_drive(string status, long readErrors)
    {
        var r = await Run(Ssd(status, readErrors), Ssd());
        Assert.Equal(TestOutcome.Failed, r.Outcome); Assert.Equal(1, r.ErrorCount);
    }
    [Fact] public async Task No_health_status_at_all_is_Unsupported_not_a_pass()
        => Assert.Equal(TestOutcome.Unsupported, (await Run(new DriveHealth("X", null, null, null, null, null, null, null, null))).Outcome);
}
