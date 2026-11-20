using System.Diagnostics; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Memory;

/// <summary>
/// RAM test (spec §10): allocates the free memory it may use, then repeatedly verifies the previous pass's
/// pattern over <b>every allocated byte</b> and overwrites it with the next one. A read-back difference is a
/// counted error, not an exception - the run keeps going so the technician sees how bad it is. Memory the OS
/// needs to stay alive is never taken: the reserve is the larger of 1.5 GiB and a 24th of installed RAM. Before it measures what is free,
/// the other programs' working sets are trimmed (<see cref="IMemoryProbe.FreeUp"/>): their pages go to the page file, nothing is closed.
/// A clean run passes only when every pattern was verified and every algorithm ran; a run too short for that is Inconclusive. So is one that,
/// left to size itself, reached less than half of the installed RAM (<see cref="MinShare"/>): the untested half may hold the fault.
/// The blocks are locked in RAM (<see cref="RamLock"/>), so what is checked is the RAM and never the page file.
/// </summary>
public sealed class MemoryPatternExecutor(IMemoryProbe probe) : ITestExecutor
{
    public const string SizeOption = "sizeMb";
    public static readonly TestDefinition Definition = new(new TestId("memory.pattern"), "Test_Memory_Pattern", 300,
        [new TestOption(SizeOption, "Test_Option_MemoryMb", TestOptionKind.Integer, "0")]);   // 0 = automatic: all free RAM above the reserve
    TestDefinition ITestExecutor.Definition => Definition;

    internal const int BlockBytes = 64 << 20;
    /// <summary>The share of the installed RAM an automatic run must reach for a clean result to be a pass.</summary>
    public const double MinShare = 0.5;

    /// <summary>Whether a clean run covered enough to pass. A size the technician set is their choice and is only reported.</summary>
    internal static bool Enough(long tested, long total, bool automatic) => !automatic || total <= 0 || tested >= total * MinShare;

    /// <summary>How much of the installed RAM the test held, and what to do when that is too little to call the RAM good.</summary>
    internal static string? Coverage(long tested, long total, bool automatic) => total <= 0 ? null
        : $"{tested * 100 / total}% of the {total >> 20} MiB installed RAM tested" + (Enough(tested, total, automatic) ? "" : " - under the half a result needs: close other programs and run it again, or test the whole RAM from boot");

    /// <summary>The blocks Windows agreed to keep in RAM, in the words the bit-fade test uses.</summary>
    internal static string Locks(int locked, int blocks) => $"{locked} of {blocks} blocks locked in RAM"
        + (locked < blocks ? " - Windows would not lock the rest, so they may have been paged out while waiting and their result says nothing about the RAM" : "");

