using System.Globalization;
namespace Mazesta.Diagnostics;

/// <summary>
/// One part's tests one after another as one test, after OCCT's presets (instruction set × data set × steady or variable load): each stage is
/// one of the existing tests with its options, run for its share of the time with its own verification, and the stages go round until the
/// time is up. A part that is stable in one stage and not another (SSE and not AVX2, a steady load and not a swinging one) shows in one run,
/// and the account says which stage it failed in. A stage the part cannot run (AVX-512 on most desktop chips) is skipped and said, never
/// counted as a pass or a fault; when no stage could run the whole test is Unsupported. A stage the time ran out before is not a pass either:
/// a clean run that left one unreached is Inconclusive (a shorter stage length, or fewer stages, fits them all). An option of the cycle itself other than its preset,
/// stages and stage length (the GPU to use) is handed to every stage that has an option of that key and does not set it.
/// </summary>
public sealed class StageCycleExecutor(TestDefinition definition, IReadOnlyDictionary<string, string> presets, IReadOnlyDictionary<string, ITestExecutor> tests) : ITestExecutor
{
    public const string PresetOption = "preset", StagesOption = "stages", StageSecondsOption = "stageSeconds", Custom = "custom";
    public TestDefinition Definition => definition;

    /// <summary>The options every cycle has: its preset (each with the key <paramref name="presetKeyPrefix"/> + id), the stages typed for
    /// "custom" (starting from the first preset's), the seconds per stage; then <paramref name="more"/>.</summary>
    public static TestOption[] Options(IReadOnlyDictionary<string, string> presets, string presetKeyPrefix, string stagesLabelKey, params TestOption[] more) =>
    [
        new TestOption(PresetOption, "Test_Option_Preset", TestOptionKind.Choice, presets.Keys.First(),
            () => [.. presets.Keys.Append(Custom).Select(p => new OptionChoice(p, presetKeyPrefix + p, true, presets.GetValueOrDefault(p)))]),
        new TestOption(StagesOption, stagesLabelKey, TestOptionKind.Text, presets.Values.First(), When: PresetOption + "=" + Custom),
        new TestOption(StageSecondsOption, "Test_Option_StageSeconds", TestOptionKind.Integer, "120"),
        .. more,
    ];

    public sealed record Stage(string TestId, IReadOnlyDictionary<string, string> Options)
    {
        public override string ToString() => Options.Count == 0 ? TestId : $"{TestId}:{string.Join(",", Options.Select(o => $"{o.Key}={o.Value}"))}";
    }

    /// <summary>Reads a list of stages; throws <see cref="FormatException"/> for a test that cannot be a stage here or an option it does not have.</summary>
    public IReadOnlyList<Stage> Parse(string text) => Parse(text, tests, definition.Id.Value);

    public static IReadOnlyList<Stage> Parse(string text, IReadOnlyDictionary<string, ITestExecutor> tests, string self)
    {
        var stages = new List<Stage>();
        foreach (string word in text.Split([' ', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = word.IndexOf(':');
            string id = colon < 0 ? word : word[..colon];
            if (id == self || !tests.TryGetValue(id, out var test)) throw new FormatException($"'{id}' is not a test that can be a stage here ({string.Join(", ", tests.Keys.Where(k => k != self))}).");
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

    /// <summary>The stage's own options, and the cycle's other options (the GPU to use) for every key the stage has and does not set itself.</summary>
    private Dictionary<string, string> Forwarded(Stage stage, ITestExecutor test, TestOptions cycle)
    {
        var all = new Dictionary<string, string>(stage.Options);
        foreach (var opt in definition.Options)
            if (opt.Key is not (PresetOption or StagesOption or StageSecondsOption) && test.Definition.Options.Any(x => x.Key == opt.Key)) all.TryAdd(opt.Key, cycle.Get(opt.Key));
        return all;
    }

    /// <summary>How long each stage runs: the length asked for, but short enough that every stage runs at least once.</summary>
    internal static int StageSeconds(int asked, int duration, int stages) => Math.Max(1, Math.Min(Math.Max(1, asked), duration / Math.Max(1, stages)));

    public async Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow; var o = request.Options ?? TestOptions.None(definition);
        if (request.DurationSeconds <= 0) return TestRunResult.Unsupported(definition.Id, started, "Duration must be positive.");
        IReadOnlyList<Stage> stages;
        try { stages = Parse(presets.GetValueOrDefault(o.Get(PresetOption)) ?? o.Get(StagesOption)); }
        catch (FormatException e) { return TestRunResult.Unsupported(definition.Id, started, $"The stages cannot be read: {e.Message}"); }

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
                    DurationSeconds = seconds, Options = new TestOptions(test.Definition, Forwarded(stage, test, o)),
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
        if (results.All(r => r is null)) return TestRunResult.Unsupported(definition.Id, started, $"No stage could run on this CPU: {string.Join("; ", stages)}.");
        var worst = results.Where(r => r is not null).Aggregate((a, b) => a!.Combine(b!))!;
        bool unreached = stages.Where((_, i) => !skipped.Contains(i) && results[i] is null).Any();
        return Result(worst.Outcome == TestOutcome.Passed && unreached ? TestOutcome.Inconclusive : worst.Outcome);

        TestRunResult Result(TestOutcome outcome)
        {
            var lines = stages.Select((s, i) => skipped.Contains(i) ? $"{s}: skipped (not supported here)"
                : results[i] is { } r ? $"{s}: {r.Outcome}, {r.ErrorCount} error(s), {runs[i]} run(s) of ≤{stageSeconds} s{(r.Outcome is TestOutcome.Failed or TestOutcome.Inconclusive or TestOutcome.Error ? $" ({r.Detail})" : "")}"
                : $"{s}: not reached");
            long errors = results.Sum(r => r?.ErrorCount ?? 0);
            return new(definition.Id, outcome, started, request.Clock.UtcNow, errors,
                $"{definition.Id} stage cycle, preset '{o.Get(PresetOption)}', {stageSeconds.ToString(CultureInfo.InvariantCulture)} s per stage; " + string.Join("; ", lines));
        }
    }
}
