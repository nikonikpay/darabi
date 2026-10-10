using System.Globalization;
namespace Mazesta.Diagnostics.Cpu;

/// <summary>
/// The processor's tests one after another as one test, after OCCT's CPU presets (instruction set × data set × steady or variable load): each
/// stage is one of the existing CPU tests with its options, run for its share of the time with its own verification, and the stages go round
/// until the time is up. A CPU that is stable on one instruction width and not another, or at full load and not when the load swings, shows
/// in one run, and the account says which stage it failed in. A stage the CPU cannot run (AVX-512 on most desktop chips) is skipped and said,
/// never counted as a pass or a fault; when no stage could run the whole test is Unsupported.
/// </summary>
public sealed class CpuCycleExecutor(IReadOnlyDictionary<string, ITestExecutor> tests) : ITestExecutor
{
    public const string PresetOption = "preset", StagesOption = "stages", StageSecondsOption = "stageSeconds", Custom = "custom";

    /// <summary>A stage: a CPU test's id and the options it runs with, written "test:option=value,option=value".</summary>
    public static readonly IReadOnlyDictionary<string, string> Presets = new Dictionary<string, string>
    {
        // Every instruction width, then the work that loads the caches and memory (FFT, Linpack) and the integer/hash/matrix mix.
        ["full"] = "cpu.vector:width=sse cpu.vector:width=avx2 cpu.vector:width=avx512 cpu.fft cpu.linpack:memory=single cpu.stress:pattern=steady",
        // The same arithmetic on each width in turn: a CPU (or undervolt) that holds on SSE and fails on AVX2 / AVX-512 shows here.
        ["instructions"] = "cpu.vector:width=sse cpu.vector:width=avx2 cpu.vector:width=avx512",
        // Load that swings and a single core that boosts and rests: where curve offsets and Vmin faults show, not at a steady full load.
        ["transient"] = "cpu.stress:pattern=variable cpu.singlecore:load=variable cpu.vector:width=avx2 cpu.stress:pattern=variable,high=100,low=20,highSeconds=3,lowSeconds=2",
    };

    public static readonly TestDefinition Definition = new(new TestId("cpu.cycle"), "Test_Cpu_Cycle", 1200,
    [
        new TestOption(PresetOption, "Test_Option_CpuPreset", TestOptionKind.Choice, "full",
            () => [.. Presets.Keys.Append(Custom).Select(p => new OptionChoice(p, $"Test_CpuPreset_{p}", true, Presets.GetValueOrDefault(p)))]),
        new TestOption(StagesOption, "Test_Option_CpuStages", TestOptionKind.Text, Presets["full"], When: PresetOption + "=" + Custom),
        new TestOption(StageSecondsOption, "Test_Option_StageSeconds", TestOptionKind.Integer, "120"),
    ]);
    TestDefinition ITestExecutor.Definition => Definition;

    /// <summary>The cycle over this program's own CPU tests (fresh instances, so a stage never shares state with the same test in the queue).</summary>
    public static CpuCycleExecutor Standard(Memory.IMemoryProbe probe) => new(new ITestExecutor[]
        { new CpuStressExecutor(), new CpuCoreCycleExecutor(), new LinpackExecutor(probe), new CpuVectorStressExecutor(), new CpuFftExecutor(probe) }
        .ToDictionary(t => t.Definition.Id.Value));

    public sealed record Stage(string TestId, IReadOnlyDictionary<string, string> Options)
    {
        public override string ToString() => Options.Count == 0 ? TestId : $"{TestId}:{string.Join(",", Options.Select(o => $"{o.Key}={o.Value}"))}";
    }

