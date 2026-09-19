using System.Diagnostics;
namespace Mazesta.Diagnostics.Memory;

/// <summary>
/// RAM test (spec §10): allocates the free memory it may use, then repeatedly verifies the previous pass's
/// pattern over <b>every allocated byte</b> and overwrites it with the next one. A read-back difference is a
/// counted error, not an exception - the run keeps going so the technician sees how bad it is. Memory the OS
/// needs to stay alive is never taken: the reserve is the larger of 2 GiB and a tenth of installed RAM.
/// </summary>
public sealed class MemoryPatternExecutor(IMemoryProbe probe) : ITestExecutor
{
    public const string SizeOption = "sizeMb";
    public static readonly TestDefinition Definition = new(new TestId("memory.pattern"), "Test_Memory_Pattern", 60,
        [new TestOption(SizeOption, "Test_Option_MemoryMb", TestOptionKind.Integer, "0")]);   // 0 = automatic: all free RAM above the reserve
    TestDefinition ITestExecutor.Definition => Definition;

    internal const int BlockBytes = 64 << 20;

    /// <summary>What the test may take: the free RAM above the OS reserve, or less if the technician asked for less.</summary>
    internal static long Budget(MemoryStatus status, int requestedMb)
    {
        long reserve = Math.Max(2L << 30, status.TotalBytes / 10);
        long budget = Math.Max(0, status.AvailableBytes - reserve);
        return requestedMb > 0 ? Math.Min(budget, (long)requestedMb << 20) : budget;
    }

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        long budget = Budget(probe.Read(), (request.Options ?? TestOptions.None(Definition)).GetInt(SizeOption));
        if (budget < BlockBytes) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, $"Only {budget >> 20} MiB of free RAM is available above the OS reserve; at least {BlockBytes >> 20} MiB is needed."));
        return Task.Run(() => Run(request, budget, started, ct), CancellationToken.None);
    }

    private TestRunResult Run(TestExecutionRequest request, long budget, DateTimeOffset started, CancellationToken ct)
    {
        var blocks = new List<NativeBlock>();
        long errors = 0, passes = 0, bytesTouched = 0; string firstError = "";
        var timed = new Stopwatch();
        try
        {
            for (long allocated = 0; allocated + BlockBytes <= budget; allocated += BlockBytes)
            {
                ct.ThrowIfCancellationRequested();
                var block = new NativeBlock(BlockBytes); blocks.Add(block);
                MemoryPatterns.Fill(block.Span, 0, blocks.Count - 1);   // commits the pages before anything is timed
                request.Progress?.Invoke(new TestProgress(0, "Test_Status_Allocating"));
            }
            var duration = TimeSpan.FromSeconds(request.DurationSeconds);
            timed.Restart();
            do
            {
                int pass = (int)passes + 1;   // the allocation wrote pass 0
                var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.For(0, blocks.Count, options, i =>
                {
                    var span = blocks[i].Span;
                    long bad = MemoryPatterns.CountMismatches(span, pass - 1, i);
                    MemoryPatterns.Fill(span, pass, i);
                    if (bad > 0) { Interlocked.Add(ref errors, bad); if (firstError.Length == 0) firstError = $"first mismatch in block {i} while verifying '{MemoryPatterns.Name(pass - 1)}'"; }
                    Interlocked.Add(ref bytesTouched, 2L * span.Length);
                });
                passes++;
                request.Progress?.Invoke(new TestProgress(Math.Clamp(timed.Elapsed / duration, 0, 1), "Test_Status_Running"));
            }
            while (timed.Elapsed < duration);
            // One last verification, so the final pass's own pattern is checked too.
            Parallel.For(0, blocks.Count, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
                i => { long bad = MemoryPatterns.CountMismatches(blocks[i].Span, (int)passes, i); if (bad > 0) Interlocked.Add(ref errors, bad); });
        }
        catch (OperationCanceledException)
        {
            return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, Describe(blocks.Count, passes, bytesTouched, timed, firstError));
        }
        finally { foreach (var b in blocks) b.Dispose(); }
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, errors, Describe(blocks.Count, passes, bytesTouched, timed, firstError));
    }

    private static string Describe(int blocks, long passes, long bytesTouched, Stopwatch sw, string firstError)
        => $"RAM pattern test; tested={(long)blocks * BlockBytes >> 20} MiB; passes={passes}; {bytesTouched / 1e9 / Math.Max(0.001, sw.Elapsed.TotalSeconds):F1} GB/s" + (firstError.Length > 0 ? $"; {firstError}" : "");
}
