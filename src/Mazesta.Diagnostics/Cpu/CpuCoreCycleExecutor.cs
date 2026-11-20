using System.Diagnostics; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Cpu;

/// <summary>
/// Single-core stability, one physical core at a time (or, when asked, a group of several at a time): one thread is pinned to a core's
/// first logical processor, runs the verified matrix workload, then moves to the next core, round and round until the time is up. A lone
/// busy thread lets a core reach its highest boost clock and lowest voltage - the state where an unstable boost / Curve Optimizer / undervolt
/// setting fails, and which an all-core load never reaches. With the variable load the thread also pauses at random moments, so the core
/// keeps dropping out of and back into boost, which is when marginal voltage steps show.
/// A visit is three kinds of work, a third of it each: the matrix products, vector multiply-adds (AVX2 + FMA, SSE2 on a CPU without them - not
/// AVX-512, which lowers the clock the test is after) and the integer block with its divider and branches. A core that holds one and not
/// another fails here; each kind has its own reference worked out in advance.
/// Every core computes the same seeded matrices, and each product is checked against the checksum worked out in advance for them (not
/// against the first core's answer, which would blame every good core if the first one were the bad one), so the core that computed wrongly
/// is named: a wrong answer is an error, not a crash the technician has to guess about.
/// A pass needs every core to have been tested: a core Windows would not pin the thread to, or one the time ran out before, leaves the
/// result Inconclusive with those cores named, never Passed.
/// </summary>
public sealed class CpuCoreCycleExecutor : ITestExecutor
{
    public const string SecondsPerCoreOption = "secondsPerCore", LoadOption = "load", CoresOption = "cores";
    /// <summary>A core's unstable boost shows after minutes on that core, not seconds: a minute a core by default, so the default length grows
    /// with the processor (five minutes at least, an hour at most).</summary>
    public const int DefaultSecondsPerCore = 60;
    public static readonly TestDefinition Definition = new(new TestId("cpu.singlecore"), "Test_Cpu_SingleCore", Math.Clamp(CpuTopology.Cores.Count * DefaultSecondsPerCore, 300, 3600),
    [
        // How many are loaded at a time: 1 is the single-core test proper; more moves a group of that many round the processor, up to
        // every thread it has (then it is an all-thread load, and nothing moves).
        new TestOption(CoresOption, "Test_Option_CoresAtOnce", TestOptionKind.Choice, "1", () => [.. Enumerable.Range(1, Math.Max(1, CpuTopology.Cores.Sum(c => c.Threads))).Select(n => new OptionChoice(n.ToString(System.Globalization.CultureInfo.InvariantCulture), n.ToString(System.Globalization.CultureInfo.InvariantCulture)))]),
        new TestOption(SecondsPerCoreOption, "Test_Option_SecondsPerCore", TestOptionKind.Integer, "60"),
        new TestOption(LoadOption, "Test_Option_Load", TestOptionKind.Choice, "variable", () => [new("variable", "Test_Load_Variable", true), new("steady", "Test_Load_Steady", true)]),
    ]);
    TestDefinition ITestExecutor.Definition => Definition;

    private const int N = CpuMatrixStressExecutor.MatrixSize;
    private readonly IReadOnlyList<CpuCore> _cores;
    public CpuCoreCycleExecutor(IReadOnlyList<CpuCore>? cores = null) => _cores = cores ?? CpuTopology.Cores;

    /// <summary>A place a thread is pinned to: one logical processor of a core.</summary>
    internal readonly record struct Slot(int Core, ushort Group, ulong Mask);

