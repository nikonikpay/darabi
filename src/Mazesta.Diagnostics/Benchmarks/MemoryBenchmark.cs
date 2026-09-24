using Mazesta.Core.Hardware; using System.Diagnostics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Memory;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>Memory bandwidth on two 256 MiB buffers (larger than any CPU cache): single-thread write, read and copy, and
/// a copy spread over every logical processor. Copy counts the bytes moved once, as memcpy benchmarks conventionally do.</summary>
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
        long need = 2L * BufferBytes + Math.Max(2L << 30, status.TotalBytes / 10);
        if (status.AvailableBytes < need) return Task.FromResult(BenchmarkResult.Unsupported(Spec.Id, started, $"{status.AvailableBytes >> 20} MiB of RAM is free; {need >> 20} MiB is needed (two {BufferBytes >> 20} MiB buffers plus the OS reserve)."));
        return Task.Run(() => Run(request, started, ct), CancellationToken.None);
    }

    private static BenchmarkResult Run(TestExecutionRequest request, DateTimeOffset started, CancellationToken ct)
    {
        using var src = new NativeBlock(BufferBytes); using var dst = new NativeBlock(BufferBytes);
        src.Span.Fill(0x5A); dst.Span.Fill(0xA5);   // commits every page before anything is timed
        var phase = TimeSpan.FromSeconds(request.DurationSeconds / 4.0); int done = 0;
        double Measure(Action pass)
        {
            long passes = 0; var sw = Stopwatch.StartNew();
            do { ct.ThrowIfCancellationRequested(); pass(); passes++; } while (sw.Elapsed < phase);
            request.Report(++done / 4.0);
            return passes * (double)BufferBytes / sw.Elapsed.TotalSeconds / 1e9;
        }
        try
        {
            double write = Measure(() => dst.Span.Fill(0x3C));
            double read = Measure(() => { ulong s = 0; foreach (ulong v in MemoryMarshal.Cast<byte, ulong>(src.Span)) s += v; _sink = s; });
            double copy = Measure(() => src.Span.CopyTo(dst.Span));
            double copyAll = Measure(() => Parallel.For(0, BufferBytes / Slice, i => src.Span.Slice(i * Slice, Slice).CopyTo(dst.Span.Slice(i * Slice, Slice))));
            return new(Spec.Id, BenchmarkStatus.Completed, started, request.Clock.UtcNow,
                [new("Bench_Mem_Write", write, "GB/s"), new("Bench_Mem_Read", read, "GB/s"), new("Bench_Mem_Copy", copy, "GB/s"), new("Bench_Mem_CopyAll", copyAll, "GB/s")],
                $"two {BufferBytes >> 20} MiB buffers; write/read/copy on one thread, copy on {Environment.ProcessorCount} threads");
        }
        catch (OperationCanceledException) { return BenchmarkResult.Cancelled(Spec.Id, started, request.Clock.UtcNow); }
    }
}
