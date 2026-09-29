using Mazesta.Core.Hardware; using System.Diagnostics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Memory;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>Memory bandwidth on 256 MiB buffers (larger than any CPU cache): single-thread write, read and copy, and a copy spread over every
/// logical processor - these count the bytes moved once, as memcpy benchmarks conventionally do. Then STREAM's four kernels on every logical
/// processor over three arrays of doubles (Copy c=a, Scale b=q·c, Add c=a+b, Triad a=b+q·c), counted as STREAM counts them (2, 2, 3 and 3
/// times 8 bytes per element), each reported as the best of its runs, and checked as STREAM checks them: the arrays must hold the values the
/// same sequence gives in plain arithmetic. Version 2 of the workload (version 1 had no STREAM kernels and a longer single-thread phase).</summary>
public sealed class MemoryBenchmark(IMemoryProbe probe) : IBenchmark
{
    public static readonly TestDefinition Spec = new(new TestId("bench.memory"), "Bench_Memory", 60);
    public TestDefinition Definition => Spec;
    public HardwareKind Component => HardwareKind.Memory;
    internal const int BufferBytes = 256 << 20;
    private const int Slice = 4 << 20;
    private static ulong _sink;   // keeps the read loop from being optimised away

    public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(BenchmarkResult.Unsupported(Spec.Id, started, "Duration must be positive."));
        var status = probe.Read();
        long need = 3L * BufferBytes + Math.Max(2L << 30, status.TotalBytes / 10);
        if (status.AvailableBytes < need) return Task.FromResult(BenchmarkResult.Unsupported(Spec.Id, started, $"{status.AvailableBytes >> 20} MiB of RAM is free; {need >> 20} MiB is needed (three {BufferBytes >> 20} MiB buffers plus the OS reserve)."));
        return Task.Run(() => Run(request, started, ct), CancellationToken.None);
    }

    private static BenchmarkResult Run(TestExecutionRequest request, DateTimeOffset started, CancellationToken ct)
    {
        using var src = new NativeBlock(BufferBytes); using var dst = new NativeBlock(BufferBytes); using var third = new NativeBlock(BufferBytes);
        src.Span.Fill(0x5A); dst.Span.Fill(0xA5); third.Span.Clear();   // commits every page before anything is timed
        var phase = TimeSpan.FromSeconds(request.DurationSeconds / 8.0); int done = 0;
        double Measure(Action pass)
        {
            long passes = 0; var sw = Stopwatch.StartNew();
            do { ct.ThrowIfCancellationRequested(); pass(); passes++; } while (sw.Elapsed < phase);
            request.Report(++done / 8.0);
            return passes * (double)BufferBytes / sw.Elapsed.TotalSeconds / 1e9;
        }
        try
        {
            double write = Measure(() => dst.Span.Fill(0x3C));
            double read = Measure(() => { ulong s = 0; foreach (ulong v in MemoryMarshal.Cast<byte, ulong>(src.Span)) s += v; _sink = s; });
            double copy = Measure(() => src.Span.CopyTo(dst.Span));
            double copyAll = Measure(() => Parallel.For(0, BufferBytes / Slice, i => src.Span.Slice(i * Slice, Slice).CopyTo(dst.Span.Slice(i * Slice, Slice))));
            var stream = Stream(src, dst, third, TimeSpan.FromSeconds(request.DurationSeconds / 2.0), f => request.Report(0.5 + f / 2), ct);
            if (stream.Problem is { } problem) return BenchmarkResult.Failed(Spec.Id, started, request.Clock.UtcNow, problem);
            return new(Spec.Id, BenchmarkStatus.Completed, started, request.Clock.UtcNow,
                [new("Bench_Mem_Write", write, "GB/s"), new("Bench_Mem_Read", read, "GB/s"), new("Bench_Mem_Copy", copy, "GB/s"), new("Bench_Mem_CopyAll", copyAll, "GB/s"),
                 new("Bench_Mem_StreamCopy", stream.Best[0], "GB/s"), new("Bench_Mem_StreamScale", stream.Best[1], "GB/s"), new("Bench_Mem_StreamAdd", stream.Best[2], "GB/s"), new("Bench_Mem_StreamTriad", stream.Best[3], "GB/s")],
                $"{BufferBytes >> 20} MiB buffers; write/read/copy on one thread, copy on {Environment.ProcessorCount} threads; STREAM kernels on {Environment.ProcessorCount} threads over 3 x {BufferBytes / 8:N0} doubles, "
                + $"best of {stream.Runs} runs (median Triad {stream.MedianTriad:F1} GB/s), results checked (relative error {stream.Error:G2})");
        }
        catch (OperationCanceledException) { return BenchmarkResult.Cancelled(Spec.Id, started, request.Clock.UtcNow); }
    }

    internal sealed record StreamResult(double[] Best, int Runs, double MedianTriad, double Error, string? Problem);

    /// <summary>STREAM: a = 1, b = 2, c = 0, q = 3, then Copy, Scale, Add and Triad in turn for as long as <paramref name="budget"/> allows (at most 100
    /// rounds - the values grow fifteenfold a round), each kernel timed on its own over every thread; the rates are the best round's. The check
    /// replays the rounds on three plain numbers and compares every array's average, as STREAM's checkSTREAMresults does.</summary>
    internal static StreamResult Stream(NativeBlock blockA, NativeBlock blockB, NativeBlock blockC, TimeSpan budget, Action<double>? progress, CancellationToken ct)
    {
        const double Q = 3.0; const int MaxRounds = 100, Chunk = 1 << 18;
        int n = blockA.Span.Length / sizeof(double);
        Span<double> A() => MemoryMarshal.Cast<byte, double>(blockA.Span);
        Span<double> B() => MemoryMarshal.Cast<byte, double>(blockB.Span);
        Span<double> C() => MemoryMarshal.Cast<byte, double>(blockC.Span);
        A().Fill(1.0); B().Fill(2.0); C().Fill(0.0);
        int chunks = (n + Chunk - 1) / Chunk;
        double Time(Action<int, int> kernel)
        {
            long t = Stopwatch.GetTimestamp();
            Parallel.For(0, chunks, k => { int from = k * Chunk, to = Math.Min(n, from + Chunk); kernel(from, to); });
            return Stopwatch.GetElapsedTime(t).TotalSeconds;
        }
        var times = new List<double[]>(); var sw = Stopwatch.StartNew();
        while (times.Count < MaxRounds && (times.Count < 3 || sw.Elapsed < budget))
        {
            ct.ThrowIfCancellationRequested();
            times.Add([
                Time((f, t) => { var a = A(); var c = C(); for (int i = f; i < t; i++) c[i] = a[i]; }),
                Time((f, t) => { var b = B(); var c = C(); for (int i = f; i < t; i++) b[i] = Q * c[i]; }),
                Time((f, t) => { var a = A(); var b = B(); var c = C(); for (int i = f; i < t; i++) c[i] = a[i] + b[i]; }),
                Time((f, t) => { var a = A(); var b = B(); var c = C(); for (int i = f; i < t; i++) a[i] = b[i] + Q * c[i]; }),
            ]);
            progress?.Invoke(Math.Min(1, sw.Elapsed / budget));
        }
        double[] bytes = [2.0 * 8 * n, 2.0 * 8 * n, 3.0 * 8 * n, 3.0 * 8 * n];
        var best = Enumerable.Range(0, 4).Select(k => bytes[k] / times.Min(r => r[k]) / 1e9).ToArray();
        var triads = times.Select(r => bytes[3] / r[3] / 1e9).Order().ToArray();
        // The same rounds on plain numbers.
        double ea = 1, eb = 2, ec = 0;
        for (int r = 0; r < times.Count; r++) { ec = ea; eb = Q * ec; ec = ea + eb; ea = eb + Q * ec; }
        double error = Math.Max(Err(A(), ea), Math.Max(Err(B(), eb), Err(C(), ec)));
        string? problem = !(error < 1e-13) ? $"STREAM check failed: the arrays differ from the expected values by {error:G3} (relative) after {times.Count} rounds" : null;
        return new(best, times.Count, triads[triads.Length / 2], error, problem);
    }

    private static double Err(Span<double> x, double expected) { double sum = 0; foreach (double v in x) sum += Math.Abs(v - expected); return sum / x.Length / Math.Abs(expected); }
}
