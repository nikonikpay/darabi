using System.Diagnostics; using System.Runtime.Intrinsics; using System.Runtime.Intrinsics.X86; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Cpu;

/// <summary>
/// Maximum-heat vector load (what OCCT's AVX2/AVX-512 modes and Prime95's small FFTs are used for): every logical processor runs long
/// chains of fused multiply-adds on the widest vectors the CPU has - AVX-512, AVX2 or SSE2 - on data that never leaves the registers, so
/// the execution units, not memory, set the pace and the CPU draws the most power it can. That is where a weak VRM, a poor cooler or an
/// undervolt that survives ordinary loads gives out.
/// Each block of work starts from the same seeded values, so its result is known after the first pass; a thread whose block result ever
/// differs has computed wrongly and is counted. The chosen width is Unsupported (not failed) when the CPU lacks it.
/// </summary>
public sealed class CpuVectorStressExecutor : ITestExecutor, ITestAvailability
{
    public const string WidthOption = "width";
    public static readonly TestDefinition Definition = new(new TestId("cpu.vector"), "Test_Cpu_Vector", 120,
    [
        new TestOption(WidthOption, "Test_Option_VectorWidth", TestOptionKind.Choice, "auto", () =>
            [new("auto", "Test_Vector_Auto", true), new("avx512", "AVX-512"), new("avx2", "AVX2 + FMA"), new("sse", "SSE2")]),
    ]);
    TestDefinition ITestExecutor.Definition => Definition;

    private const int Chains = 12, BlockIterations = 20_000;
    private const double Scale = 0.9999999, Offset = 1e-7;

    public enum Width { Sse, Avx2, Avx512 }

    internal static Width? Resolve(string choice) => choice switch
    {
        "avx512" => Avx512F.IsSupported ? Width.Avx512 : null,
        "avx2" => Avx2.IsSupported && Fma.IsSupported ? Width.Avx2 : null,
        "sse" => Sse2.IsSupported ? Width.Sse : null,
        _ => Avx512F.IsSupported ? Width.Avx512 : Avx2.IsSupported && Fma.IsSupported ? Width.Avx2 : Sse2.IsSupported ? Width.Sse : null,
    };