    /// <summary>What the test may take: the free RAM above the OS reserve, or less if the technician asked for less.</summary>
    internal static long Budget(MemoryStatus status, int requestedMb)
    {
        long reserve = Math.Max(3L << 29, status.TotalBytes / 24);
        // Free RAM, but never more than Windows will still let programs commit: with no page file (or a small one) memory others reserved and did not
        // touch counts against the commit limit, not against free RAM, and taking it all makes every other program's allocations fail.
        long free = status.AvailableCommitBytes > 0 ? Math.Min(status.AvailableBytes, status.AvailableCommitBytes) : status.AvailableBytes;
        long budget = Math.Max(0, free - reserve);
        return requestedMb > 0 ? Math.Min(budget, (long)requestedMb << 20) : budget;
    }

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        var status = probe.FreeUp(); int asked = (request.Options ?? TestOptions.None(Definition)).GetInt(SizeOption);
        long budget = Budget(status, asked);
        if (budget < BlockBytes) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, $"Only {budget >> 20} MiB of free RAM is available above the OS reserve; at least {BlockBytes >> 20} MiB is needed."));
        return Task.Run(() => Run(request, budget, status.TotalBytes, asked <= 0, started, ct), CancellationToken.None);
    }

    private TestRunResult Run(TestExecutionRequest request, long budget, long total, bool automatic, DateTimeOffset started, CancellationToken ct)
    {
        var blocks = new List<NativeBlock>(); RamLock? held = null;
        long errors = 0, passes = 0, bytesTouched = 0; string firstError = ""; var algorithmsRun = new int[3];
        var timed = new Stopwatch();
        string Detail() => SensorEvidence.Join(Describe(blocks.Count, passes, bytesTouched, timed, firstError, algorithmsRun),
            Coverage((long)blocks.Count * BlockBytes, total, automatic), held is null ? null : Locks(held.Locked, blocks.Count),
            SensorEvidence.Read(request.Engine, HardwareKind.Memory, SensorRole.DimmTemp, started, request.Clock.UtcNow)?.Format("RAM temperature", "°C", includeMax: true));
        try
        {
            for (long allocated = 0; allocated + BlockBytes <= budget; allocated += BlockBytes)
            {
                ct.ThrowIfCancellationRequested();
                var block = new NativeBlock(BlockBytes); blocks.Add(block);
                MemoryPatterns.Fill(block.Span, 0, blocks.Count - 1);   // commits the pages before anything is timed
                request.Progress?.Invoke(new TestProgress(0, "Test_Status_Allocating"));
            }
            held = new RamLock(blocks);
            request.Note("Log_Mem_Allocated", "other programs' working sets trimmed;  reserve = max(1.5 GiB, RAM / 24);  budget = free RAM − reserve", (long)blocks.Count * BlockBytes >> 20, blocks.Count);
            var duration = TimeSpan.FromSeconds(request.DurationSeconds);
            timed.Restart();
            do
            {
                int pass = (int)passes + 1;   // the allocation wrote pass 0
                // Every fourth pass also runs one of the algorithms, in turn, between checking the last pattern and writing the next.
                MemoryPatterns.Algorithm? algorithm = pass % 4 == 0 ? (MemoryPatterns.Algorithm)(pass / 4 % 3) : null;
                if (algorithm is { } run) algorithmsRun[(int)run]++;
                var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.For(0, blocks.Count, options, i =>
                {
                    var span = blocks[i].Span;
                    long bad = MemoryPatterns.CountMismatches(span, pass - 1, i);
                    string word = bad > 0 && firstError.Length == 0 ? "; " + MemoryPatterns.DescribeWord(MemoryPatterns.FirstMismatch(span, pass - 1, i)) : "";   // before the block is written over
                    if (algorithm is { } a)
                    {
                        long wrong = a switch
                        {
                            MemoryPatterns.Algorithm.MovingInversions => MemoryPatterns.MovingInversions(span, pass % 8 < 4 ? 0UL : 0x5555555555555555UL),
                            MemoryPatterns.Algorithm.BlockMove => MemoryPatterns.BlockMove(span, pass, i),
                            _ => MemoryPatterns.Stride(span, pass, i),
                        };
                        if (wrong > 0) { Interlocked.Add(ref errors, wrong); if (firstError.Length == 0) firstError = $"first mismatch in buffer block {i} (offset {(long)i * BlockBytes >> 20} MiB) during '{MemoryPatterns.Name(a)}'"; }
                        Interlocked.Add(ref bytesTouched, 3L * span.Length);
                    }
                    MemoryPatterns.Fill(span, pass, i);
                    if (bad > 0) { Interlocked.Add(ref errors, bad); if (firstError.Length == 0) firstError = $"first mismatch in buffer block {i} (offset {(long)i * BlockBytes >> 20} MiB) while verifying '{MemoryPatterns.Name(pass - 1)}'{word}"; }
                    Interlocked.Add(ref bytesTouched, 2L * span.Length);
                });
                passes++;
                request.Note("Log_Mem_Pass", algorithm switch
                {
                    MemoryPatterns.Algorithm.MovingInversions => "for every block: count(bytes ≠ pattern[p−1]);  moving inversions: fill P; up: check P, write ~P; down: check ~P, write P;  fill(pattern[p])",
                    MemoryPatterns.Algorithm.BlockMove => "for every block: count(bytes ≠ pattern[p−1]);  block move: seeded first half, memmove onto the second half, check both;  fill(pattern[p])",
                    MemoryPatterns.Algorithm.Stride => "for every block: count(bytes ≠ pattern[p−1]);  stride: write f(index) with stride 4099 words, read back with stride 8191;  fill(pattern[p])",
                    _ => "for every 64 MiB block: count(bytes ≠ pattern[p−1]); fill(pattern[p])",
                }, pass, MemoryPatterns.Name(pass - 1), algorithm is { } x ? $"{MemoryPatterns.Name(x)} + {MemoryPatterns.Name(pass)}" : MemoryPatterns.Name(pass), Interlocked.Read(ref errors));
                request.Progress?.Invoke(new TestProgress(Math.Clamp(timed.Elapsed / duration, 0, 1), "Test_Status_Running"));
            }
            while (timed.Elapsed < duration);
            // One last verification, so the final pass's own pattern is checked too.
            Parallel.For(0, blocks.Count, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
                i => { long bad = MemoryPatterns.CountMismatches(blocks[i].Span, (int)passes, i); if (bad > 0) { Interlocked.Add(ref errors, bad); if (firstError.Length == 0) firstError = $"first mismatch in buffer block {i} (offset {(long)i * BlockBytes >> 20} MiB) in the last verification of '{MemoryPatterns.Name((int)passes)}'; {MemoryPatterns.DescribeWord(MemoryPatterns.FirstMismatch(blocks[i].Span, (int)passes, i))}"; } });
        }
        catch (OperationCanceledException)
        {
            return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, Detail());
        }
        finally { foreach (var b in blocks) b.Dispose(); held?.Dispose(); }
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        bool clean = Complete(passes, algorithmsRun) && Enough((long)blocks.Count * BlockBytes, total, automatic);
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : clean ? TestOutcome.Passed : TestOutcome.Inconclusive, started, request.Clock.UtcNow, errors, Detail());
    }

    /// <summary>Every pattern verified (pattern 0 is written at allocation, the last one by the final verification) and every algorithm run.</summary>
    internal static bool Complete(long passes, int[] algorithms) => passes + 1 >= MemoryPatterns.Count && algorithms.All(n => n > 0);

    private static string Describe(int blocks, long passes, long bytesTouched, Stopwatch sw, string firstError, int[] algorithms)
        => $"RAM pattern test; tested={(long)blocks * BlockBytes >> 20} MiB; passes={passes} of {MemoryPatterns.Count} patterns{(Complete(passes, algorithms) ? "" : " (incomplete: not every pattern and algorithm ran; a longer run covers them all)")}; "
         + $"moving inversions ×{algorithms[0]}, block move ×{algorithms[1]}, stride ×{algorithms[2]}; "
         + $"{bytesTouched / 1e9 / Math.Max(0.001, sw.Elapsed.TotalSeconds):F1} GB/s; covers only the RAM Windows let the test have, addressed by buffer offset, not physical address or slot"
         + (firstError.Length > 0 ? $"; {firstError}" : "");
}
