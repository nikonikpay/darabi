using System.Diagnostics; using System.Text.Json;
namespace Mazesta.Diagnostics.Cpu;

/// <summary>
/// The everyday-use test: many machines pass every full-load test and still crash at the desk - at idle, while browsing, or as a video starts.
/// Those faults live in the changes of state, not in the load: a core waking from a deep C-state and jumping to its highest boost, a load that
/// starts or stops at once, a long idle (Intel's 13th/14th-gen Vmin fault grew from idle and light-load voltage; negative Curve Optimizer
/// offsets on Ryzen fail at idle while Prime95 passes). This test makes those changes happen thousands of times an hour, with verified work:
///  - bursts: a few milliseconds of work on one core at a time (every core in turn, in a shuffled order), then a rest of 10 ms to 2 s;
///  - light: a few threads at 10-30 % load, like a browser or an office program;
///  - everyday: hashing and compressing a file, sorting, JSON in and out, with pauses like a person's;
///  - spikes: every processor at once for half a second to three, then nothing for a few seconds;
///  - idle: the machine left alone, woken now and then for one small checked block.
/// One cycle runs every phase in its share of the cycle length (an hour by default), so a run of whole cycles says that everything was done; a
/// run shorter than one cycle shortens the cycle so every phase still runs. Each piece of work is checked against a value worked out in advance or
/// an invariant (a sorted list holds the same numbers, JSON read back equals what was written). A reset or freeze shows as the session cut
/// off, with the phase it was in (the stage is written to the checkpoint); Windows' hardware errors during the run fail it as for every test.
/// </summary>
public sealed class DailyUseExecutor : ITestExecutor
{
    public const string CycleOption = "cycleMinutes";
    public static readonly TestDefinition Definition = new(new TestId("system.daily"), "Test_System_Daily", 3600,
        [new TestOption(CycleOption, "Test_Option_CycleMinutes", TestOptionKind.Integer, "60")]);
    TestDefinition ITestExecutor.Definition => Definition;

    internal enum Phase { Bursts, Idle, Light, Spikes, Everyday }

    /// <summary>The phases of one cycle in order and their shares of it: idle comes twice, so the machine settles into it after work of both kinds.</summary>
    internal static readonly (Phase Phase, double Share)[] Cycle =
        [(Phase.Bursts, 0.25), (Phase.Idle, 0.125), (Phase.Light, 0.20), (Phase.Spikes, 0.10), (Phase.Everyday, 0.20), (Phase.Idle, 0.125)];

    internal static string Key(Phase p) => $"Test_Daily_Phase_{p}";

    /// <summary>The length of one cycle: the one asked for, but never longer than the run, so a short run still goes through every phase.</summary>
    internal static double CycleSeconds(int minutes, int duration) => Math.Max(Cycle.Length, Math.Min(Math.Clamp(minutes, 1, 24 * 60) * 60.0, duration));

