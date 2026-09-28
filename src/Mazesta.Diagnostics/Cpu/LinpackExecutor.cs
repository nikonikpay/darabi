using System.Diagnostics; using System.Numerics; using System.Runtime.CompilerServices; using System.Runtime.InteropServices; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence; using Mazesta.Diagnostics.Memory;
namespace Mazesta.Diagnostics.Cpu;

/// <summary>
/// Linpack: solves a dense random system Ax = b by LU factorisation with partial pivoting on every logical processor, the workload behind
/// HPL, Intel's Linpack and IntelBurnTest - the heaviest sustained floating-point and memory load a CPU gets, and the classic check of an
/// overclock or undervolt. Two independent checks make every pass count as evidence:
///  - the scaled residual ‖Ax−b‖∞ / (ε·(‖A‖∞·‖x‖∞ + ‖b‖∞)·n) must stay below 16, HPL's own correctness bound;
///  - every iteration solves the same seeded system, and the work is split into fixed ranges so each number is always computed by the same
///    sequence of operations: the solution must be bit-for-bit the first iteration's (IntelBurnTest's "residuals must match" rule). A healthy
///    CPU never differs; a marginal one does long before it crashes.
/// GFLOPS counts (2/3)n³ + 2n² per solve, the standard Linpack figure.
/// </summary>
public sealed class LinpackExecutor(IMemoryProbe memory) : ITestExecutor
{
    public const string SizeOption = "size";
    public static readonly TestDefinition Definition = new(new TestId("cpu.linpack"), "Test_Cpu_Linpack", 120,
        [new TestOption(SizeOption, "Test_Option_LinpackSize", TestOptionKind.Integer, "0")]);   // 0 = automatic from free RAM
    TestDefinition ITestExecutor.Definition => Definition;

    internal const int Block = 64, MinSize = 256, MaxSize = 30000, AutoMax = 6144;
    private const ulong Seed = 0x4D415A45535441;   // "MAZESTA"

