using System.Diagnostics; using System.Globalization; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Cpu;

/// <summary>
/// The processor's full-load test: the three workloads that each took every logical processor to 100 % (the matrix products of
/// <see cref="CpuMatrixStressExecutor"/>, the integer arithmetic of <see cref="CpuIntegerExecutor"/>, the hashing and compression of
/// <see cref="CpuHashExecutor"/>) as the stages of one test, which goes from one to the next every <see cref="StageOption"/> seconds and round
/// again until its time is up. Each stage keeps its own check (every block against a value worked out in advance, never one from the machine
/// under test) and its own count of blocks and errors. The log says when a stage begins, and the stage is handed on with the progress
/// (<see cref="TestProgress.Stage"/>), which the engine writes to the session's checkpoint at once: after a reset or a crash the next start
/// names the stage the machine was in.
/// The load can also swing (<see cref="PatternOption"/>): <see cref="HighOption"/> per cent for <see cref="HighSecondsOption"/> seconds, then
/// <see cref="LowOption"/> per cent for <see cref="LowSecondsOption"/>, over and over - the steps of load a power supply and a CPU's voltage
/// regulation answer worst. A share of load is a share of time: every thread works that part of each tenth of a second and sleeps the rest.
/// </summary>
public sealed class CpuStressExecutor : ITestExecutor
{
    public const string StageOption = "stageSeconds", PatternOption = "pattern", HighOption = "high", LowOption = "low", HighSecondsOption = "highSeconds", LowSecondsOption = "lowSeconds";
    public static readonly TestDefinition Definition = new(new TestId("cpu.stress"), "Test_Cpu_Stress", 300,
    [
        new TestOption(StageOption, "Test_Option_StageSeconds", TestOptionKind.Integer, "60"),
        new TestOption(PatternOption, "Test_Option_LoadPattern", TestOptionKind.Choice, "steady", () => [new("steady", "Test_LoadPattern_Steady", true), new("variable", "Test_LoadPattern_Variable", true)]),
        new TestOption(HighOption, "Test_Option_LoadHigh", TestOptionKind.Integer, "100"), new TestOption(HighSecondsOption, "Test_Option_LoadHighSeconds", TestOptionKind.Integer, "20"),
        new TestOption(LowOption, "Test_Option_LoadLow", TestOptionKind.Integer, "0"), new TestOption(LowSecondsOption, "Test_Option_LoadLowSeconds", TestOptionKind.Integer, "10"),
    ]);
    TestDefinition ITestExecutor.Definition => Definition;

    /// <summary>The stages in the order they run, each with the key of its name and of the log line that begins it.</summary>
    internal static readonly (string Name, string NameKey, string Formula)[] Stages =
    [
        ("matrix", "Test_Cpu_Stage_Matrix", $"C[i,j] = Σk A[i,k]·B[k,j], {CpuMatrixStressExecutor.MatrixSize}×{CpuMatrixStressExecutor.MatrixSize} FP64   check: FNV-1a(bits of every C[i,j]) == expected[set]"),
        ("integer", "Test_Cpu_Stage_Integer", "x = x·K + (x≫17); x ^= x≪13; x = rotl(x, 23); x += x / (y|1); branch on x&7   check: FNV-1a(result) == expected"),
        ("hash", "Test_Cpu_Stage_Hash", "h1 = SHA-256(text); z = Deflate(text); t = Inflate(z); h2 = SHA-256(t)   check: h1 == h2 == expected"),
    ];
    /// <summary>Begins the part of a result's account that says the load was not held at 100 %: the checkup judges only full-load runs.</summary>
    public const string PartLoadMark = "part load: ";
    private const double Slice = 0.1;   // seconds: the window a share of load is kept within

    /// <summary>How long each stage runs: the length asked for, but short enough that a run goes through all three at least once.</summary>
    internal static double StageSeconds(int asked, int duration) => Math.Max(1, Math.Min(Math.Max(1, asked), duration / (double)Stages.Length));

    /// <summary>The stage that runs at <paramref name="seconds"/> into the test.</summary>
    internal static int StageAt(double seconds, double stageSeconds) => (int)(seconds / stageSeconds) % Stages.Length;