    /// <summary>Reads a list of stages; throws <see cref="FormatException"/> for a test that is not a CPU test here or an option it does not have.</summary>
    public static IReadOnlyList<Stage> Parse(string text, IReadOnlyDictionary<string, ITestExecutor> tests)
    {
        var stages = new List<Stage>();
        foreach (string word in text.Split([' ', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = word.IndexOf(':');
            string id = colon < 0 ? word : word[..colon];
            if (id == Definition.Id.Value || !tests.TryGetValue(id, out var test)) throw new FormatException($"'{id}' is not a CPU test that can be a stage ({string.Join(", ", tests.Keys.Where(k => k != Definition.Id.Value))}).");
            var options = new Dictionary<string, string>();
            if (colon >= 0)
                foreach (string pair in word[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    string key = eq < 0 ? pair : pair[..eq];
                    if (eq < 0 || test.Definition.Options.All(o => o.Key != key)) throw new FormatException($"'{id}' has no option '{key}' (or it has no value).");
                    options[key] = pair[(eq + 1)..];
                }
            stages.Add(new(id, options));
        }
        return stages.Count > 0 ? stages : throw new FormatException("There are no stages.");
    }

    /// <summary>How long each stage runs: the length asked for, but short enough that every stage runs at least once.</summary>
    internal static int StageSeconds(int asked, int duration, int stages) => Math.Max(1, Math.Min(Math.Max(1, asked), duration / Math.Max(1, stages)));

    public async Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow; var o = request.Options ?? TestOptions.None(Definition);
        if (request.DurationSeconds <= 0) return TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive.");
        IReadOnlyList<Stage> stages;
        try { stages = Parse(Presets.GetValueOrDefault(o.Get(PresetOption)) ?? o.Get(StagesOption), tests); }
        catch (FormatException e) { return TestRunResult.Unsupported(Definition.Id, started, $"The stages cannot be read: {e.Message}"); }

        int stageSeconds = StageSeconds(o.GetInt(StageSecondsOption), request.DurationSeconds, stages.Count);
        var skipped = new HashSet<int>(); var runs = new int[stages.Count]; var results = new TestRunResult?[stages.Count];
        double done = 0;
        for (int round = 0; done < request.DurationSeconds; round++)
        {
            bool ranAny = false;
            for (int i = 0; i < stages.Count && done < request.DurationSeconds; i++)
            {
                if (skipped.Contains(i)) continue;
                var stage = stages[i]; var test = tests[stage.TestId];
                int seconds = (int)Math.Max(1, Math.Min(stageSeconds, request.DurationSeconds - done));
                double from = done;
                request.Note("Log_CpuCycle_Stage", null, round + 1, i + 1, stages.Count, stage.ToString(), seconds);
                var sub = request with
                {
                    DurationSeconds = seconds, Options = new TestOptions(test.Definition, stage.Options),
                    Progress = p => request.Progress?.Invoke(new TestProgress(Math.Clamp((from + p.PercentComplete * seconds) / request.DurationSeconds, 0, 1), p.StatusKey, p.Stage ?? test.Definition.NameKey)),
                };
                var r = await test.RunAsync(sub, ct).ConfigureAwait(false);
                if (r.Outcome == TestOutcome.Unsupported)
                {
                    skipped.Add(i); request.NoteWarning("Log_CpuCycle_Skipped", null, stage.ToString(), r.Detail ?? "");
                    continue;
                }
                done += seconds; runs[i]++; ranAny = true;
                results[i] = results[i] is { } before ? before.Combine(r) : r;
                if (r.Outcome == TestOutcome.Cancelled || ct.IsCancellationRequested) return Result(TestOutcome.Cancelled);
            }
            if (!ranAny) break;
        }
        if (results.All(r => r is null)) return TestRunResult.Unsupported(Definition.Id, started, $"No stage could run on this CPU: {string.Join("; ", stages)}.");
        var worst = results.Where(r => r is not null).Aggregate((a, b) => a!.Combine(b!))!;
        return Result(worst.Outcome == TestOutcome.Error ? TestOutcome.Error : worst.Outcome);

        TestRunResult Result(TestOutcome outcome)
        {
            var lines = stages.Select((s, i) => skipped.Contains(i) ? $"{s}: skipped (not supported here)"
                : results[i] is { } r ? $"{s}: {r.Outcome}, {r.ErrorCount} error(s), {runs[i]} run(s) of ≤{stageSeconds} s{(r.Outcome is TestOutcome.Failed or TestOutcome.Inconclusive or TestOutcome.Error ? $" ({r.Detail})" : "")}"
                : $"{s}: not reached");
            long errors = results.Sum(r => r?.ErrorCount ?? 0);
            return new(Definition.Id, outcome, started, request.Clock.UtcNow, errors,
                $"CPU stage cycle, preset '{o.Get(PresetOption)}', {stageSeconds.ToString(CultureInfo.InvariantCulture)} s per stage; " + string.Join("; ", lines));
        }
    }
}
