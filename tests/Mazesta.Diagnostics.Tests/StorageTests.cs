using Xunit; using Mazesta.Diagnostics.Storage; using Mazesta.Diagnostics.Tests.Fakes;
namespace Mazesta.Diagnostics.Tests;

public class StorageTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-storage-tests-" + Guid.NewGuid().ToString("N"));
    public StorageTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static TestExecutionRequest Request(TestDefinition def, string folder, int fileMb, int seconds = 1) =>
        new(seconds, new FakeClock(T0), null, null, new TestOptions(def, new Dictionary<string, string> { [StorageExecutor.DriveOption] = folder, [StorageExecutor.FileMbOption] = fileMb.ToString() }));
    private string[] Leftovers() => Directory.GetFiles(_dir, ".mazesta-test-*", SearchOption.TopDirectoryOnly);

    [Fact] public async Task Sequential_write_and_read_back_passes_and_leaves_no_file_behind()
    {
        var result = await new StorageSequentialExecutor().RunAsync(Request(StorageSequentialExecutor.Spec, _dir, 32), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, result.Outcome); Assert.Equal(0, result.ErrorCount);
        Assert.Contains("sequential unbuffered", result.Detail);
        Assert.Empty(Leftovers());
    }
    [Fact] public async Task Random_4k_write_and_read_back_passes_and_reports_iops()
    {
        var result = await new StorageRandom4kExecutor().RunAsync(Request(StorageRandom4kExecutor.Spec, _dir, 16), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, result.Outcome); Assert.Contains("IOPS", result.Detail);
        Assert.Empty(Leftovers());
    }
    [Fact] public async Task A_missing_folder_is_Unsupported_not_a_failing_drive()
    {
        var result = await new StorageSequentialExecutor().RunAsync(Request(StorageSequentialExecutor.Spec, Path.Combine(_dir, "nope"), 32), CancellationToken.None);
        Assert.Equal(TestOutcome.Unsupported, result.Outcome);
    }
    [Fact] public async Task A_file_size_outside_the_allowed_range_is_Unsupported()
        => Assert.Equal(TestOutcome.Unsupported, (await new StorageSequentialExecutor().RunAsync(Request(StorageSequentialExecutor.Spec, _dir, 4), CancellationToken.None)).Outcome);
    [Fact] public async Task Cancelling_mid_run_reports_Cancelled_and_still_cleans_up()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var result = await new StorageSequentialExecutor().RunAsync(Request(StorageSequentialExecutor.Spec, _dir, 256, seconds: 30), cts.Token);
        Assert.Equal(TestOutcome.Cancelled, result.Outcome);
        Assert.Empty(Leftovers());
    }
    [Fact] public void Drive_choices_list_fixed_drives_with_free_space_and_the_default_target_resolves()
    {
        var choices = StorageFile.DriveChoices();
        Assert.NotEmpty(choices); Assert.All(choices, c => Assert.Contains("GB free", c.Label));
        Assert.Equal(choices[0].Value, StorageFile.ResolveTarget(""));
    }
}
