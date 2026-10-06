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
            ITestExecutor[] executors = [new Stub(new(new TestId("cpu.stress"), "Test_Cpu_Stress", 60)), new Stub(new(new TestId("memory.pattern"), "Test_Memory_Pattern", 60)), new Stub(new(new TestId("cpu.fft"), "Test_Cpu_Fft", 60))];
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

    /// <summary>Holds until every lane has started, so a pass proves they overlapped; then notes the order the tests ended in.</summary>
    private sealed class Meeting(TestDefinition d, Barrier? all, List<string> ended) : ITestExecutor
    {
        public TestDefinition Definition { get; } = d;
        public Task<TestRunResult> RunAsync(TestExecutionRequest r, CancellationToken ct) => Task.Run(() =>
        {
            bool met = all?.SignalAndWait(TimeSpan.FromSeconds(20), ct) ?? true;
            lock (ended) ended.Add(Definition.Id.Value);
            return new TestRunResult(Definition.Id, met ? TestOutcome.Passed : TestOutcome.Failed, r.Clock.UtcNow, r.Clock.UtcNow, 0, null);
        }, ct);
    }

    [Fact] public async Task Together_the_parts_run_side_by_side_and_the_rest_afterwards()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mazesta-together-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            using var all = new Barrier(3); var ended = new List<string>();
            ITestExecutor[] executors = [new Meeting(new(new TestId("cpu.stress"), "Test_Cpu_Stress", 60), all, ended), new Meeting(new(new TestId("memory.pattern"), "Test_Memory_Pattern", 60), all, ended),
                new Meeting(new(new TestId("storage.smart"), "Test_Storage_Smart", 5), null, ended), new Meeting(new(new TestId("gpu.steady"), "Test_Gpu_Steady", 60), all, ended)];
            var engine = new TestEngine(executors, new JsonStore<TestSessionCheckpoint>(Path.Combine(dir, "cp.json"), new SchemaMigrator([]), TestSessionCheckpoint.CurrentSchemaVersion, NullLogger.Instance), new Clock());
            var outcomes = new Dictionary<string, TestOutcome>(); engine.TestCompleted += (id, r) => { lock (outcomes) outcomes[id.Value] = r.Outcome; };
            await engine.RunAsync([.. executors.Select(e => new QueuedTest(e.Definition, 1, RepeatMode.Once, 1))], together: true);
            Assert.All(outcomes.Values, o => Assert.Equal(TestOutcome.Passed, o)); Assert.Equal(4, outcomes.Count);
            Assert.Equal("storage.smart", ended[^1]);   // the drive check waited for the load tests
            Assert.Null(engine.FindIncompleteSession());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact] public void Only_the_processor_memory_and_graphics_tests_have_a_lane()
        => Assert.Equal(["cpu", "memory", "gpu", null, null, null, null], new[] { "cpu.fft", "memory.bitfade", "gpu.scene.rt", "storage.smart", "network.speed", "power.combined", "windows.sfc" }.Select(x => TestEngine.LaneOf(new TestId(x))));
}
