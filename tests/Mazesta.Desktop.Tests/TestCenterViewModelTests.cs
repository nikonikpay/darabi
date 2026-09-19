using System.IO; using Xunit; using Mazesta.Diagnostics; using Mazesta.Desktop.ViewModels;
namespace Mazesta.Desktop.Tests;

public class TestCenterViewModelTests
{
    private static readonly TestDefinition WithOptions = new(new TestId("fake.opts"), "Test_Cpu_Matrix", 10,
    [
        new TestOption("size", "Test_Option_FileMb", TestOptionKind.Integer, "64"),
        new TestOption("drive", "Test_Option_Drive", TestOptionKind.Choice, "", () => [new("C:\\", "C:\\ (10 GB free)"), new("D:\\", "D:\\ (20 GB free)")]),
        new TestOption("target", "Test_Option_PingTarget", TestOptionKind.Text, "1.1.1.1")
    ]);

    [Fact] public void A_row_starts_unselected_with_the_test_s_own_options_at_their_defaults()
    {
        var row = new TestQueueRowViewModel(WithOptions);
        Assert.False(row.IsSelected); Assert.True(row.HasOptions);
        Assert.Equal(["64", "C:\\", "1.1.1.1"], row.Options.Select(o => o.Value));   // a choice with no default starts on the first entry
    }

    [Fact] public void Built_queue_entry_carries_every_option_value_and_normalises_persian_digits()
    {
        var row = new TestQueueRowViewModel(WithOptions);
        row.Options[0].Text = "۲۵۶"; row.Options[1].SelectedChoice = row.Options[1].Choices[1]; row.DurationText = "۳۰";
        var queued = row.TryBuildQueuedTest();
        Assert.NotNull(queued); Assert.Equal(30, queued!.DurationSeconds);
        Assert.Equal("256", queued.Options!["size"]); Assert.Equal("D:\\", queued.Options["drive"]); Assert.Equal("1.1.1.1", queued.Options["target"]);
    }

    [Fact] public void An_invalid_number_option_is_rejected_and_named_so_it_can_be_corrected()
    {
        var row = new TestQueueRowViewModel(WithOptions);
        row.Options[0].Text = "many";
        Assert.Null(row.TryBuildQueuedTest());
        Assert.True(row.HasValidationError); Assert.Contains(row.Options[0].Label, row.ValidationError);
    }

    [Fact] public void Start_is_enabled_only_when_something_is_selected_and_no_run_is_active()
    {
        var engine = new TestEngine([], new Mazesta.Persistence.JsonStore<TestSessionCheckpoint>(Path.Combine(Path.GetTempPath(), "mazesta-vm-" + Guid.NewGuid().ToString("N") + ".json"), new Mazesta.Persistence.SchemaMigrator([]), TestSessionCheckpoint.CurrentSchemaVersion, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance), new Mazesta.Core.Time.SystemClock());
        var vm = new TestCenterViewModel(engine, [new NoopExecutor(WithOptions)], a => { a(); return null!; });
        Assert.False(vm.StartCommand.CanExecute(null));
        vm.Rows[0].IsSelected = true; Assert.True(vm.StartCommand.CanExecute(null));
        vm.ClearSelectionCommand.Execute(null); Assert.False(vm.StartCommand.CanExecute(null));
        vm.SelectAllCommand.Execute(null); Assert.True(vm.Rows.All(r => r.IsSelected));
    }

    private sealed class NoopExecutor(TestDefinition definition) : ITestExecutor
    {
        public TestDefinition Definition => definition;
        public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => Task.FromResult(TestRunResult.Cancelled(definition.Id, request.Clock.UtcNow, request.Clock.UtcNow));
    }
}
