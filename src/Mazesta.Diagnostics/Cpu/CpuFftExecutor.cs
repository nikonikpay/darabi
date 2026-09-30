using System.Diagnostics; using System.Numerics; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Cpu;

/// <summary>
/// FFT load, the kind of work Prime95 is built on: a radix-2 complex FFT on every logical processor, alternating a size that fits the core's
/// caches (4096 points, 64 KB) and one that does not (1 048 576 points, 16 MB), so both the floating-point units and the memory path are loaded.
/// Correctness is checked three ways, none of which trusts one run of the code on this machine alone:
///  - before the run the implementation is compared with a plain O(n²) DFT (a different algorithm) on 512 points;
///  - each size's reference output is accepted only if its inverse gives the input back and its energy matches the input's (Parseval) to
///    within 1e-9 of the size;
///  - every later transform must equal that reference bit for bit (same input, same order of operations).
/// FLOPS use the usual FFT convention, 5·N·log2 N per transform.
/// </summary>
public sealed class CpuFftExecutor : ITestExecutor
{
    public static readonly TestDefinition Definition = new(new TestId("cpu.fft"), "Test_Cpu_Fft", 60);
    TestDefinition ITestExecutor.Definition => Definition;

    internal const int Small = 1 << 12, Large = 1 << 20;

    /// <summary>A size's twiddle factors (cos, sin of −2πk/N for k &lt; N/2), computed once and shared read-only.</summary>
    internal sealed class Plan(int n)
    {
        public int N { get; } = n;
        public double[] Cos { get; } = [.. Enumerable.Range(0, n / 2).Select(k => Math.Cos(-2 * Math.PI * k / n))];
        public double[] Sin { get; } = [.. Enumerable.Range(0, n / 2).Select(k => Math.Sin(-2 * Math.PI * k / n))];
    }

    internal static void Input(double[] re, double[] im)
    {
        ulong s = 0x5A5A5A5A12345678UL;
        for (int i = 0; i < re.Length; i++) { re[i] = Next(ref s); im[i] = Next(ref s); }
        static double Next(ref ulong s) { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (s >> 11) * (1.0 / (1UL << 53)) - 0.5; }
    }

