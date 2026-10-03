using System.Diagnostics; using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Memory;

/// <summary>
/// Bit fade (MemTest86's test 10): all ones are written to the free RAM, left untouched for half the run, and checked; then all zeros the same
/// way. A cell that leaks its charge faster than the memory's refresh loses its bit while nothing reads or writes it - a fault the busy pattern
/// test never gives time to appear. While it waits nothing may move the data: Windows would otherwise page idle memory out and the test would
/// check the page file, so every block is locked in RAM (VirtualLock, after raising the process's working-set minimum); when Windows refuses
/// the lock, the test says so and ends Inconclusive - a pass would claim what it did not test.
/// </summary>
public sealed class MemoryBitFadeExecutor(IMemoryProbe probe) : ITestExecutor
{
    public static readonly TestDefinition Definition = new(new TestId("memory.bitfade"), "Test_Memory_BitFade", 900,
        [new TestOption(MemoryPatternExecutor.SizeOption, "Test_Option_MemoryMb", TestOptionKind.Integer, "0")]);
    TestDefinition ITestExecutor.Definition => Definition;
    private const int BlockBytes = MemoryPatternExecutor.BlockBytes;
    /// <summary>A hold shorter than this says little about a leaking cell (MemTest86 holds each pattern for minutes): such a run ends Inconclusive.</summary>
    public const int MinHoldSeconds = 60;

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        long budget = MemoryPatternExecutor.Budget(probe.Read(), (request.Options ?? TestOptions.None(Definition)).GetInt(MemoryPatternExecutor.SizeOption));
        if (budget < BlockBytes) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, $"Only {budget >> 20} MiB of free RAM is available above the OS reserve; at least {BlockBytes >> 20} MiB is needed."));
        return Task.Run(() => Run(request, budget, started, ct), CancellationToken.None);
    }

    private static TestRunResult Run(TestExecutionRequest request, long budget, DateTimeOffset started, CancellationToken ct)
    {
        var blocks = new List<NativeBlock>(); long errors = 0; string first = ""; int locked = 0; var sw = Stopwatch.StartNew();
        var total = TimeSpan.FromSeconds(request.DurationSeconds); var held = new List<double>();
        // The process's working-set limits are raised to lock the blocks and put back afterwards, however the run ends.
        bool hadLimits = GetProcessWorkingSetSizeEx(GetCurrentProcess(), out nint oldMin, out nint oldMax, out uint oldFlags);
        try
        {
            for (long allocated = 0; allocated + BlockBytes <= budget; allocated += BlockBytes) { ct.ThrowIfCancellationRequested(); var b = new NativeBlock(BlockBytes); b.Span.Clear(); blocks.Add(b); }
            long bytes = (long)blocks.Count * BlockBytes;
            // The minimum is what is locked plus what the process already uses; the maximum leaves room above it.
            nint min = (nint)(bytes + (Environment.WorkingSet)), max = (nint)(bytes + Environment.WorkingSet + (512L << 20));
            SetProcessWorkingSetSizeEx(GetCurrentProcess(), min, max, 0);
            foreach (var b in blocks) if (b.Lock()) locked++;
            request.Note("Log_Mem_BitFade_Locked", "SetProcessWorkingSetSizeEx(min = locked + in use); VirtualLock(every block)", bytes >> 20, locked, blocks.Count);

            foreach (var (value, name, index) in new[] { ((byte)0xFF, "all ones", 0), ((byte)0x00, "all zeros", 1) })
            {
                var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.For(0, blocks.Count, options, i => blocks[i].Span.Fill(value));
                // The hold starts when this pattern is written, not when the run started: what is left of the run is shared by the patterns still
                // to come (less a few seconds for the check), so slow preparation shortens both holds alike instead of erasing the first.
                var hold = Stopwatch.StartNew();
                var length = (total - sw.Elapsed) / (2 - index) - TimeSpan.FromSeconds(3);
                request.Note("Log_Mem_BitFade_Hold", "fill(all blocks, pattern); sleep; count(bytes ≠ pattern)", name, Math.Max(0, (int)length.TotalSeconds));
                while (hold.Elapsed < length) { ct.ThrowIfCancellationRequested(); Thread.Sleep(250); request.Progress?.Invoke(new TestProgress(Math.Clamp(sw.Elapsed / total, 0, 1), "Test_Status_Running")); }
                held.Add(hold.Elapsed.TotalSeconds);
                Parallel.For(0, blocks.Count, options, i =>
                {
                    var span = blocks[i].Span; long bad = span.Length - span.Count(value);
                    if (bad > 0) { Interlocked.Add(ref errors, bad); if (first.Length == 0) first = $"first faded bytes in buffer block {i} (offset {(long)i * BlockBytes >> 20} MiB) after holding '{name}'"; }
                });
                request.Note("Log_Mem_BitFade_Checked", null, name, Interlocked.Read(ref errors));
            }
        }
        catch (OperationCanceledException) { return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, Describe(blocks.Count, locked, first, held)); }
        finally
        {
            foreach (var b in blocks) b.Dispose();
            if (hadLimits) SetProcessWorkingSetSizeEx(GetCurrentProcess(), oldMin, oldMax, oldFlags);
        }
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        // A fault seen is a fault whatever the hold; a clean result counts only when every block was locked and each pattern was held long enough.
        var outcome = errors > 0 ? TestOutcome.Failed : locked < blocks.Count || held.Count < 2 || held.Min() < MinHoldSeconds ? TestOutcome.Inconclusive : TestOutcome.Passed;
        return new(Definition.Id, outcome, started, request.Clock.UtcNow, errors, Describe(blocks.Count, locked, first, held));
    }

    private static string Describe(int blocks, int locked, string first, IReadOnlyList<double> held)
        => $"Bit fade; held {(long)blocks * BlockBytes >> 20} MiB as all ones, then all zeros, untouched for {string.Join(" and ", held.Select(h => $"{h:0} s"))}"
         + (held.Count > 0 && held.Min() < MinHoldSeconds ? $" - shorter than the {MinHoldSeconds} s a leaking cell needs to show; run it longer for a result" : "")
         + $"; {locked} of {blocks} blocks locked in RAM"
         + (locked < blocks ? " - Windows would not lock the rest, so they may have been paged out while waiting and their result says nothing about the RAM" : "")
         + "; addressed by buffer offset, not physical address or slot" + (first.Length > 0 ? $"; {first}" : "");

    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetProcessWorkingSetSizeEx(nint process, nint min, nint max, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessWorkingSetSizeEx(nint process, out nint min, out nint max, out uint flags);
}