    public Unavailability? CheckAvailability(TestOptions options)
        => Resolve(options.Get(WidthOption)) is null ? new("Test_Unavailable_NoVectorWidth", $"The CPU does not support '{options.Get(WidthOption)}'.") : null;

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        string choice = (request.Options ?? TestOptions.None(Definition)).Get(WidthOption);
        if (Resolve(choice) is not { } width) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, $"The CPU does not support '{choice}'."));
        return Task.Run(() => Run(request, width, started, ct), CancellationToken.None);
    }

    private static TestRunResult Run(TestExecutionRequest request, Width width, DateTimeOffset started, CancellationToken ct)
    {
        int threads = Environment.ProcessorCount; long blocks = 0, errors = 0;
        var total = Stopwatch.StartNew(); var duration = TimeSpan.FromSeconds(request.DurationSeconds);
        void Worker(int index)
        {
            double? reference = null;
            while (!ct.IsCancellationRequested && total.Elapsed < duration)
            {
                double result = width switch { Width.Avx512 => Block512(), Width.Avx2 => Block256(), _ => Block128() };
                reference ??= result;
                if (BitConverter.DoubleToInt64Bits(result) != BitConverter.DoubleToInt64Bits(reference.Value)) Interlocked.Increment(ref errors);
                Interlocked.Increment(ref blocks);
            }
        }
        var all = Task.WhenAll(Enumerable.Range(0, threads).Select(i => Task.Factory.StartNew(() => Worker(i), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)));
        while (!all.IsCompleted) { request.Progress?.Invoke(new TestProgress(Math.Clamp(total.Elapsed / duration, 0, 1), "Test_Status_Running")); all.Wait(250, CancellationToken.None); }
        var finished = request.Clock.UtcNow;
        long lanes = width switch { Width.Avx512 => 8, Width.Avx2 => 4, _ => 2 };
        double gflops = blocks * (double)BlockIterations * Chains * lanes * 2 / Math.Max(0.001, total.Elapsed.TotalSeconds) / 1e9;
        string detail = SensorEvidence.Join($"vector FMA stress, {width switch { Width.Avx512 => "AVX-512", Width.Avx2 => "AVX2 + FMA", _ => "SSE2" }}, {threads} threads",
            $"blocks={blocks}", $"{gflops:F0} GFLOPS (FP64)", errors > 0 ? $"{errors} block(s) computed a different result" : null,
            SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuPackagePower, started, finished)?.Format("CPU package power", " W", includeMax: true),
            SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuEffectiveClockAverage, started, finished)?.Format("average effective clock", " MHz"),
            SensorEvidence.CpuTemperature(request.Engine, started, finished)?.Format("CPU temperature", "°C", includeMax: true));
        if (ct.IsCancellationRequested) return new(Definition.Id, TestOutcome.Cancelled, started, finished, errors, detail);
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, finished, errors, detail);
    }

    // Twelve independent chains hide the multiply-add latency so every cycle issues one; x = x·s + o converges to o/(1−s) and stays a normal
    // number, so no denormal slow path or overflow ever changes the timing or the result.
    private static double Block512()
    {
        var s = Vector512.Create(Scale); var o = Vector512.Create(Offset);
        Vector512<double> Seed(int c) => Vector512.Create(1.0 + c * 0.01, 1.1 + c * 0.01, 1.2 + c * 0.01, 1.3 + c * 0.01, 1.4 + c * 0.01, 1.5 + c * 0.01, 1.6 + c * 0.01, 1.7 + c * 0.01);
        Vector512<double> x0 = Seed(0), x1 = Seed(1), x2 = Seed(2), x3 = Seed(3), x4 = Seed(4), x5 = Seed(5), x6 = Seed(6), x7 = Seed(7), x8 = Seed(8), x9 = Seed(9), x10 = Seed(10), x11 = Seed(11);
        for (int i = 0; i < BlockIterations; i++)
        {
            x0 = Avx512F.FusedMultiplyAdd(x0, s, o); x1 = Avx512F.FusedMultiplyAdd(x1, s, o); x2 = Avx512F.FusedMultiplyAdd(x2, s, o); x3 = Avx512F.FusedMultiplyAdd(x3, s, o);
            x4 = Avx512F.FusedMultiplyAdd(x4, s, o); x5 = Avx512F.FusedMultiplyAdd(x5, s, o); x6 = Avx512F.FusedMultiplyAdd(x6, s, o); x7 = Avx512F.FusedMultiplyAdd(x7, s, o);
            x8 = Avx512F.FusedMultiplyAdd(x8, s, o); x9 = Avx512F.FusedMultiplyAdd(x9, s, o); x10 = Avx512F.FusedMultiplyAdd(x10, s, o); x11 = Avx512F.FusedMultiplyAdd(x11, s, o);
        }
        return Vector512.Sum(x0 + x1 + x2 + x3 + x4 + x5 + x6 + x7 + x8 + x9 + x10 + x11);
    }

    private static double Block256()
    {
        var s = Vector256.Create(Scale); var o = Vector256.Create(Offset);
        Vector256<double> Seed(int c) => Vector256.Create(1.0 + c * 0.01, 1.1 + c * 0.01, 1.2 + c * 0.01, 1.3 + c * 0.01);
        Vector256<double> x0 = Seed(0), x1 = Seed(1), x2 = Seed(2), x3 = Seed(3), x4 = Seed(4), x5 = Seed(5), x6 = Seed(6), x7 = Seed(7), x8 = Seed(8), x9 = Seed(9), x10 = Seed(10), x11 = Seed(11);
        for (int i = 0; i < BlockIterations; i++)
        {
            x0 = Fma.MultiplyAdd(x0, s, o); x1 = Fma.MultiplyAdd(x1, s, o); x2 = Fma.MultiplyAdd(x2, s, o); x3 = Fma.MultiplyAdd(x3, s, o);
            x4 = Fma.MultiplyAdd(x4, s, o); x5 = Fma.MultiplyAdd(x5, s, o); x6 = Fma.MultiplyAdd(x6, s, o); x7 = Fma.MultiplyAdd(x7, s, o);
            x8 = Fma.MultiplyAdd(x8, s, o); x9 = Fma.MultiplyAdd(x9, s, o); x10 = Fma.MultiplyAdd(x10, s, o); x11 = Fma.MultiplyAdd(x11, s, o);
        }
        return Vector256.Sum(x0 + x1 + x2 + x3 + x4 + x5 + x6 + x7 + x8 + x9 + x10 + x11);
    }

    /// <summary>SSE2 has no fused multiply-add: a multiply and an add, the load of CPUs from before AVX2.</summary>
    private static double Block128()
    {
        var s = Vector128.Create(Scale); var o = Vector128.Create(Offset);
        Vector128<double> Seed(int c) => Vector128.Create(1.0 + c * 0.01, 1.1 + c * 0.01);
        Vector128<double> x0 = Seed(0), x1 = Seed(1), x2 = Seed(2), x3 = Seed(3), x4 = Seed(4), x5 = Seed(5), x6 = Seed(6), x7 = Seed(7), x8 = Seed(8), x9 = Seed(9), x10 = Seed(10), x11 = Seed(11);
        for (int i = 0; i < BlockIterations; i++)
        {
            x0 = Sse2.Add(Sse2.Multiply(x0, s), o); x1 = Sse2.Add(Sse2.Multiply(x1, s), o); x2 = Sse2.Add(Sse2.Multiply(x2, s), o); x3 = Sse2.Add(Sse2.Multiply(x3, s), o);
            x4 = Sse2.Add(Sse2.Multiply(x4, s), o); x5 = Sse2.Add(Sse2.Multiply(x5, s), o); x6 = Sse2.Add(Sse2.Multiply(x6, s), o); x7 = Sse2.Add(Sse2.Multiply(x7, s), o);
            x8 = Sse2.Add(Sse2.Multiply(x8, s), o); x9 = Sse2.Add(Sse2.Multiply(x9, s), o); x10 = Sse2.Add(Sse2.Multiply(x10, s), o); x11 = Sse2.Add(Sse2.Multiply(x11, s), o);
        }
        return Vector128.Sum(x0 + x1 + x2 + x3 + x4 + x5 + x6 + x7 + x8 + x9 + x10 + x11);
    }
}
