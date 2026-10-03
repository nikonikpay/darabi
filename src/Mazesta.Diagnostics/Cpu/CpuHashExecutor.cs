using System.Diagnostics; using System.IO.Compression; using System.Security.Cryptography; using System.Text; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Cpu;

/// <summary>
/// Hashing and compression on every logical processor - the work of installers, backups, archives and TLS, and a path the arithmetic tests do
/// not take: SHA-256 runs on the CPU's SHA extensions where it has them, Deflate is branchy, table-driven byte work. Each block hashes a fixed
/// 256 KiB text, compresses it (Deflate, optimal), decompresses it and hashes the result. Both hashes must equal <see cref="ExpectedSha256"/>,
/// worked out in advance and pinned by a unit test against an independent computation (Python's hashlib), so the reference never comes from
/// the machine under test; and every block's compressed size must equal the first one's (the same input and library give the same output).
/// </summary>
public sealed class CpuHashExecutor : ITestExecutor
{
    public static readonly TestDefinition Definition = new(new TestId("cpu.hash"), "Test_Cpu_Hash", 900);
    TestDefinition ITestExecutor.Definition => Definition;

    internal const int Bytes = 256 << 10;
    internal const string ExpectedSha256 = "384303f2b68f3a2e1161016fc89279ea296391213075799f5a413ba3171babcd";
    private static readonly string[] Words = ["the", "memory", "processor", "cache", "power", "test", "value", "system", "drive", "network", "result", "error",
        "thread", "core", "clock", "voltage", "fan", "board", "report", "service", "customer", "window", "driver", "update", "sensor", "load", "heat", "data",
        "block", "pattern", "stable", "fault", "مازستا", "پردازنده", "حافظه", "کارت", "گرافیک", "تست", "گزارش", "دما", "ولتاژ", "خطا", "سالم", "0", "1", "42", "2026", "\n"];

    /// <summary>The input: words picked by a xorshift64 generator from a fixed seed, joined by spaces, cut to <see cref="Bytes"/> bytes of UTF-8.</summary>
    internal static byte[] Text()
    {
        var sb = new StringBuilder(Bytes); ulong s = 0x4D415A455354415AUL; var utf8 = Encoding.UTF8;
        while (sb.Length < Bytes) { s ^= s << 13; s ^= s >> 7; s ^= s << 17; sb.Append(Words[s % (ulong)Words.Length]).Append(' '); }
        return utf8.GetBytes(sb.ToString())[..Bytes];
    }

    internal static string Hex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>One block: hash, compress, decompress, hash again. Returns both hashes and the compressed size.</summary>
    internal static (string Before, string After, long Compressed) Block(byte[] text, MemoryStream packed, byte[] unpacked)
    {
        string before = Hex(text);
        packed.SetLength(0);
        using (var z = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(text);
        long size = packed.Length; packed.Position = 0;
        int read = 0;
        using (var z = new DeflateStream(packed, CompressionMode.Decompress, leaveOpen: true)) { int n; while (read < unpacked.Length && (n = z.Read(unpacked, read, unpacked.Length - read)) > 0) read += n; }
        return (before, read == text.Length ? Hex(unpacked) : $"short: {read} bytes", size);
    }

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        return Task.Run(() => Run(request, started, ct), CancellationToken.None);
    }

    private static TestRunResult Run(TestExecutionRequest request, DateTimeOffset started, CancellationToken ct)
    {
        int threads = Environment.ProcessorCount; long blocks = 0, errors = 0, size = -1; string firstError = ""; var coverage = new CpuCoverage();
        var text = Text(); var total = Stopwatch.StartNew(); var duration = TimeSpan.FromSeconds(request.DurationSeconds); var pacer = new LogPacer();
        request.Note("Log_CpuHash_Start", $"{Bytes >> 10} KiB text: h1 = SHA-256(text); z = Deflate(text); t = Inflate(z); h2 = SHA-256(t)   check: h1 == h2 == expected, |z| equal in every block", threads);
        void Worker(int index)
        {
            var packed = new MemoryStream(Bytes); var unpacked = new byte[Bytes];
            while (!ct.IsCancellationRequested && total.Elapsed < duration)
            {
                var (before, after, compressed) = Block(text, packed, unpacked);
                long first = Interlocked.CompareExchange(ref size, compressed, -1);
                if (before != ExpectedSha256 || after != ExpectedSha256 || (first != -1 && first != compressed))
                {
                    Interlocked.Increment(ref errors);
                    if (firstError.Length == 0) { firstError = $"first wrong block on thread {index}: {(before != ExpectedSha256 ? "hash of the input" : after != ExpectedSha256 ? "hash after the round trip" : "compressed size")} differs"; request.NoteError("Log_Wrong_Result", firstError); }
                }
                Interlocked.Increment(ref blocks); coverage.Mark();
            }
        }
        var all = Task.WhenAll(Enumerable.Range(0, threads).Select(i => Task.Factory.StartNew(() => Worker(i), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)));
        while (!all.IsCompleted)
        {
            request.Progress?.Invoke(new TestProgress(Math.Clamp(total.Elapsed / duration, 0, 1), "Test_Status_Running"));
            if (pacer.Due()) request.Note("Log_CpuHash_Progress", null, Interlocked.Read(ref blocks), MBps(Interlocked.Read(ref blocks), total.Elapsed.TotalSeconds), Interlocked.Read(ref errors));
            all.Wait(250, CancellationToken.None);
        }
        var finished = request.Clock.UtcNow;
        string detail = SensorEvidence.Join($"SHA-256 and Deflate round trip on {threads} threads (SHA extensions: {(System.Runtime.Intrinsics.X86.X86Base.IsSupported && (System.Runtime.Intrinsics.X86.X86Base.CpuId(7, 0).Ebx & (1 << 29)) != 0 ? "yes" : "no")}), every block checked against a precomputed hash",
            $"blocks={blocks}", $"{MBps(blocks, total.Elapsed.TotalSeconds):F0} MB/s of input", coverage.Describe(CpuTopology.Cores), size > 0 ? $"compressed to {size * 100.0 / Bytes:F1}%" : null, firstError.Length > 0 ? firstError : null,
            SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuPackagePower, started, finished)?.Format("CPU package power", " W", includeMax: true),
            SensorEvidence.CpuTemperature(request.Engine, started, finished)?.Format("CPU temperature", "°C", includeMax: true));
        if (ct.IsCancellationRequested) return new(Definition.Id, TestOutcome.Cancelled, started, finished, errors, detail);
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, finished, errors, detail);
    }

    private static double MBps(long blocks, double seconds) => blocks * (double)Bytes / Math.Max(0.001, seconds) / 1e6;
}