    /// <summary>The groups the load visits in turn, <paramref name="atOnce"/> places each. Up to the number of cores, a place is a core's first
    /// thread (so no loaded core shares itself with its sibling's work); beyond that there are not enough cores, and every logical processor is
    /// a place. A last group that would come up short is filled from the start, so the load is the same size on every visit.</summary>
    internal static IReadOnlyList<IReadOnlyList<Slot>> Groups(IReadOnlyList<CpuCore> cores, int atOnce)
    {
        var first = cores.Select(c => new Slot(c.Index, c.Group, c.FirstThreadMask)).ToList();
        var slots = first;
        if (atOnce > cores.Count)
        {
            slots = [.. first];
            foreach (var c in cores) for (ulong rest = c.Mask & ~c.FirstThreadMask; rest != 0; rest &= rest - 1) slots.Add(new(c.Index, c.Group, rest & (~rest + 1)));
        }
        atOnce = Math.Clamp(atOnce, 1, slots.Count);
        var groups = new List<IReadOnlyList<Slot>>();
        for (int start = 0; start < slots.Count; start += atOnce) groups.Add([.. Enumerable.Range(start, atOnce).Select(i => slots[i % slots.Count])]);
        return groups;
    }

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        if (_cores.Count == 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Windows reported no processor cores."));
        var options = request.Options ?? TestOptions.None(Definition);
        var groups = Groups(_cores, Math.Max(1, options.GetInt(CoresOption)));
        // Every group gets its turn inside the chosen length: a short run shortens each slice rather than leaving cores untested.
        double slice = Math.Max(1, Math.Min(Math.Max(1, options.GetInt(SecondsPerCoreOption)), request.DurationSeconds / (double)groups.Count));
        bool variable = options.Get(LoadOption) != "steady";
        var done = new TaskCompletionSource<TestRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { done.SetResult(Run(request, started, groups, slice, variable, ct)); } catch (Exception e) { done.SetException(e); } })
        { IsBackground = true, Name = "Mazesta single-core" };
        thread.Start();
        return done.Task;
    }

    private TestRunResult Run(TestExecutionRequest request, DateTimeOffset started, IReadOnlyList<IReadOnlyList<Slot>> groups, double slice, bool variable, CancellationToken ct)
    {
        var (a, b) = CpuMatrixStressExecutor.Inputs(0);
        ulong reference = CpuMatrixStressExecutor.Expected[0]; var errorsByCore = new long[_cores.Count]; var unpinned = new List<int>(); var covered = new bool[_cores.Count];
        long multiplies = 0, visits = 0; var total = Stopwatch.StartNew(); var duration = TimeSpan.FromSeconds(request.DurationSeconds);
        int atOnce = groups[0].Count; bool hybrid = _cores.Select(x => x.EfficiencyClass).Distinct().Count() > 1;
        var width = CpuVectorStressExecutor.Resolve("avx2") ?? CpuVectorStressExecutor.Resolve("sse"); var vector = width is { } vw ? CpuVectorStressExecutor.Reference(vw) : [];
        var seed = CpuIntegerExecutor.Seed(); var wrongByWork = new long[WorkNames.Length];
        string work = "work per visit: " + string.Join(", ", WorkNames.Select((n, k) => k == 1 ? width switch { CpuVectorStressExecutor.Width.Avx2 => "AVX2 + FMA", CpuVectorStressExecutor.Width.Sse => "SSE2", _ => null } : n).OfType<string>());
        string Describe() => this.Describe(request, started, slice, variable, atOnce, Interlocked.Read(ref multiplies), visits, errorsByCore, unpinned, covered, work, wrongByWork);

        // One place for one slice, on a thread of its own: the pin to a core outlives each work item, which must never happen to a pool thread.
        void Visit(Slot slot)
        {
            var core = _cores[slot.Core]; var c = new double[N, N]; var pause = new Random(Environment.TickCount ^ slot.Core * 7919 ^ (int)slot.Mask);
            bool pinned = CpuTopology.Pin(slot.Group, slot.Mask);
            Thread.Sleep(0);   // let the scheduler move the thread onto the core before the slice is timed
            pinned = pinned && CpuTopology.IsOn(slot.Group, slot.Mask);   // Windows accepted the pin and the thread is really there
            if (!pinned) lock (unpinned) { if (!unpinned.Contains(core.Index)) unpinned.Add(core.Index); }
            string kind = hybrid ? core.EfficiencyClass > 0 ? " (P)" : " (E)" : "";
            if (pinned) request.Note("Log_Core_Visit", $"SetThreadGroupAffinity(group {slot.Group}, mask 0x{slot.Mask:X})   a third each: C = A·B, 64×64, checksum == expected;  x ← fma(x, s, o), every lane == scalar reference;  integer block, FNV-1a == expected", core.Index, kind, slice);
            else request.NoteWarning("Log_Core_NotPinned", $"SetThreadGroupAffinity(group {slot.Group}, mask 0x{slot.Mask:X})", core.Index);
            long wrong = 0, done = 0, worked = 0; var ints = new ulong[CpuIntegerExecutor.Elements]; Span<double> lanes = stackalloc double[8]; lanes = lanes[..vector.Length];
            var step = Stopwatch.StartNew(); var burst = Stopwatch.StartNew(); double burstLength = NextBurst(pause, variable);
            while (step.Elapsed.TotalSeconds < slice && total.Elapsed < duration && !ct.IsCancellationRequested)
            {
                int kindOfWork = Math.Min(WorkNames.Length - 1, (int)(step.Elapsed.TotalSeconds / slice * WorkNames.Length)); bool right;
                if (kindOfWork == 1 && width is { } vec) { CpuVectorStressExecutor.Block(vec, lanes); right = CpuVectorStressExecutor.WrongLanes(lanes, vector) == 0; }
                else if (kindOfWork == 2) right = CpuIntegerExecutor.Block(ints, seed) == CpuIntegerExecutor.Expected;
                else { CpuMatrixStressExecutor.Multiply(a, b, c); done++; right = CpuMatrixStressExecutor.Checksum(c) == reference; }
                worked++;
                if (!right) { wrong++; Interlocked.Increment(ref wrongByWork[kindOfWork]); }
                if (variable && burst.Elapsed.TotalMilliseconds > burstLength)
                {
                    Thread.Sleep(pause.Next(2, 40));   // an idle gap: the core leaves boost and must come back to it
                    burst.Restart(); burstLength = NextBurst(pause, variable);
                }
            }
            Interlocked.Add(ref multiplies, done); Interlocked.Add(ref errorsByCore[core.Index], wrong);
            if (pinned && worked > 0) covered[core.Index] = true;
            if (wrong > 0) request.NoteError("Log_Core_Wrong", null, core.Index, wrong);
        }

        while (total.Elapsed < duration)
        {
            foreach (var group in groups)
            {
                if (total.Elapsed >= duration) break;
                if (ct.IsCancellationRequested) return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errorsByCore.Sum(), Describe());
                var workers = group.Select(slot => new Thread(() => Visit(slot)) { IsBackground = true, Name = "Mazesta single-core", Priority = ThreadPriority.AboveNormal }).ToList();
                foreach (var w in workers) w.Start();
                foreach (var w in workers) w.Join();
                visits++;
                request.Progress?.Invoke(new TestProgress(Math.Clamp(total.Elapsed / duration, 0, 1), "Test_Status_Running"));
            }
        }
        if (ct.IsCancellationRequested) return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errorsByCore.Sum(), Describe());
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        long errors = errorsByCore.Sum();
        return new(Definition.Id, Verdict(errors, covered), started, request.Clock.UtcNow, errors, Describe());
    }

    /// <summary>A wrong result fails the test whatever else happened; otherwise it passes only when every core was really tested.</summary>
    internal static TestOutcome Verdict(long errors, bool[] covered) => errors > 0 ? TestOutcome.Failed : covered.All(c => c) ? TestOutcome.Passed : TestOutcome.Inconclusive;

    /// <summary>How long the thread works before the next idle gap: 30 ms to 1.5 s, so boost is entered and left at many different moments.</summary>
    private static double NextBurst(Random r, bool variable) => variable ? 30 + r.NextDouble() * 1470 : double.MaxValue;

    /// <summary>The kinds of work in a visit, in order (the second is named by the vector width the CPU has).</summary>
    private static readonly string[] WorkNames = ["matrix", "vector", "integer"];

    private string Describe(TestExecutionRequest request, DateTimeOffset started, double slice, bool variable, int atOnce, long multiplies, long visits, long[] errorsByCore, List<int> unpinned, bool[] covered, string work, long[] wrongByWork)
    {
        var finished = request.Clock.UtcNow;
        bool hybrid = _cores.Select(c => c.EfficiencyClass).Distinct().Count() > 1;
        var bad = errorsByCore.Select((e, i) => (e, i)).Where(x => x.e > 0).Select(x => $"core {x.i}{(hybrid ? _cores[x.i].EfficiencyClass > 0 ? " (P)" : " (E)" : "")}: {x.e}");
        return SensorEvidence.Join($"single-core cycling over {_cores.Count} physical cores, {slice:F1} s each, {(variable ? "variable" : "steady")} load, matrix {N}x{N}" + (atOnce > 1 ? $", {atOnce} at a time" : ""),
            work, $"core visits={visits}", $"multiplies={multiplies}",
            errorsByCore.Any(e => e > 0) ? "wrong results on " + string.Join(", ", bad) : null,
            wrongByWork.Any(e => e > 0) ? "wrong results by work: " + string.Join(", ", WorkNames.Select((n, k) => $"{n} {wrongByWork[k]}")) : null,
            unpinned.Count > 0 ? $"Windows refused pinning to core(s) {string.Join(", ", unpinned)}" : null,
            covered.Any(x => !x) ? $"not tested: core(s) {string.Join(", ", covered.Select((x, i) => (x, i)).Where(y => !y.x).Select(y => y.i))} - the result covers only the others" : $"every core tested",
            SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuCoreClock, started, finished) is { } clock ? $"peak core clock {clock.Max:F0} MHz" : null,
            SensorEvidence.CpuTemperature(request.Engine, started, finished)?.Format("CPU temperature", "°C", includeMax: true));
    }
}
