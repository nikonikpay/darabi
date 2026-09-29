using System.Diagnostics; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Cpu;

/// <summary>Real floating-point CPU+memory workload: repeated dense 64x64 matrix multiplication on every logical processor, every product
/// checked in full (spec §9: "الگوریتم تست باید صحت خروجی محاسبه/حافظه را هم بررسی کند و errors count ثبت شود"). The inputs are
/// <see cref="Sets"/> fixed seeded pairs, and a product's every cell goes into a checksum of its exact bits that must equal the value worked
/// out in advance for that pair (<see cref="Expected"/>). A multiply in a fixed order of IEEE operations gives the same bits on any x64 CPU,
/// so the reference does not come from the machine under test: a wrong first result cannot become the yardstick, and one flipped bit in
/// one cell is caught. Deliberately named "matrix load" (NameKey Test_Cpu_Matrix), not Linpack - that is <see cref="LinpackExecutor"/>.
/// FLOPS count 2n³ per multiply (one multiply and one add per term).</summary>
public sealed class CpuMatrixStressExecutor : ITestExecutor
{
    public static readonly TestDefinition Definition = new(new TestId("cpu.matrix"), "Test_Cpu_Matrix", 60);
    TestDefinition ITestExecutor.Definition => Definition;

    internal const int MatrixSize = 64, Sets = 4;
    /// <summary>The checksum (<see cref="Checksum"/>) of set k's product, computed once off the machine under test and pinned by a unit test.</summary>
    internal static readonly ulong[] Expected = [9064527581815777477UL, 10314864045855360163UL, 10710372662706872264UL, 1936444054680412613UL];
    private readonly int _threadCount;

    public CpuMatrixStressExecutor(int? threadCount = null) => _threadCount = threadCount is > 0 ? threadCount.Value : Environment.ProcessorCount;

    /// <summary>Set k's two input matrices, the same on every run and every machine (Random with a seed is stable across .NET versions).</summary>
    internal static (double[,] A, double[,] B) Inputs(int set)
    {
        var a = new double[MatrixSize, MatrixSize]; var b = new double[MatrixSize, MatrixSize];
        Fill(a, new Random(20260928 + set * 7919)); Fill(b, new Random(19450808 + set * 104729));
        return (a, b);
    }

    public async Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0)
            return TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive.");

        long totalIterations = 0, totalErrors = 0; string firstError = "";
        var inputs = Enumerable.Range(0, Sets).Select(Inputs).ToArray();
        request.Note("Log_CpuMatrix_Start", "C[i,j] = Σk A[i,k]·B[k,j]   check: FNV-1a(bits of every C[i,j]) == expected[set]   FLOPs = 2·n³ per product", _threadCount, MatrixSize);
        var sw = Stopwatch.StartNew(); var pacer = new LogPacer();
        var duration = TimeSpan.FromSeconds(request.DurationSeconds);

        void RunWorker(int worker)
        {
            var c = new double[MatrixSize, MatrixSize];
            for (long i = worker; !ct.IsCancellationRequested && sw.Elapsed < duration; i++)
            {
                int set = (int)(i % Sets);
                Multiply(inputs[set].A, inputs[set].B, c);
                if (Checksum(c) != Expected[set])
                {
                    Interlocked.Increment(ref totalErrors);
                    if (firstError.Length == 0) { firstError = $"first wrong product: thread {worker}, input set {set}, {WrongCells(inputs[set].A, inputs[set].B, c)} cell(s) differ from a recomputation"; request.NoteError("Log_Wrong_Result", firstError); }
                }
                Interlocked.Increment(ref totalIterations);
            }
        }

        var workers = new Task[_threadCount];
        for (int t = 0; t < _threadCount; t++) { int worker = t; workers[t] = Task.Run(() => RunWorker(worker)); }
        var allDone = Task.WhenAll(workers);
        while (!allDone.IsCompleted)
        {
            request.Progress?.Invoke(new TestProgress(Math.Clamp(sw.Elapsed.TotalSeconds / duration.TotalSeconds, 0, 1), "Test_Status_Running"));
            if (pacer.Due()) request.Note("Log_CpuMatrix_Progress", null, Interlocked.Read(ref totalIterations), Interlocked.Read(ref totalIterations) * 2.0 * MatrixSize * MatrixSize * MatrixSize / Math.Max(0.001, sw.Elapsed.TotalSeconds) / 1e9, Interlocked.Read(ref totalErrors));
            await Task.WhenAny(allDone, Task.Delay(250)).ConfigureAwait(false);
        }
        await allDone.ConfigureAwait(false);
        double seconds = Math.Max(0.001, sw.Elapsed.TotalSeconds);
        request.Progress?.Invoke(new TestProgress(1.0, "Test_Status_Running"));

        var finished = request.Clock.UtcNow;
        if (ct.IsCancellationRequested)
            return new TestRunResult(Definition.Id, TestOutcome.Cancelled, started, finished, totalErrors, $"threads={_threadCount}; iterations={totalIterations}");

        string detail = SensorEvidence.Join($"matrix load {MatrixSize}x{MatrixSize}, {Sets} fixed input sets, every product checked in full against a precomputed checksum", $"threads={_threadCount}", $"iterations={totalIterations}",
            $"{totalIterations * 2.0 * MatrixSize * MatrixSize * MatrixSize / seconds / 1e9:F1} GFLOPS (FP64, scalar)", firstError.Length > 0 ? firstError : null,
            SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuTotalLoad, started, finished)?.Format("measured CPU load", "%"),
            SensorEvidence.CpuTemperature(request.Engine, started, finished)?.Format("CPU temperature", "°C", includeMax: true));
        return new TestRunResult(Definition.Id, totalErrors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, finished, totalErrors, detail);
    }

    // internal, not private: the tests corrupt a single bit of a single cell and require the check to catch it, so the claim that it finds
    // a fault is verified rather than trusted (a healthy machine never corrupts a real multiply, so no whole-run test can reach that path).
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

    /// <summary>The exact bits of every cell (FNV-1a over the doubles): the same product always gives the same sum, one wrong bit anywhere does not.</summary>
    internal static ulong Checksum(double[,] m)
    {
        ulong h = 1469598103934665603UL;
        foreach (double v in m) h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(v)) * 1099511628211UL;
        return h;
    }

    /// <summary>How many cells of <paramref name="c"/> differ from a fresh multiply, for the error's description only (a transient fault may not repeat).</summary>
    private static int WrongCells(double[,] a, double[,] b, double[,] c)
    {
        var again = new double[MatrixSize, MatrixSize]; Multiply(a, b, again); int wrong = 0;
        for (int i = 0; i < MatrixSize; i++) for (int j = 0; j < MatrixSize; j++) if (BitConverter.DoubleToInt64Bits(again[i, j]) != BitConverter.DoubleToInt64Bits(c[i, j])) wrong++;
        return wrong;
    }
}
