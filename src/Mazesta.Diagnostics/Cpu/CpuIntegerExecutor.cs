using System.Diagnostics; using System.Numerics; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Cpu;

/// <summary>
/// Integer load on every logical processor: multiplies, 64-bit divisions, shifts, rotates, xor and a data-dependent branch over a fixed seeded
/// 32 KB array (it stays in the core's own caches, so the integer units, not memory, set the pace). The floating-point tests never reach the
/// divider or the branch predictor this way. Integer arithmetic is exact on every x64 CPU, so each block's checksum must equal the constant
/// worked out in advance (<see cref="Expected"/>, pinned by a unit test): the reference does not come from the machine under test.
/// Operations are counted per element and round (<see cref="OpsPerElement"/>) - integer operations, not floating-point ones.
/// </summary>
public sealed class CpuIntegerExecutor : ITestExecutor
{
    public static readonly TestDefinition Definition = new(new TestId("cpu.integer"), "Test_Cpu_Integer", 300);
    TestDefinition ITestExecutor.Definition => Definition;

    internal const int Elements = 4096, Rounds = 64, OpsPerElement = 14;
    /// <summary>The checksum of one block (<see cref="Block"/>) from the seeded start, pinned by a unit test.</summary>
    internal const ulong Expected = 14063435632275695979UL;

    internal static ulong[] Seed()
    {
        var a = new ulong[Elements]; ulong s = 0x4D415A455354415AUL;
        for (int i = 0; i < a.Length; i++) { s += 0x9E3779B97F4A7C15UL; ulong z = s; z = (z ^ z >> 30) * 0xBF58476D1CE4E5B9UL; z = (z ^ z >> 27) * 0x94D049BB133111EBUL; a[i] = z ^ z >> 31; }
        return a;
    }

    /// <summary>One block: <see cref="Rounds"/> passes over the array (reset to the seed first), and the checksum of the result.</summary>
    internal static ulong Block(ulong[] a, ulong[] seed)
    {
        seed.CopyTo(a, 0);
        for (int r = 0; r < Rounds; r++)
            for (int i = 0; i < a.Length; i++)
            {
                ulong x = a[i], y = a[(i + 1) & (Elements - 1)];
                x = x * 0x2545F4914F6CDD1DUL + (x >> 17);
                x ^= x << 13;
                x = BitOperations.RotateLeft(x, 23);
                x += x / (y | 1);                         // the 64-bit divider
                if ((x & 7) == 3) x ^= y; else x -= y;    // a branch the data decides
                x ^= (ulong)(uint)x * (x >> 32);
                a[i] = x;
            }
        ulong h = 1469598103934665603UL;
        foreach (ulong v in a) h = (h ^ v) * 1099511628211UL;
        return h;
    }

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        return Task.Run(() => Run(request, started, ct), CancellationToken.None);
    }

    private static TestRunResult Run(TestExecutionRequest request, DateTimeOffset started, CancellationToken ct)
    {
        int threads = Environment.ProcessorCount; long blocks = 0, errors = 0; string firstError = ""; var coverage = new CpuCoverage();
        var seed = Seed(); var total = Stopwatch.StartNew(); var duration = TimeSpan.FromSeconds(request.DurationSeconds); var pacer = new LogPacer();
        request.Note("Log_CpuInteger_Start", $"{Elements} × 64-bit values, {Rounds} rounds: x = x·K + (x≫17); x ^= x≪13; x = rotl(x, 23); x += x / (y|1); branch on x&7; x ^= lo32(x)·hi32(x)   check: FNV-1a(result) == expected",
            threads);
        void Worker(int index)
        {
            var a = new ulong[Elements];
            while (!ct.IsCancellationRequested && total.Elapsed < duration)
            {
                if (Block(a, seed) != Expected)
                {
                    Interlocked.Increment(ref errors);
                    if (firstError.Length == 0) { firstError = $"first wrong block on thread {index}"; request.NoteError("Log_Wrong_Result", firstError); }
                }
                Interlocked.Increment(ref blocks); coverage.Mark();
            }
        }
        var all = Task.WhenAll(Enumerable.Range(0, threads).Select(i => Task.Factory.StartNew(() => Worker(i), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)));
        while (!all.IsCompleted)
        {
            request.Progress?.Invoke(new TestProgress(Math.Clamp(total.Elapsed / duration, 0, 1), "Test_Status_Running"));
            if (pacer.Due()) request.Note("Log_CpuInteger_Progress", null, Interlocked.Read(ref blocks), Gops(Interlocked.Read(ref blocks), total.Elapsed.TotalSeconds), Interlocked.Read(ref errors));
            all.Wait(250, CancellationToken.None);
        }
        var finished = request.Clock.UtcNow;
        string detail = SensorEvidence.Join($"integer load (multiply, 64-bit divide, shift, rotate, xor, branch) on {threads} threads, every block checked against a precomputed checksum",
            $"blocks={blocks}", $"{Gops(blocks, total.Elapsed.TotalSeconds):F1} Gop/s (integer)", coverage.Describe(CpuTopology.Cores), firstError.Length > 0 ? firstError : null,
            SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuPackagePower, started, finished)?.Format("CPU package power", " W", includeMax: true),
            SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuCoreClock, started, finished)?.Format("CPU core clock", " MHz", includeMax: true),
            SensorEvidence.CpuTemperature(request.Engine, started, finished)?.Format("CPU temperature", "°C", includeMax: true));
        if (ct.IsCancellationRequested) return new(Definition.Id, TestOutcome.Cancelled, started, finished, errors, detail);
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, finished, errors, detail);
    }

    private static double Gops(long blocks, double seconds) => blocks * (double)Elements * Rounds * OpsPerElement / Math.Max(0.001, seconds) / 1e9;
}
