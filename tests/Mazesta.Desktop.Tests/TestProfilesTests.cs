using System.IO; using System.Reflection; using Xunit; using Mazesta.Diagnostics; using Mazesta.Desktop.ViewModels; using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Desktop.Tests;

public class TestProfilesTests
{
    /// <summary>Every test definition the app declares, found in the diagnostics assemblies.</summary>
    private static HashSet<string> DeclaredIds() => [.. new[] { typeof(TestEngine).Assembly, typeof(Mazesta.Diagnostics.Gpu.GpuDevices).Assembly }
        .SelectMany(a => a.GetTypes()).SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static)).Where(f => f.FieldType == typeof(TestDefinition))
        .Select(f => ((TestDefinition)f.GetValue(null)!).Id.Value)];

    [Fact] public void Every_test_a_profile_names_exists_and_has_a_length()
    {
        var ids = DeclaredIds();
        Assert.All(TestProfiles.All.SelectMany(p => p.Tests), t => { Assert.Contains(t.TestId, ids); Assert.True(t.Seconds > 0); });
    }

    private sealed class Stub(TestDefinition d) : ITestExecutor
    {
        public TestDefinition Definition { get; } = d;
        public Task<TestRunResult> RunAsync(TestExecutionRequest r, CancellationToken ct) => Task.FromResult(new TestRunResult(Definition.Id, TestOutcome.Passed, r.Clock.UtcNow, r.Clock.UtcNow, 0, null));
    }
    private sealed class Clock : Mazesta.Core.Time.IClock { public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch; }

    [Fact] public void Applying_a_profile_selects_exactly_its_tests_with_its_lengths()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mazesta-profile-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            ITestExecutor[] executors = [new Stub(new(new TestId("cpu.matrix"), "Test_Cpu_Matrix", 60)), new Stub(new(new TestId("memory.pattern"), "Test_Memory_Pattern", 60)), new Stub(new(new TestId("cpu.fft"), "Test_Cpu_Fft", 60))];
            var engine = new TestEngine(executors, new JsonStore<TestSessionCheckpoint>(Path.Combine(dir, "cp.json"), new SchemaMigrator([]), TestSessionCheckpoint.CurrentSchemaVersion, NullLogger.Instance), new Clock());
            using var vm = new TestCenterViewModel(engine, executors, a => { a(); return null!; });
            vm.Rows[2].IsSelected = true;   // chosen before: a profile replaces the selection
            vm.ApplyProfile("quick");
            Assert.Equal([true, true, false], vm.Rows.Select(r => r.IsSelected));
            Assert.Equal(["60", "120"], vm.Rows.Take(2).Select(r => r.DurationText));
            Assert.NotNull(vm.ProfileNote);
        }
        finally { Directory.Delete(dir, true); }
    }
}
