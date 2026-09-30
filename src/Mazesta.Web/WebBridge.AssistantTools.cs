using System.Globalization; using System.IO; using System.Text.Json; using System.Text.Json.Nodes;
using Mazesta.Core.Hardware; using Mazesta.Desktop.Composition; using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Ai; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Monitoring; using Mazesta.Reporting;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    /// <summary>The tests the assistant may start for each area it names: short, safe ones. Nothing that stresses the GPU or changes Windows is in it.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> AssistantTestAreas = new Dictionary<string, string[]>
    {
        ["cpu"] = ["cpu.matrix", "cpu.integer", "cpu.fft"], ["memory"] = ["memory.pattern"],
        ["storage"] = ["storage.smart", "storage.sequential"], ["network"] = ["network.latency"],
    };

    /// <summary>The benchmarks the assistant may start: the ones that do not need the graphics card (it is busy serving the assistant).</summary>
    private static readonly IReadOnlyDictionary<string, string> AssistantBenchmarks = new Dictionary<string, string>
    {
        ["cpu_single"] = "bench.cpu.single", ["cpu_multi"] = "bench.cpu.multi", ["memory"] = "bench.memory", ["storage"] = "bench.storage",
    };

    /// <summary>
    /// What the assistant may call. The first four only read. <c>run_tests</c> and <c>run_benchmark</c> start a real run, but only after the user said
    /// yes on the page (<paramref name="ask"/>); what they return is what the engine reported, outcome names unchanged, so a test that did not run is
    /// never told as a pass. A value that is not available is left out, never written as 0. The page shows which tools ran beside the answer.
    /// </summary>
    private IReadOnlyList<AiTool> AssistantTools(Func<SensorSnapshot?> latest, Func<string, IReadOnlyList<(string, string)>, CancellationToken, Task<bool>> ask,
        Func<Activity, Func<Task<string>>, Task<string>> running, Func<Func<Task<string>>, Task<string>> ui)
    {
        var engine = _sp.GetRequiredService<PollingEngine>(); var inventory = _sp.GetRequiredService<InventoryCache>(); var runner = _sp.GetRequiredService<BenchmarkRunner>();
        static string Json(object o) => JsonSerializer.Serialize(o, Json_);
        static string? Text(JsonElement a, string name) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static int Int(JsonElement a, string name, int fallback, int max) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? Math.Clamp(n, 1, max) : fallback;
        static string[] Texts(JsonElement a, string name) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)] : [];
        static string Cut(string? s, int n) => s is null ? "" : s.Length <= n ? s : s[..n] + "…";
        Task<string> OnUi(Func<string> work) => ui(() => Task.FromResult(work()));
        const string NoArgs = """{"type":"object","properties":{}}""";

        return
        [
            new("get_machine_summary", "The computer's specification: processor, graphics cards, memory, operating system, motherboard, BIOS and the drives with their health.", NoArgs,
                async (_, _) =>
                {
                    var inv = await inventory.GetAsync().ConfigureAwait(false);
                    return Json(new
                    {
                        cpu = inv.Cpu?.Name?.Trim(), gpus = inv.Gpus.Select(g => g.Name?.Trim()), ramGb = inv.TotalPhysicalMemoryBytes is { } b ? Math.Round(b / 1073741824.0, 1) : (double?)null,
                        os = inv.Os?.Caption, board = inv.Motherboard is { } m ? $"{m.Manufacturer} {m.Product}".Trim() : null, bios = inv.Bios?.Version,
                        drives = inv.Storage.Select(d => new { name = d.FriendlyName, health = SystemInfoViewModel.DriveHealth(d.HealthStatus, d.WearPercent) }),
                    });
                }),
            new("get_sensors", "The live sensor readings now (temperatures, loads, clocks, power, fans), grouped by device. Optional kind: Temperature, Load, Clock, Power or Fan.",
                """{"type":"object","properties":{"kind":{"type":"string","enum":["Temperature","Load","Clock","Power","Fan"]}}}""",
                (a, _) =>
                {
                    var snap = latest(); if (snap is null) return Task.FromResult(Json(new { error = "no sensor reading has arrived yet" }));
                    string? kind = Text(a, "kind"); var values = snap.Readings.Where(r => r.Quality == DataQuality.Ok && r.Value is not null).ToDictionary(r => r.Id, r => r.Value!.Value);
                    var devices = engine.Hardware.Select(n => new
                    {
                        device = n.Name, part = n.Kind.ToString(),
                        sensors = n.Sensors.Where(s => values.ContainsKey(s.Id) && (kind is null || s.Kind.ToString() == kind) && s.Kind.ToString() is "Temperature" or "Load" or "Clock" or "Power" or "Fan")
                            .Take(12).Select(s => new { name = s.Name, kind = s.Kind.ToString(), value = Math.Round(values[s.Id], 1), unit = s.Unit.ToString() }),
                    }).Where(d => d.sensors.Any()).Take(8);
                    return Task.FromResult(Json(new { secondsAgo = Math.Round((DateTimeOffset.UtcNow - snap.Timestamp).TotalSeconds), devices }));
                }),
            new("list_reports", "The newest saved test reports: when, kind, verdict and how many tests passed, failed or did not run. Optional limit (default 5, at most 10).",
                """{"type":"object","properties":{"limit":{"type":"integer"}}}""",
                (a, _) => Task.FromResult(Json(new
                {
                    reports = new ReportStore(_paths.ReportsDir).List().OrderByDescending(r => r.CreatedAt).Take(Int(a, "limit", 5, 10))
                        .Select(r => new { createdAt = r.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), kind = r.Kind.ToString(), verdict = r.Verdict?.ToString(), r.Counts, benchmarks = r.Benchmarks }),
                }))),
            new("get_benchmark_history", "This computer's newest benchmark results, newest first, to see whether it got slower or faster. Optional benchmark (part of its id) and limit (default 6, at most 15).",
                """{"type":"object","properties":{"benchmark":{"type":"string"},"limit":{"type":"integer"}}}""",
                (a, _) =>
                {
                    if (_benchRunLog is null || _benchSystemHash?.Invoke() is not { } hash) return Task.FromResult(Json(new { error = "the benchmark history is not available yet" }));
                    string? name = Text(a, "benchmark");
                    var runs = _benchRunLog.Recent(hash, 300).Where(r => name is null || r.Benchmark.Contains(name, StringComparison.OrdinalIgnoreCase)).Take(Int(a, "limit", 6, 15))
                        .Select(r => new { at = r.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), benchmark = r.Benchmark, settings = r.Settings, value = Math.Round(r.Value, 2), unit = r.Unit, overclocked = r.Overclocked });
                    return Task.FromResult(Json(new { runs }));
                }),
            new("run_tests", "Runs real hardware tests, after the user confirmed on the page, and returns each test's outcome. It takes minutes. Areas: cpu, memory, storage, network. " +
                "An outcome other than Passed (Failed, Cancelled, Unsupported, NotRun, Error, Inconclusive) is never to be told as a pass.",
                """{"type":"object","properties":{"areas":{"type":"array","items":{"type":"string","enum":["cpu","memory","storage","network"]}}},"required":["areas"]}""",
                async (a, ct) =>
                {
                    if (_testVm is not { } tests) return Json(new { error = "the tests are not available yet" });
                    var ids = Texts(a, "areas").Distinct().SelectMany(x => AssistantTestAreas.GetValueOrDefault(x) ?? []).ToHashSet();
                    if (ids.Count == 0) return Json(new { error = "name at least one area: cpu, memory, storage or network" });
                    // The rows this machine can run, with their default lengths; the user sees exactly this list before anything starts.
                    var plan = await OnUi(() => tests.IsRunning || runner.IsBusy ? "" : Json(tests.Rows.Where(r => ids.Contains(r.Definition.Id.Value) && r.IsAvailable)
                        .Select(r => new { id = r.Definition.Id.Value, name = r.Name, seconds = r.Definition.DefaultDurationSeconds }))).ConfigureAwait(false);
                    if (plan.Length == 0) return Json(new { error = "a test or a benchmark is already running; nothing was started" });
                    var rows = JsonSerializer.Deserialize<List<JsonElement>>(plan)!;
                    if (rows.Count == 0) return Json(new { error = "this computer can not run those tests" });
                    var items = rows.Select(r => (r.GetProperty("name").GetString()!, r.GetProperty("seconds").GetInt32().ToString(CultureInfo.InvariantCulture))).ToList();
                    if (!await ask("tests", items, ct).ConfigureAwait(false)) return Json(new { started = false, reason = "the user declined; nothing was run" });

                    var chosen = rows.Select(r => r.GetProperty("id").GetString()!).ToHashSet();
                    return await running(new("tests", () => tests.CurrentRow is { } cur ? (cur.Name, Math.Round((tests.CurrentIndex + cur.PercentComplete) / Math.Max(1, tests.RunQueue.Count) * 100)) : null), async () =>
                    {
                        using var stop = ct.Register(() => _window.Dispatcher.BeginInvoke(() => { if (tests.CancelCommand.CanExecute(null)) tests.CancelCommand.Execute(null); }));
                        // The page's own selection is put back afterwards; the assistant only borrows the Tests page's queue.
                        var before = await ui(() => Task.FromResult(Json(tests.Rows.Select(r => new { id = r.Definition.Id.Value, on = r.IsSelected, sec = r.DurationText })))).ConfigureAwait(false);
                        string started = await ui(async () =>
                        {
                            foreach (var r in tests.Rows) { r.IsSelected = chosen.Contains(r.Definition.Id.Value); if (r.IsSelected) r.DurationText = r.Definition.DefaultDurationSeconds.ToString(CultureInfo.InvariantCulture); }
                            if (!tests.StartCommand.CanExecute(null)) return "refused";
                            await tests.StartCommand.ExecuteAsync(null);
                            return tests.BlockedMessage is { } why ? why : "";
                        }).ConfigureAwait(false);
                        try
                        {
                            if (started.Length > 0) return Json(new { started = false, reason = started == "refused" ? "the test queue could not start" : started });
                            return await OnUi(() => Json(new
                            {
                                started = true, results = tests.Rows.Where(r => chosen.Contains(r.Definition.Id.Value))
                                    .Select(r => new { id = r.Definition.Id.Value, name = r.Name, outcome = r.Outcome.ToString(), errors = r.ErrorCount > 0 ? r.ErrorCount : (long?)null, detail = r.HasDetail ? Cut(r.Detail, 240) : null }),
                            })).ConfigureAwait(false);
                        }
                        finally
                        {
                            await ui(() =>
                            {
                                var saved = JsonSerializer.Deserialize<List<JsonElement>>(before)!;
                                foreach (var s in saved) if (tests.Rows.FirstOrDefault(r => r.Definition.Id.Value == s.GetProperty("id").GetString()) is { } r) { r.IsSelected = s.GetProperty("on").GetBoolean(); r.DurationText = s.GetProperty("sec").GetString()!; }
                                return Task.FromResult("");
                            }).ConfigureAwait(false);
                        }
                    }).ConfigureAwait(false);
                }),
            new("run_benchmark", "Runs one real benchmark, after the user confirmed on the page, and returns its number, the best earlier result of this computer and the change against it " +
                "(positive change is better, negative is slower than the best kept). It takes about a minute. Benchmarks: cpu_single, cpu_multi, memory, storage. For a trend over time use get_benchmark_history.",
                """{"type":"object","properties":{"benchmark":{"type":"string","enum":["cpu_single","cpu_multi","memory","storage"]}},"required":["benchmark"]}""",
                async (a, ct) =>
                {
                    if (_benchVm is not { } bench || _benchCompared is not { } compared) return Json(new { error = "the benchmarks are not available yet" });
                    if (Text(a, "benchmark") is not { } key || !AssistantBenchmarks.TryGetValue(key, out string? id)) return Json(new { error = "benchmark must be cpu_single, cpu_multi, memory or storage" });
                    var plan = await OnUi(() =>
                    {
                        if (bench.IsRunning || runner.IsBusy) return "busy";
                        var row = bench.Rows.FirstOrDefault(r => r.Benchmark.Definition.Id.Value == id);
                        return row is null ? "unknown" : !row.IsAvailable ? "unavailable" : Json(new { name = row.Name, seconds = row.DurationText });
                    }).ConfigureAwait(false);
                    if (plan == "busy") return Json(new { error = "a test or a benchmark is already running; nothing was started" });
                    if (plan is "unknown" or "unavailable") return Json(new { error = "this computer can not run that benchmark" });
                    using var info = JsonDocument.Parse(plan);
                    if (!await ask("benchmark", [(info.RootElement.GetProperty("name").GetString()!, info.RootElement.GetProperty("seconds").GetString()!)], ct).ConfigureAwait(false))
                        return Json(new { started = false, reason = "the user declined; nothing was run" });

                    return await running(new("benchmark", () => bench.Rows.FirstOrDefault(r => r.IsActive) is { } r ? (r.Name, r.PercentComplete) : null), async () =>
                    {
                        using var stop = ct.Register(() => _window.Dispatcher.BeginInvoke(() => { if (bench.CancelCommand.CanExecute(null)) bench.CancelCommand.Execute(null); }));
                        string ran = await ui(async () =>
                        {
                            var row = bench.Rows.First(r => r.Benchmark.Definition.Id.Value == id);
                            compared.Remove(id);   // what is found under it afterwards is this run's
                            if (!bench.RunCommand.CanExecute(row)) return "refused";
                            await bench.RunCommand.ExecuteAsync(row);
                            return "";
                        }).ConfigureAwait(false);
                        if (ran.Length > 0) return Json(new { started = false, reason = "the benchmark could not start" });
                        // The comparison with the best kept result is filed when the run's own event reaches the page; give it a few seconds.
                        for (int i = 0; i < 40; i++)
                        {
                            string result = await OnUi(() =>
                            {
                                var row = bench.Rows.First(r => r.Benchmark.Definition.Id.Value == id);
                                if (compared.GetValueOrDefault(id) is not { } c) return row.IsActive || i < 39 ? "" : Json(new { started = true, completed = false, status = row.StatusText, note = "no result was recorded; do not report a number" });
                                return Json(new
                                {
                                    started = true, completed = true, benchmark = row.Name, value = Math.Round(c.Current.Value, 2), unit = c.Current.Unit,
                                    previousBest = c.Previous is { } p ? new { value = Math.Round(p.Value, 2), at = p.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm") } : null,
                                    changePercent = c.ChangePercent is { } ch ? Math.Round(ch, 1) : (double?)null, newRecord = c.Saved,
                                    note = c.Previous is null ? "the first recorded result of this computer; there is nothing to compare with" : null,
                                });
                            }).ConfigureAwait(false);
                            if (result.Length > 0) return result;
                            await Task.Delay(250, CancellationToken.None).ConfigureAwait(false);
                        }
                        return Json(new { started = true, completed = false, note = "no result was recorded; do not report a number" });
                    }).ConfigureAwait(false);
                }),
        ];
    }

    private static readonly JsonSerializerOptions Json_ = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
}
