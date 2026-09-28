using System.Diagnostics; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>Double-precision throughput of the same 64x64 matrix multiply the CPU load test uses, on one thread or on
/// every logical processor - two separate benchmarks, as single-core and multi-core are reported everywhere else, so
/// each gets the whole duration and its own clock and temperature. GFLOPS counts 2·n³ operations per multiply.</summary>
public sealed class CpuBenchmark(bool allThreads) : IBenchmark
{
    public static readonly TestDefinition Single = new(new TestId("bench.cpu.single"), "Bench_Cpu_Single", 60);
    public static readonly TestDefinition Multi = new(new TestId("bench.cpu.multi"), "Bench_Cpu_Multi", 60);
    public TestDefinition Definition => allThreads ? Multi : Single;
    public HardwareKind Component => HardwareKind.Cpu;
    private const int N = CpuMatrixStressExecutor.MatrixSize;
    private const double FlopsPerMultiply = 2.0 * N * N * N;

    public async Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return BenchmarkResult.Unsupported(Definition.Id, started, "Duration must be positive.");
        int threads = allThreads ? Environment.ProcessorCount : 1;
        double gflops = await MeasureAsync(threads, TimeSpan.FromSeconds(request.DurationSeconds), request, ct).ConfigureAwait(false);
        var finished = request.Clock.UtcNow;
        if (ct.IsCancellationRequested) return BenchmarkResult.Cancelled(Definition.Id, started, finished);

        List<BenchmarkMetric> metrics = [new("Bench_Cpu_Gflops", gflops, "GFLOPS")];
        // One busy thread lifts one core, so its peak clock is the single-thread number; all threads load every core evenly.
        if (allThreads)
        {
            metrics.AddRange([new("Bench_Cpu_PerThread", gflops / threads, "GFLOPS"), new("Bench_Threads", threads, "")]);
            metrics.AddSensor(request, HardwareKind.Cpu, SensorRole.CpuEffectiveClockAverage, started, finished, "Bench_Cpu_Clock", Unit.MegaHertz);
        }
        else metrics.AddSensor(request, HardwareKind.Cpu, SensorRole.CpuEffectiveClock, started, finished, "Bench_Cpu_ClockPeak", Unit.MegaHertz, peak: true);
        metrics.AddSensor(request, HardwareKind.Cpu, SensorRole.CpuPackagePower, started, finished, "Bench_Cpu_Power", Unit.Watt);
        if (SensorEvidence.CpuTemperature(request.Engine, started, finished) is { } temp) metrics.Add(new("Bench_Cpu_TempMax", temp.Max, Units.Symbol(Unit.Celsius)));
        return new(Definition.Id, BenchmarkStatus.Completed, started, finished, metrics, $"matrix {N}x{N} double on {threads} thread(s) for {request.DurationSeconds} s");
    }

    private static async Task<double> MeasureAsync(int threads, TimeSpan duration, TestExecutionRequest request, CancellationToken ct)
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
        var all = Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Factory.StartNew(Worker, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)));
        while (!all.IsCompleted) { request.Report(sw.Elapsed / duration); await Task.WhenAny(all, Task.Delay(250, CancellationToken.None)).ConfigureAwait(false); }
        await all.ConfigureAwait(false);
        return multiplies * FlopsPerMultiply / Math.Max(0.001, sw.Elapsed.TotalSeconds) / 1e9;
    }
}