    /// <summary>In-place iterative radix-2 FFT (bit reversal, then log2 N butterfly stages); <paramref name="inverse"/> uses conjugate twiddles and scales by 1/N.</summary>
    internal static void Transform(Plan p, double[] re, double[] im, bool inverse = false)
    {
        int n = p.N;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len >> 1, step = n / len;
            for (int start = 0; start < n; start += len)
                for (int k = 0; k < half; k++)
                {
                    double wr = p.Cos[k * step], wi = inverse ? -p.Sin[k * step] : p.Sin[k * step];
                    int a = start + k, b = a + half;
                    double tr = re[b] * wr - im[b] * wi, ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
                }
        }
        if (inverse) for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
    }

    /// <summary>The largest difference between the FFT and a direct DFT, relative to the largest output value; the check of the algorithm itself.</summary>
    internal static double DftError(int n)
    {
        var p = new Plan(n); var re = new double[n]; var im = new double[n]; Input(re, im);
        var r0 = (double[])re.Clone(); var i0 = (double[])im.Clone();
        Transform(p, re, im);
        double worst = 0, scale = 0;
        for (int k = 0; k < n; k++)
        {
            double sr = 0, si = 0;
            for (int t = 0; t < n; t++) { double ang = -2 * Math.PI * ((long)k * t % n) / n; sr += r0[t] * Math.Cos(ang) - i0[t] * Math.Sin(ang); si += r0[t] * Math.Sin(ang) + i0[t] * Math.Cos(ang); }
            worst = Math.Max(worst, Math.Max(Math.Abs(sr - re[k]), Math.Abs(si - im[k]))); scale = Math.Max(scale, Math.Max(Math.Abs(sr), Math.Abs(si)));
        }
        return worst / scale;
    }

    /// <summary>A size's reference output, or null with the reason when it fails the round-trip or Parseval check.</summary>
    internal static (double[] Re, double[] Im, string? Problem) Reference(Plan p)
    {
        var re = new double[p.N]; var im = new double[p.N]; Input(re, im);
        double energyIn = 0; for (int i = 0; i < p.N; i++) energyIn += re[i] * re[i] + im[i] * im[i];
        Transform(p, re, im);
        double energyOut = 0; for (int i = 0; i < p.N; i++) energyOut += re[i] * re[i] + im[i] * im[i];
        double parseval = Math.Abs(energyOut / p.N - energyIn) / energyIn;
        var br = (double[])re.Clone(); var bi = (double[])im.Clone(); Transform(p, br, bi, inverse: true);
        var r0 = new double[p.N]; var i0 = new double[p.N]; Input(r0, i0);
        double roundTrip = 0; for (int i = 0; i < p.N; i++) roundTrip = Math.Max(roundTrip, Math.Max(Math.Abs(br[i] - r0[i]), Math.Abs(bi[i] - i0[i])));
        double bound = 1e-9;
        string? problem = !(parseval < bound) ? $"Parseval check off by {parseval:G3} at N={p.N}" : !(roundTrip < bound) ? $"inverse transform off by {roundTrip:G3} at N={p.N}" : null;
        return (re, im, problem);
    }

    internal static bool Same(double[] a, double[] b) { for (int i = 0; i < a.Length; i++) if (BitConverter.DoubleToInt64Bits(a[i]) != BitConverter.DoubleToInt64Bits(b[i])) return false; return true; }

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        return Task.Run(() => Run(request, started, ct), CancellationToken.None);
    }

    private static TestRunResult Run(TestExecutionRequest request, DateTimeOffset started, CancellationToken ct)
    {
        request.Note("Log_CpuFft_Start", "X[k] = Σn x[n]·e^(−2πi·kn/N), radix-2;  checks: FFT vs direct DFT (N=512);  IFFT(FFT(x)) = x and Σ|X|²/N = Σ|x|² within 1e-9;  then every run bit-identical to the reference;  FLOPs = 5·N·log2 N",
            Environment.ProcessorCount, Small, Large);
        double dft = DftError(512);
        if (!(dft < 1e-10)) return new(Definition.Id, TestOutcome.Failed, started, request.Clock.UtcNow, 1, $"the FFT disagrees with a direct DFT by {dft:G3} (relative) before the load started");
        var plans = new[] { new Plan(Small), new Plan(Large) };
        var refs = plans.Select(Reference).ToArray();
        if (refs.FirstOrDefault(r => r.Problem is not null) is { Problem: { } problem }) return new(Definition.Id, TestOutcome.Failed, started, request.Clock.UtcNow, 1, $"the reference transform failed its own check: {problem}");
        request.Note("Log_CpuFft_Checked", null, dft.ToString("G2", System.Globalization.CultureInfo.InvariantCulture));

        int threads = Environment.ProcessorCount; long transforms = 0, errors = 0; double flops = 0; string firstError = ""; object sum = new(); var coverage = new CpuCoverage();
        var total = Stopwatch.StartNew(); var duration = TimeSpan.FromSeconds(request.DurationSeconds); var pacer = new LogPacer();
        void Worker(int index)
        {
            var re = new double[Large]; var im = new double[Large]; var sr = new double[Small]; var si = new double[Small]; double done = 0; long count = 0;
            for (int turn = 0; !ct.IsCancellationRequested && total.Elapsed < duration; turn++)
            {
                // Sixteen small transforms to each large one: about the same time on each.
                bool large = turn % 17 == 16; var p = plans[large ? 1 : 0]; var (r, i) = large ? (re, im) : (sr, si);
                Input(r, i); Transform(p, r, i);
                if (!Same(r, refs[large ? 1 : 0].Re) || !Same(i, refs[large ? 1 : 0].Im))
                {
                    Interlocked.Increment(ref errors);
                    if (firstError.Length == 0) { firstError = $"first wrong transform: thread {index}, N={p.N}"; request.NoteError("Log_Wrong_Result", firstError); }
                }
                count++; done += 5.0 * p.N * Math.Log2(p.N); coverage.Mark();
            }
            lock (sum) { flops += done; transforms += count; }
        }
        var all = Task.WhenAll(Enumerable.Range(0, threads).Select(i => Task.Factory.StartNew(() => Worker(i), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)));
        while (!all.IsCompleted)
        {
            request.Progress?.Invoke(new TestProgress(Math.Clamp(total.Elapsed / duration, 0, 1), "Test_Status_Running"));
            if (pacer.Due()) request.Note("Log_CpuFft_Progress", null, Interlocked.Read(ref errors));
            all.Wait(250, CancellationToken.None);
        }
        var finished = request.Clock.UtcNow;
        string detail = SensorEvidence.Join($"radix-2 complex FFT, N={Small} and N={Large}, {threads} threads; checked against a direct DFT (relative error {dft:G2}), an inverse round trip and Parseval, then bit for bit",
            $"transforms={transforms}", coverage.Describe(CpuTopology.Cores), $"{flops / Math.Max(0.001, total.Elapsed.TotalSeconds) / 1e9:F1} GFLOPS (5·N·log2 N)", firstError.Length > 0 ? firstError : null,
            SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuPackagePower, started, finished)?.Format("CPU package power", " W", includeMax: true),
            SensorEvidence.CpuTemperature(request.Engine, started, finished)?.Format("CPU temperature", "°C", includeMax: true));
        if (ct.IsCancellationRequested) return new(Definition.Id, TestOutcome.Cancelled, started, finished, errors, detail);
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, finished, errors, detail);
    }
}