    /// <summary>The matrix order: the technician's, or the largest up to <see cref="AutoMax"/> whose matrix fits in a quarter of the free RAM.</summary>
    internal static int ChooseSize(int requested, long availableBytes)
    {
        if (requested > 0) return Math.Clamp(requested, MinSize, MaxSize) / 8 * 8;
        int fits = (int)Math.Sqrt(Math.Max(0, availableBytes / 4) / 8.0);
        return Math.Clamp(Math.Min(fits, AutoMax), MinSize, AutoMax) / 64 * 64;
    }

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        var status = memory.Read();
        int n = ChooseSize((request.Options ?? TestOptions.None(Definition)).GetInt(SizeOption), status.AvailableBytes);
        long needed = 8L * n * n;
        if (needed > status.AvailableBytes - (1L << 30)) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, $"A {n}x{n} matrix needs {needed >> 20} MiB; only {status.AvailableBytes >> 20} MiB of RAM is free."));
        return Task.Run(() => Run(request, n, started, ct), CancellationToken.None);
    }

    private static TestRunResult Run(TestExecutionRequest request, int n, DateTimeOffset started, CancellationToken ct)
    {
        var a = GC.AllocateUninitializedArray<double>(n * n); var piv = new int[n]; var b = new double[n]; var x = new double[n];
        long iterations = 0, errors = 0; double bestGflops = 0, worstResidual = 0; ulong? reference = null; string firstError = "";
        var total = Stopwatch.StartNew(); var duration = TimeSpan.FromSeconds(request.DurationSeconds);
        void Progress(double within) => request.Progress?.Invoke(new TestProgress(Math.Clamp(total.Elapsed / duration, 0, 1), "Test_Status_Running"));
        try
        {
            do
            {
                ct.ThrowIfCancellationRequested();
                Generate(a, n); GenerateRhs(b);
                var solve = Stopwatch.StartNew();
                bool regular = Factor(a, n, piv, ct, Progress);
                if (regular) { b.CopyTo(x, 0); Solve(a, n, piv, x); }
                solve.Stop();
                iterations++;
                if (!regular) { errors++; firstError = firstError.Length > 0 ? firstError : $"iteration {iterations}: a zero pivot - the factorisation broke down"; continue; }
                bestGflops = Math.Max(bestGflops, (2.0 / 3 * n * (double)n * n + 2.0 * n * n) / solve.Elapsed.TotalSeconds / 1e9);

                Generate(a, n);   // the LU overwrote A: the residual is taken against the original matrix, rebuilt from the seed
                double residual = ScaledResidual(a, n, x, b);
                worstResidual = Math.Max(worstResidual, residual);
                ulong sum = Checksum(x);
                reference ??= sum;
                if (!(residual < 16)) { errors++; if (firstError.Length == 0) firstError = $"iteration {iterations}: residual {residual:G4} is above HPL's bound of 16"; }
                else if (sum != reference) { errors++; if (firstError.Length == 0) firstError = $"iteration {iterations}: the solution differs from iteration 1 (same system, same operations)"; }
                Progress(0);
            }
            while (total.Elapsed < duration);
        }
        catch (OperationCanceledException)
        {
            return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, Describe(request, started, n, iterations, bestGflops, worstResidual, firstError));
        }
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, errors, Describe(request, started, n, iterations, bestGflops, worstResidual, firstError));
    }

    private static string Describe(TestExecutionRequest request, DateTimeOffset started, int n, long iterations, double gflops, double residual, string firstError)
    {
        var finished = request.Clock.UtcNow;
        return SensorEvidence.Join($"Linpack (LU with partial pivoting), n={n} ({8L * n * n >> 20} MiB), {Environment.ProcessorCount} threads", $"iterations={iterations}",
            iterations > 0 ? $"{gflops:F1} GFLOPS" : null, iterations > 0 ? $"worst scaled residual {residual:G3} (bound 16)" : null, firstError.Length > 0 ? firstError : null,
            SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuPackagePower, started, finished)?.Format("CPU package power", " W", includeMax: true),
            SensorEvidence.CpuTemperature(request.Engine, started, finished)?.Format("CPU temperature", "°C", includeMax: true));
    }

    // ——— the numbers ———

    /// <summary>Row i from its own SplitMix64 stream, so the matrix is the same whatever thread fills which row. Values in [-0.5, 0.5).</summary>
    internal static void Generate(double[] a, int n) => Parallel.For(0, n, i =>
    {
        ulong s = Seed ^ (ulong)i * 0x9E3779B97F4A7C15UL; var row = a.AsSpan(i * n, n);
        for (int j = 0; j < n; j++) row[j] = Next(ref s);
    });

    internal static void GenerateRhs(double[] b) { ulong s = ~Seed; for (int i = 0; i < b.Length; i++) b[i] = Next(ref s); }

    private static double Next(ref ulong s)
    {
        ulong z = s += 0x9E3779B97F4A7C15UL;
        z = (z ^ z >> 30) * 0xBF58476D1CE4E5B9UL; z = (z ^ z >> 27) * 0x94D049BB133111EBUL; z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53)) - 0.5;
    }

    /// <summary>
    /// In-place blocked right-looking LU with partial pivoting (L unit lower, U upper, in A); false on a zero pivot. Each panel of
    /// <see cref="Block"/> columns is factored with row swaps across the whole matrix, the block row of U is solved, and the trailing matrix
    /// gets the rank-<see cref="Block"/> update, where nearly all the time goes. Every parallel loop splits its work into the same fixed
    /// ranges on every run, so each element is always computed by the same operations in the same order.
    /// </summary>
    internal static bool Factor(double[] a, int n, int[] piv, CancellationToken ct, Action<double>? progress = null)
    {
        double[]? panel = null;
        for (int kb = 0; kb < n; kb += Block)
        {
            ct.ThrowIfCancellationRequested();
            int nb = Math.Min(Block, n - kb), end = kb + nb;
            if (!FactorPanel(a, n, kb, nb, piv, panel ??= new double[n * Block])) return false;
            if (end == n) break;
            // U12: the block row right of the panel, solved against the panel's unit lower triangle (split by column ranges).
            ForColumns(end, n, (j0, j1) =>
            {
                for (int k = kb; k < end - 1; k++)
                {
                    var uk = a.AsSpan(k * n + j0, j1 - j0);
                    for (int i = k + 1; i < end; i++) Axpy(-a[i * n + k], uk, a.AsSpan(i * n + j0, j1 - j0));
                }
            });
            // A22 -= L21 · U12
            TrailingUpdate(a, n, kb, nb, end);
            progress?.Invoke(end / (double)n);
        }
        return true;
    }

    /// <summary>
    /// Factors columns kb..kb+nb-1 over rows kb..n-1. A column of a row-major matrix is one cache miss per element, so the panel is first
    /// copied into <paramref name="p"/> column by column, where the pivot search, the scaling and the updates all run along contiguous
    /// memory; it is copied back afterwards and the row swaps are applied to the columns outside the panel.
    /// </summary>
    private static bool FactorPanel(double[] a, int n, int kb, int nb, int[] piv, double[] p)
    {
        int m = n - kb, end = kb + nb;
        ForRows(0, m, r => { int src = (kb + r) * n + kb; for (int c = 0; c < nb; c++) p[c * m + r] = a[src + c]; });
        for (int c = 0; c < nb; c++)
        {
            var col = p.AsSpan(c * m, m);
            int best = c; double max = Math.Abs(col[c]);
            for (int r = c + 1; r < m; r++) { double v = Math.Abs(col[r]); if (v > max) { max = v; best = r; } }
            piv[kb + c] = kb + best;
            if (max == 0) return false;
            if (best != c) for (int j = 0; j < nb; j++) (p[j * m + c], p[j * m + best]) = (p[j * m + best], p[j * m + c]);
            double inv = 1 / col[c];
            var below = col[(c + 1)..];
            for (int r = 0; r < below.Length; r++) below[r] *= inv;
            for (int j = c + 1; j < nb; j++) Axpy(-p[j * m + c], below, p.AsSpan(j * m + c + 1, m - c - 1));
        }
        ForRows(0, m, r => { int dst = (kb + r) * n + kb; for (int c = 0; c < nb; c++) a[dst + c] = p[c * m + r]; });
        for (int c = 0; c < nb; c++)
        {
            int r0 = kb + c, r1 = piv[r0];
            if (r1 == r0) continue;
            SwapRows(a, r0 * n, r1 * n, kb);                     // the L columns left of the panel
            SwapRows(a, r0 * n + end, r1 * n + end, n - end);   // the columns right of it, still to be factored
        }
        return true;
    }

    /// <summary>Forward then back substitution with the factors and the recorded row swaps.</summary>
    internal static void Solve(double[] lu, int n, int[] piv, double[] x)
    {
        for (int k = 0; k < n; k++) if (piv[k] != k) (x[k], x[piv[k]]) = (x[piv[k]], x[k]);
        for (int i = 1; i < n; i++) { double s = x[i]; var row = lu.AsSpan(i * n, i); for (int j = 0; j < i; j++) s -= row[j] * x[j]; x[i] = s; }
        for (int i = n - 1; i >= 0; i--) { double s = x[i]; var row = lu.AsSpan(i * n, n); for (int j = i + 1; j < n; j++) s -= row[j] * x[j]; x[i] = s / row[i]; }
    }

    internal static double ScaledResidual(double[] a, int n, double[] x, double[] b)
    {
        var r = new double[n]; var rowNorm = new double[n];
        Parallel.For(0, n, i =>
        {
            var row = a.AsSpan(i * n, n); double s = 0, norm = 0;
            for (int j = 0; j < n; j++) { s += row[j] * x[j]; norm += Math.Abs(row[j]); }
            r[i] = Math.Abs(s - b[i]); rowNorm[i] = norm;
        });
        double normA = rowNorm.Max(), normX = x.Max(Math.Abs), normB = b.Max(Math.Abs);
        return r.Max() / (Epsilon * (normA * normX + normB) * n);
    }
    private const double Epsilon = 1.1102230246251565e-16;   // 2^-53, unit round-off of a double (HPL's eps)

    internal static ulong Checksum(double[] v) { ulong h = 1469598103934665603UL; foreach (double d in v) h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(d)) * 1099511628211UL; return h; }

    private static void Axpy(double alpha, ReadOnlySpan<double> x, Span<double> y)
    {
        int w = Vector<double>.Count, j = 0; var va = new Vector<double>(alpha);
        for (; j + w <= y.Length; j += w) Vector.FusedMultiplyAdd(va, new Vector<double>(x[j..]), new Vector<double>(y[j..])).CopyTo(y[j..]);
        for (; j < y.Length; j++) y[j] = Math.FusedMultiplyAdd(alpha, x[j], y[j]);
    }

    /// <summary>
    /// The rank-nb update C -= L·U on rows/columns from <paramref name="start"/>. Rows are handled four at a time (fixed quads, split among
    /// threads in fixed ranges) and columns in <see cref="Panel"/>-wide blocks. Each thread first copies the block of U (nb rows of the
    /// matrix, 32 KB apart for n = 4096) into one contiguous buffer: read in place, every k step lands on another memory page and the
    /// first-level TLB runs out, which halved the throughput on the dev box. Each quad keeps 4 rows × 2 vectors of C in registers across
    /// the nb multiply-adds.
    /// </summary>
    private static void TrailingUpdate(double[] a, int n, int kb, int nb, int start)
    {
        int rows = n - start, quads = (rows + 3) / 4;
        int w = Vector<double>.Count, step = 2 * w;
        ForRanges(quads, (q0, q1) =>
        {
            if (q1 <= q0) return;
            Span<double> l = stackalloc double[4 * Block];
            var ub = PackBuffer ??= new double[Block * Panel];
            ref double m0 = ref MemoryMarshal.GetArrayDataReference(a); ref double lr = ref MemoryMarshal.GetReference(l); ref double b0 = ref MemoryMarshal.GetArrayDataReference(ub);
            for (int jb = start; jb < n; jb += Panel)
            {
                int je = Math.Min(n, jb + Panel), width = je - jb;
                for (int k = 0; k < nb; k++) a.AsSpan((kb + k) * n + jb, width).CopyTo(ub.AsSpan(k * Panel, width));
                for (int q = q0; q < q1; q++)
                {
                    int i0 = start + q * 4, count = Math.Min(4, n - i0);
                    for (int r = 0; r < 4; r++) for (int k = 0; k < nb; k++) l[r * Block + k] = r < count ? -a[(i0 + r) * n + kb + k] : 0;
                    int j = jb;
                    if (count == 4)
                        // The loop limits keep every index in range; reading through refs lets the JIT emit bare loads and FMAs.
                        for (; j + step <= je; j += step)
                        {
                            nuint c0 = (nuint)(i0 * n + j), c1 = c0 + (nuint)n, c2 = c1 + (nuint)n, c3 = c2 + (nuint)n, vw = (nuint)w;
                            Vector<double> a00 = Vector.LoadUnsafe(ref m0, c0), a01 = Vector.LoadUnsafe(ref m0, c0 + vw), a10 = Vector.LoadUnsafe(ref m0, c1), a11 = Vector.LoadUnsafe(ref m0, c1 + vw),
                                a20 = Vector.LoadUnsafe(ref m0, c2), a21 = Vector.LoadUnsafe(ref m0, c2 + vw), a30 = Vector.LoadUnsafe(ref m0, c3), a31 = Vector.LoadUnsafe(ref m0, c3 + vw);
                            nuint u = (nuint)(j - jb);
                            for (int k = 0; k < nb; k++, u += Panel)
                            {
                                Vector<double> u0 = Vector.LoadUnsafe(ref b0, u), u1 = Vector.LoadUnsafe(ref b0, u + vw);
                                var x0 = new Vector<double>(Unsafe.Add(ref lr, k)); var x1 = new Vector<double>(Unsafe.Add(ref lr, Block + k));
                                var x2 = new Vector<double>(Unsafe.Add(ref lr, 2 * Block + k)); var x3 = new Vector<double>(Unsafe.Add(ref lr, 3 * Block + k));
                                a00 = Vector.FusedMultiplyAdd(x0, u0, a00); a01 = Vector.FusedMultiplyAdd(x0, u1, a01);
                                a10 = Vector.FusedMultiplyAdd(x1, u0, a10); a11 = Vector.FusedMultiplyAdd(x1, u1, a11);
                                a20 = Vector.FusedMultiplyAdd(x2, u0, a20); a21 = Vector.FusedMultiplyAdd(x2, u1, a21);
                                a30 = Vector.FusedMultiplyAdd(x3, u0, a30); a31 = Vector.FusedMultiplyAdd(x3, u1, a31);
                            }
                            a00.StoreUnsafe(ref m0, c0); a01.StoreUnsafe(ref m0, c0 + vw); a10.StoreUnsafe(ref m0, c1); a11.StoreUnsafe(ref m0, c1 + vw);
                            a20.StoreUnsafe(ref m0, c2); a21.StoreUnsafe(ref m0, c2 + vw); a30.StoreUnsafe(ref m0, c3); a31.StoreUnsafe(ref m0, c3 + vw);
                        }
                    // The columns left over (and a last partial quad of rows) one element at a time, in the same k order.
                    for (int r = 0; r < count; r++)
                        for (int jj = j; jj < je; jj++)
                        {
                            int c = (i0 + r) * n + jj; double s = a[c];
                            for (int k = 0; k < nb; k++) s = Math.FusedMultiplyAdd(l[r * Block + k], ub[k * Panel + jj - jb], s);
                            a[c] = s;
                        }
                }
            }
        });
    }
    private const int Panel = 256;
    [ThreadStatic] private static double[]? PackBuffer;
    private static void SwapRows(double[] a, int r, int s, int n)
    {
        var x = a.AsSpan(r, n); var y = a.AsSpan(s, n); int w = Vector<double>.Count, j = 0;
        for (; j + w <= n; j += w) { var t = new Vector<double>(x[j..]); new Vector<double>(y[j..]).CopyTo(x[j..]); t.CopyTo(y[j..]); }
        for (; j < n; j++) (x[j], y[j]) = (y[j], x[j]);
    }

    /// <summary>Splits [0, count) into fixed, equal ranges, one per logical processor: the split never depends on timing.</summary>
    private static void ForRanges(int count, Action<int, int> body)
    {
        int parts = Math.Max(1, Math.Min(Environment.ProcessorCount, count));
        Parallel.For(0, parts, p => body((int)((long)count * p / parts), (int)((long)count * (p + 1) / parts)));
    }
    private static void ForRows(int from, int to, Action<int> body)
    {
        if (to - from < 512) { for (int i = from; i < to; i++) body(i); return; }   // too little work to share
        ForRanges(to - from, (i0, i1) => { for (int i = from + i0; i < from + i1; i++) body(i); });
    }
    private static void ForColumns(int from, int to, Action<int, int> body) => ForRanges(to - from, (j0, j1) => { if (j1 > j0) body(from + j0, from + j1); });
}