    /// <summary>The share of load (0-1) asked for at <paramref name="seconds"/> into the test: steady at the high level, or high and low in turn.</summary>
    internal static double LoadAt(double seconds, bool variable, int high, int low, int highSeconds, int lowSeconds)
    {
        double top = Math.Clamp(high, 1, 100) / 100.0;
        if (!variable) return top;
        double h = Math.Max(1, highSeconds), l = Math.Max(1, lowSeconds);
        return seconds % (h + l) < h ? top : Math.Clamp(low, 0, 100) / 100.0;
    }

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        return Task.Run(() => Run(request, started, ct), CancellationToken.None);
    }

    private static TestRunResult Run(TestExecutionRequest request, DateTimeOffset started, CancellationToken ct)
    {
        var o = request.Options ?? TestOptions.None(Definition);
        bool variable = o.Get(PatternOption) == "variable"; int high = o.GetInt(HighOption), low = o.GetInt(LowOption), highSeconds = o.GetInt(HighSecondsOption), lowSeconds = o.GetInt(LowSecondsOption);
        double stageSeconds = StageSeconds(o.GetInt(StageOption), request.DurationSeconds);
        int threads = Environment.ProcessorCount; var coverage = new CpuCoverage();
        var blocks = new long[Stages.Length]; var errors = new long[Stages.Length]; var spent = new double[Stages.Length]; string firstError = "";
        var matrices = Enumerable.Range(0, CpuMatrixStressExecutor.Sets).Select(CpuMatrixStressExecutor.Inputs).ToArray(); var seed = CpuIntegerExecutor.Seed(); var text = CpuHashExecutor.Text();
        var total = Stopwatch.StartNew(); var duration = TimeSpan.FromSeconds(request.DurationSeconds);
        request.Note("Log_CpuStress_Start", null, threads, Stages.Length, stageSeconds);
        if (variable) request.Note("Log_CpuStress_Pattern", "share of load = share of each 100 ms a thread works", Math.Clamp(high, 1, 100), Math.Max(1, highSeconds), Math.Clamp(low, 0, 100), Math.Max(1, lowSeconds));

        void Wrong(int stage, int thread, string what)
        {
            Interlocked.Increment(ref errors[stage]);
            if (firstError.Length == 0) { firstError = $"first wrong block in the {Stages[stage].Name} stage, thread {thread}: {what}"; request.NoteError("Log_Wrong_Result", firstError); }
        }
        void Worker(int index)
        {
            var c = new double[CpuMatrixStressExecutor.MatrixSize, CpuMatrixStressExecutor.MatrixSize]; var a = new ulong[CpuIntegerExecutor.Elements];
            var packed = new MemoryStream(CpuHashExecutor.Bytes); var unpacked = new byte[CpuHashExecutor.Bytes]; long n = index;
            while (!ct.IsCancellationRequested && total.Elapsed < duration)
            {
                double now = total.Elapsed.TotalSeconds, load = LoadAt(now, variable, high, low, highSeconds, lowSeconds), within = now % Slice;
                if (within >= load * Slice) { Thread.Sleep(Math.Max(1, (int)((Slice - within) * 1000))); continue; }   // this slice's share is done
                int stage = StageAt(now, stageSeconds);
                switch (stage)
                {
                    case 0:
                        int set = (int)(n++ % CpuMatrixStressExecutor.Sets);
                        CpuMatrixStressExecutor.Multiply(matrices[set].A, matrices[set].B, c);
                        if (CpuMatrixStressExecutor.Checksum(c) != CpuMatrixStressExecutor.Expected[set]) Wrong(stage, index, $"the product of input set {set} differs from its checksum");
                        break;
                    case 1:
                        if (CpuIntegerExecutor.Block(a, seed) != CpuIntegerExecutor.Expected) Wrong(stage, index, "the checksum differs");
                        break;
                    default:
                        var (before, after, _) = CpuHashExecutor.Block(text, packed, unpacked);
                        if (before != CpuHashExecutor.ExpectedSha256 || after != CpuHashExecutor.ExpectedSha256) Wrong(stage, index, before != CpuHashExecutor.ExpectedSha256 ? "the hash of the input differs" : "the hash after the round trip differs");
                        break;
                }
                Interlocked.Increment(ref blocks[stage]); coverage.Mark();
            }
        }
        var all = Task.WhenAll(Enumerable.Range(0, threads).Select(i => Task.Factory.StartNew(() => Worker(i), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)));
        // The watcher: says each stage as it begins and hands it on with the progress. With every processor taken by the workers it may wake
        // late, so it runs above them and, if a stage went by between two wakes, still names it - no stage is missing from the log.
        int turn = -1; double level = -1, last = 0; var priority = Thread.CurrentThread.Priority; Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        void Watch()
        {
            double now = Math.Min(total.Elapsed.TotalSeconds, request.DurationSeconds - 0.001); int at = (int)(now / stageSeconds);
            if (turn >= 0) spent[turn % Stages.Length] += Math.Min(now, (turn + 1) * stageSeconds) - last;
            while (turn < at)
            {
                if (turn >= 0) request.Note("Log_CpuStress_StageEnd", null, "@" + Stages[turn % Stages.Length].NameKey, Interlocked.Read(ref blocks[turn % Stages.Length]), Interlocked.Read(ref errors[turn % Stages.Length]));
                turn++; int stage = turn % Stages.Length;
                request.Note("Log_CpuStress_Stage", Stages[stage].Formula, stage + 1, Stages.Length, "@" + Stages[stage].NameKey, turn / Stages.Length + 1);
                request.Progress?.Invoke(new TestProgress(Math.Clamp(now / request.DurationSeconds, 0, 1), "Test_Status_Running", Stages[stage].NameKey));
                if (turn > 0) spent[stage] += Math.Min(now, (turn + 1) * stageSeconds) - turn * stageSeconds;
            }
            last = now;
            double asked = LoadAt(now, variable, high, low, highSeconds, lowSeconds);
            if (variable && asked != level) { request.Note("Log_CpuStress_Level", null, asked * 100, "@" + Stages[turn % Stages.Length].NameKey); level = asked; }
            request.Progress?.Invoke(new TestProgress(Math.Clamp(now / request.DurationSeconds, 0, 1), "Test_Status_Running", Stages[turn % Stages.Length].NameKey));
        }
        try { while (!all.IsCompleted) { Watch(); all.Wait(250, CancellationToken.None); } if (!ct.IsCancellationRequested) Watch(); }
        finally { Thread.CurrentThread.Priority = priority; }
        var finished = request.Clock.UtcNow; long wrong = errors.Sum();
        string Stage(int k) => $"{Stages[k].Name}: {blocks[k]} blocks in {spent[k].ToString("F0", CultureInfo.InvariantCulture)} s, {errors[k]} wrong";
        string detail = SensorEvidence.Join($"full load in {Stages.Length} stages of {stageSeconds.ToString("F0", CultureInfo.InvariantCulture)} s on {threads} threads, every block checked against a value worked out in advance",
            Stage(0), Stage(1), Stage(2),
            variable ? $"{PartLoadMark}{Math.Clamp(high, 1, 100)}% for {Math.Max(1, highSeconds)} s, then {Math.Clamp(low, 0, 100)}% for {Math.Max(1, lowSeconds)} s, in turn" : high < 100 ? $"{PartLoadMark}held at {Math.Clamp(high, 1, 100)}%" : null,
            coverage.Describe(CpuTopology.Cores), firstError.Length > 0 ? firstError : null,
            SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuTotalLoad, started, finished)?.Format("measured CPU load", "%"),
            SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuPackagePower, started, finished)?.Format("CPU package power", " W", includeMax: true),
            SensorEvidence.CpuTemperature(request.Engine, started, finished)?.Format("CPU temperature", "°C", includeMax: true));
        if (ct.IsCancellationRequested) return new(Definition.Id, TestOutcome.Cancelled, started, finished, wrong, detail);
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, wrong > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, finished, wrong, detail);
    }
}
