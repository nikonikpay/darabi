using System.Diagnostics; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>Double-precision throughput of the same 64x64 matrix multiply the CPU load test uses: first one thread,
/// then every logical processor, each for half the duration. GFLOPS counts 2·n³ operations per multiply.
/// The average core clock and package temperature over the run come from the monitor, when it has them.</summary>
public sealed class CpuBenchmark : IBenchmark
{
    public static readonly TestDefinition Spec = new(new TestId("bench.cpu"), "Bench_Cpu", 20);
    public TestDefinition Definition => Spec;
    private const int N = CpuMatrixStressExecutor.MatrixSize;
    private const double FlopsPerMultiply = 2.0 * N * N * N;

    public async Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return BenchmarkResult.Unsupported(Spec.Id, started, "Duration must be positive.");
        var phase = TimeSpan.FromSeconds(request.DurationSeconds / 2.0); int threads = Environment.ProcessorCount;
        var overall = Stopwatch.StartNew();
        void Progress() => request.Progress?.Invoke(new TestProgress(Math.Clamp(overall.Elapsed.TotalSeconds / request.DurationSeconds, 0, 1), "Test_Status_Running"));

        double single = await MeasureAsync(1, phase, ct, Progress).ConfigureAwait(false);
        double multi = ct.IsCancellationRequested ? 0 : await MeasureAsync(threads, phase, ct, Progress).ConfigureAwait(false);
        var finished = request.Clock.UtcNow;
        if (ct.IsCancellationRequested) return new(Spec.Id, BenchmarkStatus.Cancelled, started, finished, [], null);

        List<BenchmarkMetric> metrics = [new("Bench_Cpu_Single", single, "GFLOPS"), new("Bench_Cpu_Multi", multi, "GFLOPS"), new("Bench_Threads", threads, "")];
        if (SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuEffectiveClockAverage, started, finished) is { } clock) metrics.Add(new("Bench_Cpu_Clock", clock.Average, "MHz"));
        if (SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuPackageTemp, started, finished) is { } temp) metrics.Add(new("Bench_Cpu_TempMax", temp.Max, "°C"));
        return new(Spec.Id, BenchmarkStatus.Completed, started, finished, metrics, $"matrix {N}x{N} double; single thread then {threads} threads");
    }

    private static async Task<double> MeasureAsync(int threads, TimeSpan duration, CancellationToken ct, Action progress)
    {
        long multiplies = 0; var sw = Stopwatch.StartNew();
        void Worker()
        {
            var rng = new Random(unchecked(Environment.CurrentManagedThreadId * 397) ^ Environment.TickCount);
            var a = new double[N, N]; var b = new double[N, N]; var c = new double[N, N];
            CpuMatrixStressExecutor.Fill(a, rng); CpuMatrixStressExecutor.Fill(b, rng);
            long mine = 0;
            while (!ct.IsCancellationRequested && sw.Elapsed < duration) { CpuMatrixStressExecutor.Multiply(a, b, c); mine++; }
            Interlocked.Add(ref multiplies, mine);
        }
        var all = Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(Worker)));
        while (!all.IsCompleted) { progress(); await Task.WhenAny(all, Task.Delay(250)).ConfigureAwait(false); }
        await all.ConfigureAwait(false);
        return multiplies * FlopsPerMultiply / Math.Max(0.001, sw.Elapsed.TotalSeconds) / 1e9;
    }
}
