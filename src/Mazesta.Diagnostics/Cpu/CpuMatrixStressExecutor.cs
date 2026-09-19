using System.Diagnostics; using Mazesta.Core.Hardware; using Mazesta.Monitoring;
namespace Mazesta.Diagnostics.Cpu;

/// <summary>Real integer-and-floating-point CPU+memory workload: repeated dense matrix multiplication
/// with independent spot-check verification of the result (spec §9: "الگوریتم تست باید صحت خروجی
/// محاسبه/حافظه را هم بررسی کند و errors count ثبت شود"). Deliberately named "matrix load" (NameKey
/// Test_Cpu_Matrix), not Linpack - spec §9 explicitly forbids labelling a simple multiply as Linpack; a
/// real Linpack-equivalent (LU factorisation + residual) is a separate, later executor.</summary>
public sealed class CpuMatrixStressExecutor : ITestExecutor
{
    public static readonly TestDefinition Definition = new(new TestId("cpu.matrix"), "Test_Cpu_Matrix", DefaultDurationSeconds: 60);
    TestDefinition ITestExecutor.Definition => Definition;

    private const int MatrixSize = 64;
    private readonly int _threadCount;

    public CpuMatrixStressExecutor(int? threadCount = null) => _threadCount = threadCount is > 0 ? threadCount.Value : Environment.ProcessorCount;

    public async Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0)
            return TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive.");

        long totalIterations = 0, totalErrors = 0;
        var sw = Stopwatch.StartNew();
        var duration = TimeSpan.FromSeconds(request.DurationSeconds);

        void RunWorker()
        {
            var rng = new Random(unchecked(Environment.CurrentManagedThreadId * 397) ^ Environment.TickCount);
            var a = new double[MatrixSize, MatrixSize]; var b = new double[MatrixSize, MatrixSize]; var c = new double[MatrixSize, MatrixSize];
            while (!ct.IsCancellationRequested && sw.Elapsed < duration)
            {
                Fill(a, rng); Fill(b, rng);
                Multiply(a, b, c);
                if (!VerifySpotChecks(a, b, c, rng)) Interlocked.Increment(ref totalErrors);
                Interlocked.Increment(ref totalIterations);
            }
        }

        var workers = new Task[_threadCount];
        for (int t = 0; t < _threadCount; t++) workers[t] = Task.Run(RunWorker);
        var allDone = Task.WhenAll(workers);
        while (!allDone.IsCompleted)
        {
            request.Progress?.Invoke(new TestProgress(Math.Clamp(sw.Elapsed.TotalSeconds / duration.TotalSeconds, 0, 1), "Test_Status_Running"));
            await Task.WhenAny(allDone, Task.Delay(250)).ConfigureAwait(false);
        }
        await allDone.ConfigureAwait(false);
        request.Progress?.Invoke(new TestProgress(1.0, "Test_Status_Running"));

        var finished = request.Clock.UtcNow;
        if (ct.IsCancellationRequested)
            return new TestRunResult(Definition.Id, TestOutcome.Cancelled, started, finished, totalErrors, $"threads={_threadCount}; iterations={totalIterations}");

        string? load = DescribeMeasuredLoad(request.Engine, started, finished);
        string detail = $"matrix load {MatrixSize}x{MatrixSize}; threads={_threadCount}; iterations={totalIterations}" + (load is null ? "" : $"; {load}");
        return new TestRunResult(Definition.Id, totalErrors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, finished, totalErrors, detail);
    }

    /// <summary>Spec §8: "نمره سرعت به‌تنهایی PASS سلامت نیست" - a pass must be backed by measured
    /// evidence of real load, not the executor's own say-so. Reads the CPU total-load sensor's already
    /// recorded history for exactly this run's time window; returns null (never a fabricated number)
    /// when there is no engine, no such sensor, or no sample fell inside the window.</summary>
    private static string? DescribeMeasuredLoad(PollingEngine? engine, DateTimeOffset started, DateTimeOffset finished)
    {
        if (engine is null) return null;
        var loadSensors = engine.Hardware.Where(n => n.Kind == HardwareKind.Cpu).SelectMany(n => n.Sensors).Where(s => s.Role == SensorRole.CpuTotalLoad).ToList();
        if (loadSensors.Count == 0) return null;
        int startSec = engine.History.SecondsSinceEpoch(started), endSec = engine.History.SecondsSinceEpoch(finished);
        var samples = new List<float>();
        foreach (var s in loadSensors)
        {
            var raw = engine.History.GetRaw(s.Id);
            for (int i = 0; i < raw.Seconds.Length; i++)
                if (raw.Seconds[i] >= startSec && raw.Seconds[i] <= endSec && !float.IsNaN(raw.Values[i])) samples.Add(raw.Values[i]);
        }
        return samples.Count == 0 ? null : $"measured CPU load avg {samples.Average():F1}% (n={samples.Count})";
    }

    // internal, not private: CpuMatrixStressExecutorTests exercises VerifySpotChecks directly against a
    // deliberately corrupted cell, so the "does it actually catch a fault" claim is verified rather than
    // just trusted (a whole-RunAsync test cannot demonstrate this - a healthy dev box never corrupts a
    // real multiply, so the failure path would otherwise be unreachable in any automated run).
    internal static void Fill(double[,] m, Random rng) { for (int i = 0; i < MatrixSize; i++) for (int j = 0; j < MatrixSize; j++) m[i, j] = rng.NextDouble() * 2 - 1; }

    internal static void Multiply(double[,] a, double[,] b, double[,] c)
    {
        for (int i = 0; i < MatrixSize; i++)
            for (int j = 0; j < MatrixSize; j++)
            {
                double sum = 0;
                for (int k = 0; k < MatrixSize; k++) sum += a[i, k] * b[k, j];
                c[i, j] = sum;
            }
    }

    /// <summary>Recomputes a handful of C's cells directly and compares bit-for-bit against what
    /// Multiply produced for the same inputs. Both computations use the identical operation order, so a
    /// healthy CPU/RAM must reproduce the exact same double - any mismatch is real, observed corruption,
    /// not floating-point rounding noise, which is why this compares with == rather than a tolerance.</summary>
    internal static bool VerifySpotChecks(double[,] a, double[,] b, double[,] c, Random rng)
    {
        for (int k = 0; k < 4; k++)
        {
            int i = rng.Next(MatrixSize), j = rng.Next(MatrixSize);
            double expected = 0;
            for (int x = 0; x < MatrixSize; x++) expected += a[i, x] * b[x, j];
            if (expected != c[i, j]) return false;
        }
        return true;
    }
}