    private readonly IReadOnlyList<CpuCore> _cores;
    public DailyUseExecutor(IReadOnlyList<CpuCore>? cores = null) => _cores = cores ?? CpuTopology.Cores;

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        double cycle = CycleSeconds((request.Options ?? TestOptions.None(Definition)).GetInt(CycleOption), request.DurationSeconds);
        var done = new TaskCompletionSource<TestRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        // A thread of its own: the bursts pin it to one core after another, which must never happen to a pool thread.
        var thread = new Thread(() => { try { done.SetResult(new Run(request, _cores, cycle, started, ct).Go()); } catch (Exception e) { done.SetException(e); } })
        { IsBackground = true, Name = "Mazesta everyday use" };
        thread.Start();
        return done.Task;
    }

    private sealed class Run(TestExecutionRequest request, IReadOnlyList<CpuCore> cores, double cycleSeconds, DateTimeOffset started, CancellationToken ct)
    {
        private readonly Stopwatch _total = Stopwatch.StartNew();
        private readonly TimeSpan _duration = TimeSpan.FromSeconds(request.DurationSeconds);
        private readonly Random _rng = new(unchecked(Environment.TickCount * 31 + 7));
        private readonly long[] _errors = new long[Enum.GetValues<Phase>().Length], _work = new long[Enum.GetValues<Phase>().Length];
        private readonly long[] _burstsOnCore = new long[cores.Count], _errorsOnCore = new long[cores.Count];
        private readonly HashSet<int> _unpinned = [];
        private int _cycles, _wakes; private string _firstError = "";

        public TestRunResult Go()
        {
            try
            {
                while (!TimeUp)
                {
                    // Phases end at fixed points of the cycle, so one that runs over (a spike, a JSON round trip) shortens the next instead of the cycle.
                    var cycleStart = _total.Elapsed; double at = 0;
                    bool whole = cycleStart + TimeSpan.FromSeconds(cycleSeconds) <= _duration + TimeSpan.FromSeconds(0.5);
                    foreach (var (phase, share) in Cycle)
                    {
                        if (TimeUp) { whole = false; break; }
                        at += share;
                        var until = cycleStart + TimeSpan.FromSeconds(at * cycleSeconds);
                        if (until > _duration) until = _duration;
                        request.Progress?.Invoke(new TestProgress(Percent, "Test_Status_Running", Key(phase)));
                        request.Note("Log_Daily_Phase", null, _cycles + 1, "@" + Key(phase), Math.Round(Math.Max(0, (until - _total.Elapsed).TotalSeconds)));
                        switch (phase)
                        {
                            case Phase.Bursts: Bursts(until); break;
                            case Phase.Idle: Idle(until); break;
                            case Phase.Light: Light(until); break;
                            case Phase.Spikes: Spikes(until); break;
                            default: Everyday(until); break;
                        }
                        ct.ThrowIfCancellationRequested();
                    }
                    if (whole) _cycles++;
                    request.Note("Log_Daily_Cycle", null, _cycles, _errors.Sum());
                }
            }
            catch (OperationCanceledException) { return Result(TestOutcome.Cancelled); }
            request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
            return Result(_errors.Sum() > 0 ? TestOutcome.Failed : TestOutcome.Passed);
        }

        private bool TimeUp => _total.Elapsed >= _duration || ct.IsCancellationRequested;
        private double Percent => Math.Clamp(_total.Elapsed / _duration, 0, 1);

        /// <summary>Sleeps up to <paramref name="ms"/> but not past <paramref name="until"/>; false when the phase (or the run) is over.</summary>
        private bool Rest(int ms, TimeSpan until)
        {
            var left = until - _total.Elapsed;
            if (left <= TimeSpan.Zero) return false;
            ct.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(Math.Min(ms, left.TotalMilliseconds)));
            ct.ThrowIfCancellationRequested();
            return _total.Elapsed < until;
        }

        private void Wrong(Phase p, long n, string what)
        {
            if (n == 0) return;
            Interlocked.Add(ref _errors[(int)p], n);
            lock (_unpinned) if (_firstError.Length == 0) _firstError = $"first wrong result in phase {p}: {what}";
            request.NoteError("Log_Daily_Error", null, "@" + Key(p), what);
        }

        private static long IntegerBlock(ulong[] a, ulong[] seed) => CpuIntegerExecutor.Block(a, seed) == CpuIntegerExecutor.Expected ? 0 : 1;

        // ——— phases ———

        private void Bursts(TimeSpan until)
        {
            var seed = CpuIntegerExecutor.Seed(); var a = new ulong[CpuIntegerExecutor.Elements];
            try
            {
                while (_total.Elapsed < until && !ct.IsCancellationRequested)
                    foreach (int c in Enumerable.Range(0, Math.Max(1, cores.Count)).OrderBy(_ => _rng.Next()))   // every core in a round, in a new order each round
                    {
                        if (_total.Elapsed >= until) break;
                        bool pinned = cores.Count > 0 && CpuTopology.Pin(cores[c].Group, cores[c].FirstThreadMask);
                        if (!pinned && cores.Count > 0) lock (_unpinned) _unpinned.Add(cores[c].Index);
                        int blocks = _rng.Next(1, 21);   // a few milliseconds of work
                        long wrong = 0;
                        for (int i = 0; i < blocks; i++) wrong += IntegerBlock(a, seed);
                        _work[(int)Phase.Bursts] += blocks;
                        if (cores.Count > 0) { _burstsOnCore[c]++; _errorsOnCore[c] += wrong; }
                        Wrong(Phase.Bursts, wrong, cores.Count > 0 ? $"core {cores[c].Index}" : "unpinned");
                        if (!Rest(_rng.Next(10, 2001), until)) break;
                    }
            }
            finally { if (cores.Count > 0) CpuTopology.Pin(cores[0].Group, cores.Aggregate(0UL, (m, x) => x.Group == cores[0].Group ? m | x.Mask : m)); }   // back to every processor of the group
        }

        private void Idle(TimeSpan until)
        {
            var seed = CpuIntegerExecutor.Seed(); var a = new ulong[CpuIntegerExecutor.Elements];
            while (Rest(_rng.Next(5_000, 15_001), until))
            {
                _wakes++; _work[(int)Phase.Idle]++;
                Wrong(Phase.Idle, IntegerBlock(a, seed), "a block computed on waking from idle");
            }
        }

        private void Light(TimeSpan until)
        {
            int threads = Math.Clamp(Environment.ProcessorCount / 4, 2, 4);
            var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
            {
                var (ma, mb) = CpuMatrixStressExecutor.Inputs(t % CpuMatrixStressExecutor.Expected.Length); var mc = new double[CpuMatrixStressExecutor.MatrixSize, CpuMatrixStressExecutor.MatrixSize];
                ulong expected = CpuMatrixStressExecutor.Expected[t % CpuMatrixStressExecutor.Expected.Length];
                var rng = new Random(t * 7919 + Environment.TickCount); var slice = Stopwatch.StartNew(); double share = 0.1 + rng.NextDouble() * 0.2; var shareSince = Stopwatch.StartNew();
                while (_total.Elapsed < until && !ct.IsCancellationRequested)
                {
                    if (shareSince.Elapsed.TotalSeconds > 3) { share = 0.1 + rng.NextDouble() * 0.2; shareSince.Restart(); }   // the load a person makes keeps changing
                    if (slice.Elapsed.TotalMilliseconds >= 100) slice.Restart();
                    if (slice.Elapsed.TotalMilliseconds >= share * 100) { Thread.Sleep(Math.Max(1, (int)(100 - slice.Elapsed.TotalMilliseconds))); continue; }
                    CpuMatrixStressExecutor.Multiply(ma, mb, mc); Interlocked.Increment(ref _work[(int)Phase.Light]);
                    if (CpuMatrixStressExecutor.Checksum(mc) != expected) Wrong(Phase.Light, 1, $"a 64×64 product on light-load thread {t}");
                }
            }) { IsBackground = true, Name = "Mazesta light load" }).ToList();
            workers.ForEach(w => w.Start()); workers.ForEach(w => w.Join());
            ct.ThrowIfCancellationRequested();
        }

        private void Spikes(TimeSpan until)
        {
            var seeds = CpuIntegerExecutor.Seed();
            while (_total.Elapsed < until && !ct.IsCancellationRequested)
            {
                var end = _total.Elapsed + TimeSpan.FromMilliseconds(_rng.Next(500, 3001));
                if (end > until) end = until;
                Parallel.For(0, Environment.ProcessorCount, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct }, _ =>
                {
                    var a = new ulong[CpuIntegerExecutor.Elements];
                    while (_total.Elapsed < end && !ct.IsCancellationRequested) { Interlocked.Increment(ref _work[(int)Phase.Spikes]); Wrong(Phase.Spikes, IntegerBlock(a, seeds), "a block of an all-core spike"); }
                });
                if (!Rest(_rng.Next(2_000, 8_001), until)) break;
            }
        }

        private void Everyday(TimeSpan until)
        {
            byte[] text = CpuHashExecutor.Text(); var packed = new MemoryStream(); var unpacked = new byte[text.Length];
            for (int task = 0; _total.Elapsed < until && !ct.IsCancellationRequested; task++)
            {
                _work[(int)Phase.Everyday]++;
                switch (task % 3)
                {
                    case 0:
                        var (before, after, _) = CpuHashExecutor.Block(text, packed, unpacked);
                        if (before != CpuHashExecutor.ExpectedSha256 || after != CpuHashExecutor.ExpectedSha256) Wrong(Phase.Everyday, 1, "hash and compress a 256 KiB file");
                        break;
                    case 1: Wrong(Phase.Everyday, SortCheck(_rng.Next()), "sort 1 million numbers"); break;
                    default: Wrong(Phase.Everyday, JsonCheck(_rng.Next()), "write and read back 20 000 records as JSON"); break;
                }
                if (!Rest(_rng.Next(50, 501), until)) break;   // the pause between two things a person does
            }
        }

        private TestRunResult Result(TestOutcome outcome)
        {
            var phases = Enum.GetValues<Phase>().Select(p => $"{p} {_work[(int)p]} unit(s), {_errors[(int)p]} wrong");
            int coresBursted = _burstsOnCore.Count(n => n > 0);
            string detail = $"Everyday-use test; cycle {cycleSeconds / 60:0.#} min, {_cycles} whole cycle(s) in {_total.Elapsed.TotalMinutes:0.#} min; "
                + string.Join(", ", phases) + $"; bursts reached {coresBursted} of {cores.Count} cores; {_wakes} wake(s) from idle"
                + (_unpinned.Count > 0 ? $"; Windows would not pin bursts to core(s) {string.Join(", ", _unpinned.Order())}" : "")
                + (_errorsOnCore.Any(e => e > 0) ? $"; wrong results on core(s) {string.Join(", ", _errorsOnCore.Select((e, i) => (e, i)).Where(x => x.e > 0).Select(x => $"{cores[x.i].Index} ×{x.e}"))}" : "")
                + (_firstError.Length > 0 ? $"; {_firstError}" : "");
            return new(Definition.Id, outcome, started, request.Clock.UtcNow, _errors.Sum(), detail);
        }
    }

    /// <summary>Sorts a seeded list of a million numbers; wrong (1) unless it comes out in order and holds the same numbers (their sum and xor).</summary>
    internal static long SortCheck(int seed)
    {
        var rng = new Random(seed); var a = new long[1_000_000]; long sum = 0, xor = 0;
        for (int i = 0; i < a.Length; i++) { a[i] = rng.NextInt64(); sum += a[i]; xor ^= a[i]; }
        Array.Sort(a);
        long sum2 = 0, xor2 = 0; bool ordered = true;
        for (int i = 0; i < a.Length; i++) { sum2 += a[i]; xor2 ^= a[i]; if (i > 0 && a[i - 1] > a[i]) ordered = false; }
        return ordered && sum == sum2 && xor == xor2 ? 0 : 1;
    }

    internal sealed record Item(int Id, string Name, double Price, long[] Codes);

    /// <summary>Writes seeded records as JSON and reads them back; wrong (1) unless every record comes back equal.</summary>
    internal static long JsonCheck(int seed)
    {
        var rng = new Random(seed);
        var items = Enumerable.Range(0, 20_000).Select(i => new Item(i, $"part-{rng.Next():x8}", Math.Round(rng.NextDouble() * 1e6, 2), [rng.NextInt64(), rng.NextInt64()])).ToArray();
        var back = JsonSerializer.Deserialize<Item[]>(JsonSerializer.SerializeToUtf8Bytes(items));
        if (back is null || back.Length != items.Length) return 1;
        for (int i = 0; i < items.Length; i++)
            if (back[i].Id != items[i].Id || back[i].Name != items[i].Name || back[i].Price != items[i].Price || !back[i].Codes.AsSpan().SequenceEqual(items[i].Codes)) return 1;
        return 0;
    }
}
