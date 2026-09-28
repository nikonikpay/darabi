using System.Diagnostics; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Cpu;

/// <summary>
/// Single-core stability, one physical core at a time: one thread is pinned to a core's
/// first logical processor, runs the verified matrix workload, then moves to the next core, round and round until the time is up. A lone
/// busy thread lets a core reach its highest boost clock and lowest voltage - the state where an unstable boost / Curve Optimizer / undervolt
/// setting fails, and which an all-core load never reaches. With the variable load the thread also pauses at random moments, so the core
/// keeps dropping out of and back into boost, which is when marginal voltage steps show.
/// Every core computes the same seeded matrices, so a core whose result differs from the reference (taken on the first core) is named in
/// the result: a wrong answer is an error, not a crash the technician has to guess about.
/// </summary>
public sealed class CpuCoreCycleExecutor : ITestExecutor
{
    public const string SecondsPerCoreOption = "secondsPerCore", LoadOption = "load";
    public static readonly TestDefinition Definition = new(new TestId("cpu.singlecore"), "Test_Cpu_SingleCore", 120,
    [
        new TestOption(SecondsPerCoreOption, "Test_Option_SecondsPerCore", TestOptionKind.Integer, "10"),
        new TestOption(LoadOption, "Test_Option_Load", TestOptionKind.Choice, "variable", () => [new("variable", "Test_Load_Variable", true), new("steady", "Test_Load_Steady", true)]),
    ]);
    TestDefinition ITestExecutor.Definition => Definition;

    private const int N = CpuMatrixStressExecutor.MatrixSize;
    private readonly IReadOnlyList<CpuCore> _cores;
    public CpuCoreCycleExecutor(IReadOnlyList<CpuCore>? cores = null) => _cores = cores ?? CpuTopology.Cores;

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        if (_cores.Count == 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Windows reported no processor cores."));
        var options = request.Options ?? TestOptions.None(Definition);
        // Every core gets its turn inside the chosen length: a short run shortens each core's slice rather than leaving cores untested.
        double slice = Math.Max(1, Math.Min(Math.Max(1, options.GetInt(SecondsPerCoreOption)), request.DurationSeconds / (double)_cores.Count));
        bool variable = options.Get(LoadOption) != "steady";
        var done = new TaskCompletionSource<TestRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        // A thread of its own: the pin to a core outlives each work item, which must never happen to a pool thread.
        var thread = new Thread(() => { try { done.SetResult(Run(request, started, slice, variable, ct)); } catch (Exception e) { done.SetException(e); } })
        { IsBackground = true, Name = "Mazesta single-core", Priority = ThreadPriority.AboveNormal };
        thread.Start();
        return done.Task;
    }

    private TestRunResult Run(TestExecutionRequest request, DateTimeOffset started, double slice, bool variable, CancellationToken ct)
    {
        var a = new double[N, N]; var b = new double[N, N]; var c = new double[N, N];
        CpuMatrixStressExecutor.Fill(a, new Random(20260928)); CpuMatrixStressExecutor.Fill(b, new Random(19450808));
        ulong? reference = null; var errorsByCore = new long[_cores.Count]; var unpinned = new List<int>();
        long multiplies = 0, visits = 0; var total = Stopwatch.StartNew(); var duration = TimeSpan.FromSeconds(request.DurationSeconds);
        var pause = new Random(Environment.TickCount);
        try
        {
            while (total.Elapsed < duration)
            {
                foreach (var core in _cores)
                {
                    if (total.Elapsed >= duration) break;
                    ct.ThrowIfCancellationRequested();
                    if (!CpuTopology.Pin(core.Group, core.FirstThreadMask) && !unpinned.Contains(core.Index)) unpinned.Add(core.Index);
                    Thread.Sleep(0);   // let the scheduler move the thread onto the core before the slice is timed
                    visits++;
                    var step = Stopwatch.StartNew(); var burst = Stopwatch.StartNew(); double burstLength = NextBurst(pause, variable);
                    while (step.Elapsed.TotalSeconds < slice && total.Elapsed < duration)
                    {
                        ct.ThrowIfCancellationRequested();
                        CpuMatrixStressExecutor.Multiply(a, b, c); multiplies++;
                        ulong sum = Checksum(c);
                        if (reference is null) reference = sum;
                        else if (sum != reference) errorsByCore[core.Index]++;
                        if (variable && burst.Elapsed.TotalMilliseconds > burstLength)
                        {
                            Thread.Sleep(pause.Next(2, 40));   // an idle gap: the core leaves boost and must come back to it
                            burst.Restart(); burstLength = NextBurst(pause, variable);
                        }
                    }
                    request.Progress?.Invoke(new TestProgress(Math.Clamp(total.Elapsed / duration, 0, 1), "Test_Status_Running"));
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errorsByCore.Sum(), Describe(request, started, slice, variable, multiplies, visits, errorsByCore, unpinned));
        }
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        long errors = errorsByCore.Sum();
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, errors, Describe(request, started, slice, variable, multiplies, visits, errorsByCore, unpinned));
    }

    /// <summary>How long the thread works before the next idle gap: 30 ms to 1.5 s, so boost is entered and left at many different moments.</summary>
    private static double NextBurst(Random r, bool variable) => variable ? 30 + r.NextDouble() * 1470 : double.MaxValue;

    /// <summary>The exact bits of every cell: two cores that computed the same product give the same sum, one wrong bit anywhere does not.</summary>
    internal static ulong Checksum(double[,] m)
    {
        ulong h = 1469598103934665603UL;
        foreach (double v in m) h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(v)) * 1099511628211UL;
        return h;
    }

    private string Describe(TestExecutionRequest request, DateTimeOffset started, double slice, bool variable, long multiplies, long visits, long[] errorsByCore, List<int> unpinned)
    {
        var finished = request.Clock.UtcNow;
        bool hybrid = _cores.Select(c => c.EfficiencyClass).Distinct().Count() > 1;
        var bad = errorsByCore.Select((e, i) => (e, i)).Where(x => x.e > 0).Select(x => $"core {x.i}{(hybrid ? _cores[x.i].EfficiencyClass > 0 ? " (P)" : " (E)" : "")}: {x.e}");
        return SensorEvidence.Join($"single-core cycling over {_cores.Count} physical cores, {slice:F1} s each, {(variable ? "variable" : "steady")} load, matrix {N}x{N}",
            $"core visits={visits}", $"multiplies={multiplies}",
            errorsByCore.Any(e => e > 0) ? "wrong results on " + string.Join(", ", bad) : null,
            unpinned.Count > 0 ? $"Windows refused pinning to core(s) {string.Join(", ", unpinned)}" : null,
            SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuCoreClock, started, finished) is { } clock ? $"peak core clock {clock.Max:F0} MHz" : null,
            SensorEvidence.CpuTemperature(request.Engine, started, finished)?.Format("CPU temperature", "°C", includeMax: true));
    }
}
