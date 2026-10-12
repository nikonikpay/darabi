using System.Diagnostics;
namespace Mazesta.Diagnostics.Memory;

/// <summary>
/// RAM sequence test: runs a preset of <see cref="MemorySequence"/> steps over all the free RAM it may take, round after round until its time
/// is up, then checks once more so the last steps are covered too. The presets differ in what they press on: Quick touches every step once,
/// Extreme sweeps many times between checks (TestMem5's Extreme1), Heat writes as fast as the RAM takes (DDR5 heats up and its margin shrinks),
/// Spike swings between flat out and idle (the memory controller and its supply), Margin mixes pattern changes, inversions and short breaks.
/// "Custom" runs a sequence the technician typed. A wrong word is a counted error naming the step it was found after; the run goes on.
/// The memory reserve and the block size are the pattern test's.
/// </summary>
public sealed class MemorySequenceExecutor(IMemoryProbe probe) : ITestExecutor
{
    public const string PresetOption = "preset", SequenceOption = "sequence";
    private static readonly string[] PresetIds = ["quick", "extreme", "heat", "spike", "margin", MemorySequence.Custom];
    public static readonly TestDefinition Definition = new(new TestId("memory.sequence"), "Test_Memory_Sequence", 900,
    [
        new TestOption(PresetOption, "Test_Option_MemPreset", TestOptionKind.Choice, "heat",
            () => [.. PresetIds.Select(p => new OptionChoice(p, $"Test_MemPreset_{p}", true, p == MemorySequence.Custom ? null : MemorySequence.Presets[p]))]),
        new TestOption(SequenceOption, "Test_Option_MemSequence", TestOptionKind.Text, MemorySequence.Presets["quick"], When: PresetOption + "=" + MemorySequence.Custom),
        new TestOption(MemoryPatternExecutor.SizeOption, "Test_Option_MemoryMb", TestOptionKind.Integer, "0"),
    ]);
    TestDefinition ITestExecutor.Definition => Definition;

    private const int BlockBytes = MemoryPatternExecutor.BlockBytes;

    /// <summary>The sequence the options ask for (a preset's, or the typed one).</summary>
    public static IReadOnlyList<MemorySequence.Step> SequenceOf(TestOptions o)
        => MemorySequence.Parse(o.Get(PresetOption) is var p && MemorySequence.Presets.TryGetValue(p, out var text) ? text : o.Get(SequenceOption));

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow; var options = request.Options ?? TestOptions.None(Definition);
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        IReadOnlyList<MemorySequence.Step> steps;
        try { steps = SequenceOf(options); }
        catch (FormatException e) { return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, $"The sequence cannot be read: {e.Message}")); }
        long budget = MemoryPatternExecutor.Budget(probe.FreeUp(), options.GetInt(MemoryPatternExecutor.SizeOption));
        if (budget < BlockBytes) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, $"Only {budget >> 20} MiB of free RAM is available above the OS reserve; at least {BlockBytes >> 20} MiB is needed."));
        return Task.Run(() => new Run(request, options.Get(PresetOption), steps, budget, started, ct).Go(), CancellationToken.None);
    }

    private sealed class Run(TestExecutionRequest request, string preset, IReadOnlyList<MemorySequence.Step> steps, long budget, DateTimeOffset started, CancellationToken ct)
    {
        private readonly List<NativeBlock> _blocks = [];
        private readonly MemorySequence.Expected _expected = new();
        private readonly Stopwatch _timed = new();
        private readonly ParallelOptions _parallel = new() { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount };
        private TimeSpan _duration;
        private long _errors, _bits, _sweeps, _checks, _rounds; private ulong _fills, _sink;
        private bool _dirty; private string _firstError = ""; private TimeSpan? _firstRound;

        public TestRunResult Go()
        {
            try
            {
                for (long allocated = 0; allocated + BlockBytes <= budget; allocated += BlockBytes)
                {
                    ct.ThrowIfCancellationRequested();
                    var block = new NativeBlock(BlockBytes); _blocks.Add(block);
                    MemorySequence.Fill(Words(_blocks.Count - 1), MemorySequence.Pattern.Random, 0, _blocks.Count - 1);   // commits the pages before anything is timed
                    request.Progress?.Invoke(new TestProgress(0, "Test_Status_Allocating"));
                }
                _expected.Filled(MemorySequence.Pattern.Random, 0);
                request.Note("Log_MemSeq_Start", string.Join("; ", Flatten(steps)), preset, (long)_blocks.Count * BlockBytes >> 20);
                _duration = TimeSpan.FromSeconds(request.DurationSeconds);
                _timed.Restart();
                do
                {
                    if (!RunSteps(steps)) break;
                    _rounds++; _firstRound ??= _timed.Elapsed;
                    request.Note("Log_MemSeq_Round", null, _rounds, _checks, _errors);
                }
                while (_timed.Elapsed < _duration);
                if (_dirty) Check("end of run");   // the last steps are checked too
            }
            catch (OperationCanceledException) { return Result(TestOutcome.Cancelled); }
            finally { foreach (var b in _blocks) b.Dispose(); }
            request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
            return Result(_errors > 0 ? TestOutcome.Failed : TestOutcome.Passed);
        }

        private Span<ulong> Words(int i) => System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(_blocks[i].Span);

        /// <summary>Runs the steps once; false when the time ran out part way (the round is not counted).</summary>
        private bool RunSteps(IReadOnlyList<MemorySequence.Step> list)
        {
            foreach (var s in list)
            {
                if (_timed.Elapsed >= _duration) return false;
                ct.ThrowIfCancellationRequested();
                switch (s.Op)
                {
                    case MemorySequence.Op.Loop: for (int r = 0; r < s.Count; r++) if (!RunSteps(s.Body!)) return false; break;
                    case MemorySequence.Op.Check: Check(s.ToString()); break;
                    case MemorySequence.Op.Idle: ct.WaitHandle.WaitOne(s.Count); break;
                    default: Sweep(s); break;
                }
                request.Progress?.Invoke(new TestProgress(Math.Clamp(_timed.Elapsed / _duration, 0, 1), "Test_Status_Running"));
            }
            return true;
        }

        private void Sweep(MemorySequence.Step s)
        {
            var (a, b) = MemorySequence.ArithConstants(s); ulong seed = s.Op == MemorySequence.Op.Fill ? ++_fills : 0;
            for (int pass = 0; pass < s.Count; pass++)
            {
                if (_timed.Elapsed >= _duration) break;   // the expectation follows each finished pass, so a step may stop between any two
                long sink = 0;
                Parallel.For(0, _blocks.Count, _parallel, () => new ulong[1024], (i, _, scratch) =>
                {
                    var w = Words(i);
                    switch (s.Op)
                    {
                        case MemorySequence.Op.Fill: MemorySequence.Fill(w, s.Fill, seed, i); break;
                        case MemorySequence.Op.Arith: MemorySequence.Arith(w, a, b); break;
                        case MemorySequence.Op.Invert: MemorySequence.Invert(w); break;
                        case MemorySequence.Op.Flip: MemorySequence.Invert(w); MemorySequence.Invert(w); break;
                        case MemorySequence.Op.Mirror: MemorySequence.Mirror(w, s.Chunk, scratch); break;
                        case MemorySequence.Op.Read: Interlocked.Add(ref sink, (long)MemorySequence.Read(w)); break;
                    }
                    return scratch;
                }, _ => { });
                _sink ^= (ulong)sink; _sweeps++;
                switch (s.Op)
                {
                    case MemorySequence.Op.Fill: if (pass == 0) _expected.Filled(s.Fill, seed); break;
                    case MemorySequence.Op.Arith: _expected.Arith(a, b); break;
                    case MemorySequence.Op.Invert: _expected.Inverted(); break;
                    case MemorySequence.Op.Mirror: _expected.Mirrored(s.Chunk); break;
                }
            }
            _dirty = true;
        }

        private void Check(string after)
        {
            long words = 0, bits = 0; int firstBlock = -1;
            Parallel.For(0, _blocks.Count, _parallel, i =>
            {
                var (w, b) = MemorySequence.Check(Words(i), _expected, i);
                if (w == 0) return;
                Interlocked.Add(ref words, w); Interlocked.Add(ref bits, b); Interlocked.CompareExchange(ref firstBlock, i, -1);
            });
            _checks++; _dirty = false;
            if (words == 0) return;
            _errors += words; _bits += bits;
            if (_firstError.Length == 0) _firstError = $"first wrong words in buffer block {firstBlock} (offset {(long)firstBlock * BlockBytes >> 20} MiB), found by the check after '{after}' in round {_rounds + 1}";
            request.NoteError("Log_MemSeq_Errors", "words ≠ expected(fill, affine map, mirrors)", after, words, bits);
        }

        private TestRunResult Result(TestOutcome outcome)
        {
            double gbps = _sweeps * (double)_blocks.Count * BlockBytes / 1e9 / Math.Max(0.001, _timed.Elapsed.TotalSeconds);
            string detail = $"RAM sequence test, preset '{preset}'; tested={(long)_blocks.Count * BlockBytes >> 20} MiB; rounds={_rounds}"
                + (_firstRound is { } r ? $" (one round {r.TotalSeconds:0} s)" : " (the first round did not finish; a longer run covers every step)")
                + $"; {_sweeps} sweeps, {_checks} checks, ~{gbps:F1} GB/s of sweeps; {_errors} wrong word(s), {_bits} wrong bit(s); "
                + "covers only the RAM Windows let the test have, addressed by buffer offset, not physical address or slot"
                + (_firstError.Length > 0 ? $"; {_firstError}" : "") + (_sink == 0x5EED ? "." : "");   // the read steps' result is used, so they are not optimised away
            return new(Definition.Id, outcome, started, request.Clock.UtcNow, _errors, detail);
        }
    }

    private static IEnumerable<string> Flatten(IReadOnlyList<MemorySequence.Step> steps)
        => steps.Select(s => s.Op == MemorySequence.Op.Loop ? $"{s} {{ {string.Join("; ", Flatten(s.Body!))} }}" : s.ToString());
}
