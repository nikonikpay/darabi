using System.Globalization; using System.IO; using System.Text.Json; using System.Text.Json.Nodes;
using Mazesta.Core.Ai; using Mazesta.Core.Hardware; using Mazesta.Core.Software; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Ai; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Monitoring; using Mazesta.Reporting;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    /// <summary>The tests the assistant may start for each area it names: short ones at their default lengths. Nothing that changes Windows is in it.
    /// The graphics card's are run with the model unloaded (it would hold the card's memory and share its time).</summary>
    private static readonly IReadOnlyDictionary<string, string[]> AssistantTestAreas = new Dictionary<string, string[]>
    {
        ["cpu"] = ["cpu.matrix", "cpu.integer", "cpu.fft"], ["memory"] = ["memory.pattern"],
        ["storage"] = ["storage.smart", "storage.sequential"], ["network"] = ["network.latency"], ["gpu"] = ["gpu.render", "gpu.steady", "gpu.vram"],
    };

    /// <summary>The benchmarks the assistant may start; the graphics one with the model unloaded, like the GPU tests.</summary>
    private static readonly IReadOnlyDictionary<string, string> AssistantBenchmarks = new Dictionary<string, string>
    {
        ["cpu_single"] = "bench.cpu.single", ["cpu_multi"] = "bench.cpu.multi", ["memory"] = "bench.memory", ["storage"] = "bench.storage", ["gpu"] = "bench.gpu.d3d",
    };

    /// <summary>The pages the assistant may open (the page's own ids, from <see cref="AppGuide"/>) and the controls it may point at on them.</summary>
    private static readonly string[] AssistantPageIds = [.. AppGuide.Places.Where(p => p.Target is null).Select(p => p.Page)];
    private static readonly string[] AssistantTargets = [.. AppGuide.Places.Where(p => p.Target is not null).Select(p => p.Page + "/" + p.Target)];

    /// <summary>
    /// What the assistant may call. The first four only read. <c>open_page</c> and <c>set_overlay</c> do what the user could do with one click and can
    /// undo with one. <c>run_tests</c> and <c>run_benchmark</c> start a real run, but only after the user said yes on the page (<paramref name="ask"/>),
    /// for the items left ticked; what they return is what the engine reported, outcome names unchanged, so a test that did not run is never told as
    /// a pass. A value that is not available is left out, never written as 0. The page shows which tools ran beside the answer, and a run's outcomes
    /// from its result.
    /// </summary>
    private IReadOnlyList<AiTool> AssistantTools(Func<SensorSnapshot?> latest, Func<string, IReadOnlyList<(string, string)>, CancellationToken, Task<bool[]?>> ask,
        Func<Activity, Func<Task<string>>, Task<string>> running, Func<Func<Task<string>>, Task<string>> ui, Func<Func<Task<string>>, CancellationToken, Task<string>> withoutModel,
        Action<string, string?> navigate, Func<Task<SoftMachine>> softMachine, Action<string> offerFile)
    {
        var engine = _sp.GetRequiredService<PollingEngine>(); var inventory = _sp.GetRequiredService<InventoryCache>(); var runner = _sp.GetRequiredService<BenchmarkRunner>();
        static string Json(object o) => JsonSerializer.Serialize(o, Json_);
        static string? Text(JsonElement a, string name) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static int Int(JsonElement a, string name, int fallback, int max) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? Math.Clamp(n, 1, max) : fallback;
        static string[] Texts(JsonElement a, string name) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)] : [];
        static string Cut(string? s, int n) => s is null ? "" : s.Length <= n ? s : s[..n] + "…";
        static int Index(JsonElement a) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty("index", out var v) && v.TryGetInt32(out var n) ? n : 0;
        Task<string> OnUi(Func<string> work) => ui(() => Task.FromResult(work()));

        return
        [
            new("get_machine_summary", "This computer's specification as the app read it: processor (cores, threads), graphics cards with their memory (VRAM), " +
                "RAM size and modules, operating system, motherboard, BIOS and the drives with size and health. Optional part: cpu, gpu, vram, ram, storage, board, os.",
                """{"type":"object","properties":{"part":{"type":"string","enum":["cpu","gpu","vram","ram","storage","board","os","all"]}}}""",
                async (a, _) =>
                {
                    var inv = await inventory.GetAsync().ConfigureAwait(false); var pc = await softMachine().ConfigureAwait(false);
                    string part = Text(a, "part") ?? "all"; bool All(params string[] p) => part == "all" || p.Contains(part);
                    static double? Gb(long? b) => b is { } x ? Math.Round(x / 1073741824.0, 1) : null;
                    return Json(new
                    {
                        cpu = All("cpu") ? new { name = inv.Cpu?.Name?.Trim(), cores = inv.Cpu?.PhysicalCores, threads = inv.Cpu?.LogicalProcessors, maxClockMhz = inv.Cpu?.MaxClockMhz, socket = inv.Cpu?.Socket } : null,
                        gpus = All("gpu", "vram") ? inv.Gpus.Select(g => new
                        {
                            name = g.Name?.Trim(), driver = g.DriverVersion,
                            // The card's own memory comes from its sensors (WMI's figure stops at 4 GB); it is the one the AI page and the programs check use.
                            vramGb = pc.GpuName is not null && g.Name is not null && Diagnostics.Benchmarks.BenchmarkPeers.PartName(g.Name) == Diagnostics.Benchmarks.BenchmarkPeers.PartName(pc.GpuName) ? Gb(pc.VramBytes) : null,
                        }) : null,
                        ramGb = All("ram") ? Gb(inv.TotalPhysicalMemoryBytes) : null,
                        ramModules = part == "ram" ? inv.MemoryModules.Select(m => new { slot = m.Slot, gb = Gb(m.CapacityBytes), speedMts = m.ConfiguredSpeedMts ?? m.SpeedMts, maker = m.Manufacturer?.Trim(), part = m.PartNumber?.Trim() }) : null,
                        os = All("os") ? $"{inv.Os?.Caption} {inv.Os?.Version} ({inv.Os?.Architecture})".Trim() : null,
                        board = All("board") ? (inv.Motherboard is { } m ? $"{m.Manufacturer} {m.Product}".Trim() : null) : null, bios = All("board") ? inv.Bios?.Version : null,
                        drives = All("storage") ? inv.Storage.Select(d => new { name = d.FriendlyName, sizeGb = Gb(d.SizeBytes), type = d.MediaType, health = SystemInfoViewModel.DriveHealth(d.HealthStatus, d.WearPercent) }) : null,
                    });
                }),
            new("find_in_app", "Where in this app something is done: the page and the control for it, by the names the app shows, and what is there. For questions of how or where.",
                """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}""",
                (a, _) =>
                {
                    string q = AppGuide.Normalize(Text(a, "query") ?? "");
                    var place = AppGuide.FindPlace(q);
                    var page = place is null ? null : AppGuide.Page(place.Page);
                    if (place is null) return Task.FromResult(Json(new { found = false, pages = AppGuide.PageList(Loc.Get) }));
                    return Task.FromResult(Json(new { found = true, page = place.Page, pageName = Loc.Get(page?.TitleKey ?? place.TitleKey), control = place.Target is null ? null : Loc.Get(place.TitleKey), what = place.What }));
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
                        // A drive's "Critical" or "Warning Temperature" and a DIMM's sensor resolution are limits and settings, not readings.
                        sensors = n.Sensors.Where(s => values.ContainsKey(s.Id) && (kind is null || s.Kind.ToString() == kind) && s.Kind.ToString() is "Temperature" or "Load" or "Clock" or "Power" or "Fan"
                                && !NotAReading.IsMatch(s.Name))
                            .Take(kind is null ? 12 : 40).Select(s => new { name = s.Name, kind = s.Kind.ToString(), value = Math.Round(values[s.Id], 1), unit = s.Unit.ToString() }),
                    }).Where(d => d.sensors.Any()).Take(8);
                    // A reading older than a few polls is not "now" (the polling was paused or stuck): it is said with its age.
                    double age = Math.Round((DateTimeOffset.UtcNow - snap.Timestamp).TotalSeconds);
                    return Task.FromResult(Json(new { secondsAgo = age, stale = age > Math.Max(StaleSeconds, 3 * engine.FastInterval.TotalSeconds) ? true : (bool?)null, devices }));
                }),
            new("list_reports", "The saved test and benchmark reports, newest first: index (0 is the newest), when, kind, verdict and how many tests passed, failed or did not run. Optional limit (default 5, at most 10).",
                """{"type":"object","properties":{"limit":{"type":"integer"}}}""",
                (a, _) =>
                {
                    var list = new ReportStore(_paths.ReportsDir).List();
                    return Task.FromResult(Json(new
                    {
                        total = list.Count,
                        reports = list.Take(Int(a, "limit", 5, 10)).Select((r, i) => new { index = i, createdAt = r.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), kind = r.Kind.ToString(), verdict = r.Verdict?.ToString(), r.Counts, benchmarks = r.Benchmarks }),
                    }));
                }),
            new("get_report", "One saved report in full: its tests and outcomes, its benchmarks' numbers, the highest temperatures while it ran (per part) and the diagnosis findings. " +
                "Optional index (0 = newest, as list_reports numbers them). Use it to summarise a report or to answer what the highest temperature was (kind says the part: cpu, gpu, " +
                "motherboard, storage, memory). Say only what it holds: " +
                "do not call temperatures safe or high, nor the computer good, unless a finding says so.",
                """{"type":"object","properties":{"index":{"type":"integer"}}}""",
                (a, _) =>
                {
                    var store = new ReportStore(_paths.ReportsDir); var list = store.List();
                    int i = Index(a);
                    if (list.Count == 0) return Task.FromResult(Json(new { error = "there are no saved reports yet; run tests or benchmarks to make one" }));
                    if (i < 0 || i >= list.Count) return Task.FromResult(Json(new { error = $"there are {list.Count} reports; index must be 0 to {list.Count - 1}" }));
                    if (store.Load(list[i]) is not { } r) return Task.FromResult(Json(new { error = "the report could not be read" }));
                    return Task.FromResult(Json(new
                    {
                        index = i, total = list.Count, createdAt = r.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), kind = r.Kind.ToString(),
                        verdict = r.Verdict?.ToString() ?? "none: a benchmark report measures speed, nothing in it passed or failed",
                        minutes = Math.Round(r.DurationSeconds / 60, 1), computer = new { cpu = r.Machine.Cpu?.Name?.Trim(), gpu = r.Machine.Gpus.FirstOrDefault()?.Name?.Trim() },
                        tests = r.Tests.Select(t => new { name = t.Name, outcome = t.Outcome.ToString(), errors = t.ErrorCount > 0 ? t.ErrorCount : (long?)null, detail = Cut(t.Detail, 100) is { Length: > 0 } d ? d : null }),
                        benchmarks = (r.Benchmarks ?? []).Select(b => new { name = b.Name, results = b.Metrics.Take(3).Select(m => $"{Math.Round(m.Value, 2)} {m.Unit} ({m.Name})") }),
                        // The hottest reading of each part while the report ran, from every recorded sample.
                        highestTemperatures = r.Sensors.Where(x => x.Kind == "Temperature" && x.Samples > 0).GroupBy(x => x.Hardware)
                            .Select(g => g.OrderByDescending(x => x.Max).First()).OrderByDescending(x => x.Max).Take(10)
                            .Select(x => new { part = x.Hardware, kind = x.Id.Split('/')[0], sensor = x.Name, maxC = Math.Round(x.Max, 1) }),
                        // The problems first, so a short list never drops one behind the notes; how many were left out is said.
                        findings = (r.Findings ?? []).OrderBy(f => f.Level switch { "Problem" => 0, "Attention" => 1, _ => 2 }).Take(8).Select(f => new { level = f.Level, title = f.Title }),
                        findingsOmitted = (r.Findings?.Count ?? 0) > 8 ? r.Findings!.Count - 8 : (int?)null,
                    }));
                }),
            new("export_report", "Makes a file of a saved report and offers it in the chat with an Open button: pdf (the full report), html (the full report as a web page) or summary " +
                "(a one-page PDF summary). Optional index (0 = newest). Returns the file's name; the user opens it from the chat.",
                """{"type":"object","properties":{"format":{"type":"string","enum":["pdf","html","summary"]},"index":{"type":"integer"}},"required":["format"]}""",
                async (a, _) =>
                {
                    var service = _sp.GetRequiredService<Desktop.Services.ReportService>(); var list = service.Store.List();
                    int i = Index(a);
                    if (list.Count == 0) return Json(new { error = "there are no saved reports yet" });
                    if (i < 0 || i >= list.Count) return Json(new { error = $"index must be 0 to {list.Count - 1}" });
                    var stored = list[i]; string format = Text(a, "format") ?? "pdf";
                    string path = await ui(async () =>
                    {
                        switch (format)
                        {
                            case "html": return stored.HtmlPath;
                            case "summary":
                                {
                                    string html = service.CreateSummary(stored), pdf = Path.ChangeExtension(html, ".pdf");
                                    await Desktop.Services.PdfExporter.ExportAsync(html, pdf, Desktop.Localization.Loc.Get("Reports_SummaryBusy"), _window, service.BrowserDataDir, a5: true);
                                    return pdf;
                                }
                            default:
                                if (!File.Exists(stored.PdfPath)) await Desktop.Services.PdfExporter.ExportAsync(stored.HtmlPath, stored.PdfPath, Desktop.Localization.Loc.Get("Reports_PdfBusy"), _window, service.BrowserDataDir);
                                return stored.PdfPath;
                        }
                    }).ConfigureAwait(false);
                    if (!File.Exists(path)) return Json(new { error = "the file was not made" });
                    offerFile(path);
                    return Json(new { made = true, format, file = Path.GetFileName(path), path, report = stored.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") });
                }),
            new("check_software", "Whether this computer runs a professional program (rendering, architecture, civil, animation, video editing, graphics) and at which of its " +
                "publisher's tiers (Minimum, Recommended, HighEnd), what work that tier suits, and what is missing for the next tier. Give app (its name or id) for one " +
                "program, or category (Visualization, Rendering, Architecture, Civil, Animation, Video, Graphics) for a group, or neither for all.",
                """{"type":"object","properties":{"app":{"type":"string"},"category":{"type":"string","enum":["Visualization","Rendering","Architecture","Civil","Animation","Video","Graphics"]}}}""",
                async (a, _) =>
                {
                    var pc = await softMachine().ConfigureAwait(false);
                    string? name = Text(a, "app"); string? cat = Text(a, "category");
                    var app = name is null ? null : SoftwareCatalog.Find(name.ToLowerInvariant()) ?? AppGuide.FindApp(AppGuide.Normalize(name));
                    if (name is not null && app is null) return Json(new { error = "that program is not in the app's list", known = SoftwareCatalog.Apps.Select(x => x.Name) });
                    var apps = app is not null ? [app] : SoftwareCatalog.Apps.Where(x => cat is null || x.Category.ToString() == cat).ToList();
                    return Json(new
                    {
                        computer = new { cpu = pc.CpuName, cores = pc.Cores, ramGb = pc.RamBytes is { } r ? Math.Round(r / 1073741824.0, 1) : (double?)null, gpu = pc.GpuName, vramGb = pc.VramBytes is { } v ? Math.Round(v / 1073741824.0, 1) : (double?)null },
                        programs = apps.Select(x => SoftwareRow(x, pc, detail: app is not null)),
                        note = "levels compare memory, graphics memory, cores and graphics card features with the publisher's tiers; the card's and processor's speed against the publisher's example parts is not measured",
                    });
                }),
            new("get_benchmark_history", "This computer's newest benchmark results, newest first, to see whether it got slower or faster. Optional benchmark (part of its id) and limit (default 6, at most 15).",
                """{"type":"object","properties":{"benchmark":{"type":"string"},"limit":{"type":"integer"}}}""",
                (a, _) =>
                {
                    if (_benchRunLog is null || _benchSystemHash?.Invoke() is not { } hash) return Task.FromResult(Json(new { error = "the benchmark history is not available yet" }));
                    string? name = Text(a, "benchmark");
                    var runs = _benchRunLog.Recent(hash, 300).Where(r => name is null || r.Benchmark.Contains(name, StringComparison.OrdinalIgnoreCase)).Take(Int(a, "limit", 6, 15))
                        .Select(r => new { at = r.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), benchmark = r.Benchmark, settings = r.Settings, value = Math.Round(r.Value, 2), unit = r.Unit, overclocked = r.Overclocked,
                            // A run of an earlier workload measured something else: it is listed, but is not compared with the current one.
                            earlierWorkload = Diagnostics.Benchmarks.BenchmarkRecords.Headline(r.Benchmark)?.Version is { } v && r.Version != v ? true : (bool?)null });
                    return Task.FromResult(Json(new { runs }));
                }),
            new("open_page", "Opens a page of the app on the screen, beside the chat, and can point at one control on it (target). Use the page ids of the list in your instructions.",
                "{\"type\":\"object\",\"properties\":{\"page\":{\"type\":\"string\",\"enum\":[" + string.Join(",", AssistantPageIds.Select(x => $"\"{x}\"")) + "]},\"target\":{\"type\":\"string\",\"description\":\"optional control: " + string.Join(", ", AssistantTargets) + "\"}},\"required\":[\"page\"]}",
                (a, _) =>
                {
                    if (Text(a, "page") is not { } page || !AssistantPageIds.Contains(page)) return Task.FromResult(Json(new { error = "unknown page" }));
                    string? target = Text(a, "target") is { } x && AssistantTargets.Contains(page + "/" + x) ? x : null;
                    navigate(page, target);
                    var place = AppGuide.Places.FirstOrDefault(p => p.Page == page && p.Target == target) ?? AppGuide.Page(page);
                    return Task.FromResult(Json(new { opened = page, name = place is null ? null : Loc.Get(AppGuide.Page(page)!.TitleKey), control = target is null || place is null ? null : Loc.Get(place.TitleKey) }));
                }),
            new("set_overlay", "Shows or hides the on-screen overlay (the small always-on-top readout of temperatures, loads and frame rate over games). Returns whether it is shown now.",
                """{"type":"object","properties":{"on":{"type":"boolean"}},"required":["on"]}""",
                (a, _) => OnUi(() =>
                {
                    if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("on", out var v) || v.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return Json(new { error = "on must be true or false" });
                    var overlay = _sp.GetRequiredService<Desktop.Services.OverlayService>(); overlay.SetVisible(v.GetBoolean());
                    return Json(new { shown = overlay.IsVisible });
                })),
            new("test_dns", "Times every DNS resolver the app knows (and the one in use) on this connection, as DNS Jumper does, and names the fastest that answered " +
                "every lookup. It changes nothing: the user switches on the Windows tools page (it is opened for them).", """{"type":"object","properties":{}}""",
                async (_, ct) =>
                {
                    var current = Diagnostics.Windows.DnsChoice.Current().SelectMany(a => a.Servers).Distinct().Where(x => Diagnostics.Windows.DnsChoice.Identify([x]) == "auto").Take(1).Select(x => ("current", x));
                    var scores = await Diagnostics.Windows.DnsBench.RunAsync(Diagnostics.Windows.DnsChoice.Providers.Select(p => (p.Key, p.Value[0])).Concat(current), ct).ConfigureAwait(false);
                    var best = Diagnostics.Windows.DnsBench.Best(scores);
                    string Name(string id) => id == "current" ? Loc.Get("Dns_Modem") : Loc.Get("Dns_" + id);
                    navigate("tools", "dns");
                    return Json(new
                    {
                        fastest = best is null ? null : Name(best.Provider), fastestMs = best?.MedianMs is { } ms ? Math.Round(ms, 1) : (double?)null,
                        inUse = Diagnostics.Windows.DnsChoice.Current().Select(a => Name(Diagnostics.Windows.DnsChoice.Identify(a.Servers) is "auto" ? "current" : Diagnostics.Windows.DnsChoice.Identify(a.Servers))).Distinct(),
                        results = scores.Where(x => x.MedianMs is not null).OrderBy(x => x.Reliable ? 0 : 1).ThenBy(x => x.MedianMs).Take(6)
                            .Select(x => new { name = Name(x.Provider), ms = Math.Round(x.MedianMs!.Value, 1), answered = $"{x.Answered}/{x.Asked}" }),
                        note = "nothing was changed; the Windows tools page is open at the DNS box, where «فعال کن» switches to one",
                    });
                }),
            new("run_tests", "Runs real hardware tests, after the user confirmed on the page, and returns each test's outcome. It takes minutes. Areas: cpu, memory (RAM), storage, network, gpu (graphics card). " +
                "Name only the areas the user asked for. An outcome other than Passed (Failed, Cancelled, Unsupported, NotRun, Error, Inconclusive) is never to be told as a pass.",
                """{"type":"object","properties":{"areas":{"type":"array","items":{"type":"string","enum":["cpu","memory","storage","network","gpu"]}}},"required":["areas"]}""",
                async (a, ct) =>
                {
                    if (_testVm is not { } tests) return Json(new { error = "the tests are not available yet" });
                    var ids = Texts(a, "areas").Distinct().SelectMany(x => AssistantTestAreas.GetValueOrDefault(x) ?? []).ToHashSet();
                    if (ids.Count == 0) return Json(new { error = "name at least one area: cpu, memory, storage, network or gpu" });
                    // The request is fixed here, before the user is asked: the rows this machine can run, each once, at its default length, with the
                    // options (graphics card, drive) the page has now. The run uses exactly this, whatever the page is changed to meanwhile.
                    List<ChatTest>? plan = null;
                    await OnUi(() => { plan = tests.IsRunning || runner.IsBusy ? null : tests.Rows.Where(r => ids.Contains(r.Definition.Id.Value) && r.IsAvailable)
                        .Select(r => new ChatTest(r.Definition.Id.Value, r.Name, r.Definition.DefaultDurationSeconds, r.Options.Select(o => (o.Option.Key, o.Value)).ToList(),
                            string.Join("، ", r.Options.Where(o => o.Value.Length > 0).Select(o => o.Label + ": " + (o.IsChoice ? o.SelectedChoice?.Label : o.Value)))))
                        .ToList(); return ""; }).ConfigureAwait(false);
                    if (plan is null) return Json(new { error = "a test or a benchmark is already running; nothing was started" });
                    if (plan.Count == 0) return Json(new { error = "this computer can not run those tests" });
                    var items = plan.Select(r => (r.Shown.Length > 0 ? $"{r.Name} ({r.Shown})" : r.Name, r.Seconds.ToString(CultureInfo.InvariantCulture))).ToList();
                    if (await ask("tests", items, ct).ConfigureAwait(false) is not { } kept) return Json(new { started = false, reason = "the user declined; nothing was run" });

                    var chosen = plan.Where((_, i) => kept[i]).ToDictionary(r => r.Id);
                    return await running(new("tests", () => tests.CurrentRow is { } cur ? (cur.Name, Math.Round((tests.CurrentIndex + cur.PercentComplete) / Math.Max(1, tests.RunQueue.Count) * 100)) : null), () => withoutModel(Run, ct));
                    async Task<string> Run()
                    {
                        using var stop = ct.Register(() => _window.Dispatcher.BeginInvoke(() => { if (tests.CancelCommand.CanExecute(null)) tests.CancelCommand.Execute(null); }));
                        // The page's own settings are put back afterwards, however the run ends; the assistant only borrows the Tests page's queue.
                        List<RowState>? before = null;
                        try
                        {
                            string started = await ui(async () =>
                            {
                                before = [.. tests.Rows.Select(RowState.Of)];
                                foreach (var r in tests.Rows)
                                {
                                    r.IsSelected = chosen.TryGetValue(r.Definition.Id.Value, out var want);
                                    if (want is null) continue;
                                    r.DurationText = want.Seconds.ToString(CultureInfo.InvariantCulture); r.Repeat = Diagnostics.RepeatMode.Once; r.RepeatCountText = "1";
                                    foreach ((string key, string value) in want.Options) if (r.Options.FirstOrDefault(o => o.Option.Key == key) is { } o) RowState.Set(o, value);
                                }
                                if (!tests.StartCommand.CanExecute(null)) return "refused";
                                await tests.StartCommand.ExecuteAsync(null);
                                return tests.BlockedMessage is { } why ? why : "";
                            }).ConfigureAwait(false);
                            if (started.Length > 0) return Json(new { started = false, reason = started == "refused" ? "the test queue could not start" : started });
                            return await OnUi(() => Json(new
                            {
                                started = true, results = tests.Rows.Where(r => chosen.ContainsKey(r.Definition.Id.Value))
                                    .Select(r => new { id = r.Definition.Id.Value, name = r.Name, outcome = r.Outcome.ToString(), errors = r.ErrorCount > 0 ? r.ErrorCount : (long?)null, detail = r.HasDetail ? Cut(r.Detail, 240) : null }),
                            })).ConfigureAwait(false);
                        }
                        finally
                        {
                            if (before is { } saved)
                                await ui(() =>
                                {
                                    foreach (var s in saved) if (tests.Rows.FirstOrDefault(r => r.Definition.Id.Value == s.Id) is { } r) s.Restore(r);
                                    return Task.FromResult("");
                                }).ConfigureAwait(false);
                        }
                    }
                }),
            new("run_benchmark", "Runs one real benchmark, after the user confirmed on the page, and returns its number, the best earlier result of this computer and the change against it " +
                "(positive change is better, negative is slower than the best kept). It takes about a minute. Benchmarks: cpu_single, cpu_multi, memory, storage, gpu. For a trend over time use get_benchmark_history.",
                """{"type":"object","properties":{"benchmark":{"type":"string","enum":["cpu_single","cpu_multi","memory","storage","gpu"]}},"required":["benchmark"]}""",
                async (a, ct) =>
                {
                    if (_benchVm is not { } bench || _benchCompared is not { } compared) return Json(new { error = "the benchmarks are not available yet" });
                    if (Text(a, "benchmark") is not { } key || !AssistantBenchmarks.TryGetValue(key, out string? id)) return Json(new { error = "benchmark must be cpu_single, cpu_multi, memory, storage or gpu" });
                    var plan = await OnUi(() =>
                    {
                        if (bench.IsRunning || runner.IsBusy) return "busy";
                        var row = bench.Rows.FirstOrDefault(r => r.Benchmark.Definition.Id.Value == id);
                        return row is null ? "unknown" : !row.IsAvailable ? "unavailable" : Json(new { name = row.Name, seconds = row.DurationText });
                    }).ConfigureAwait(false);
                    if (plan == "busy") return Json(new { error = "a test or a benchmark is already running; nothing was started" });
                    if (plan is "unknown" or "unavailable") return Json(new { error = "this computer can not run that benchmark" });
                    using var info = JsonDocument.Parse(plan);
                    if (await ask("benchmark", [(info.RootElement.GetProperty("name").GetString()!, info.RootElement.GetProperty("seconds").GetString()!)], ct).ConfigureAwait(false) is null)
                        return Json(new { started = false, reason = "the user declined; nothing was run" });

                    return await running(new("benchmark", () => bench.Rows.FirstOrDefault(r => r.IsActive) is { } r ? (r.Name, r.PercentComplete) : null), () => withoutModel(Run, ct));
                    async Task<string> Run()
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
                    }
                }),
        ];
    }

    /// <summary>One program's verdict as the assistant and the page read it, in the user's language: its level, what that level suits, what the
    /// next level lacks. With <paramref name="detail"/> the tiers' own figures too.</summary>
    private static object SoftwareRow(SoftApp x, SoftMachine pc, bool detail)
    {
        var v = SoftwareCatalog.Judge(x, pc);
        var tier = v.Level is { } l ? x.Tiers.First(t => t.Kind == l) : null;
        return new
        {
            id = x.Id, name = x.Name, level = SoftwareCatalog.LevelName(x, v), levelName = Desktop.Localization.Loc.Get("Soft_Level_" + SoftwareCatalog.LevelName(x, v)),
            suits = tier is null ? null : Desktop.Localization.Loc.Get(x.Tiers.Count == 1 ? "Soft_Scale_Single" : tier.ScaleKey ?? "Soft_Scale_" + x.Category + "_" + tier.Kind),
            missingForNext = v.Missing.Select(m => ShortText(m)),
            notChecked = (v.Unchecked ?? []).Select(x => Desktop.Localization.Loc.Get("Soft_Unchecked_" + x)),
            nextLevel = v.Next is { } n ? Desktop.Localization.Loc.Get("Soft_Level_" + n) : null,
            tiers = detail ? x.Tiers.Select(t => new { level = Desktop.Localization.Loc.Get("Soft_Level_" + t.Kind), ramGb = t.RamGb, vramGb = t.VramGb, cores = t.Cores, gpu = t.Gpu, cpu = t.Cpu }) : null,
            source = detail ? x.Source : null, note = detail && x.NoteKey is { } nk ? Desktop.Localization.Loc.Get(nk) : null,
        };
    }

    /// <summary>What a tier lacks, in words: "RAM: 32 GB needed, 16 here".</summary>
    private static string ShortText(SoftShort m) => m.Need is { } need
        ? Desktop.Localization.Loc.Format("Soft_Short_" + m.What, need.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), m.Have?.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) ?? "—")
        : Desktop.Localization.Loc.Get("Soft_Short_" + m.What);

    /// <summary>A test as the user confirmed it from the chat: once, at this length, with these options.</summary>
    private sealed record ChatTest(string Id, string Name, int Seconds, IReadOnlyList<(string Key, string Value)> Options, string Shown);

    /// <summary>What the Tests page had on a row before the assistant borrowed it, put back afterwards.</summary>
    private sealed record RowState(string Id, bool On, string Seconds, Diagnostics.RepeatMode Repeat, string Count, IReadOnlyList<(string Key, string Value)> Options)
    {
        public static RowState Of(Desktop.ViewModels.TestQueueRowViewModel r)
            => new(r.Definition.Id.Value, r.IsSelected, r.DurationText, r.Repeat, r.RepeatCountText, [.. r.Options.Select(o => (o.Option.Key, o.IsChoice ? o.Value : o.Text))]);
        public void Restore(Desktop.ViewModels.TestQueueRowViewModel r)
        {
            r.DurationText = Seconds; r.Repeat = Repeat; r.RepeatCountText = Count;
            foreach (var (key, value) in Options) if (r.Options.FirstOrDefault(o => o.Option.Key == key) is { } o) Set(o, value);
            r.IsSelected = On;
        }
        public static void Set(Desktop.ViewModels.TestOptionViewModel o, string value)
        {
            if (o.IsChoice) { if (o.Choices.FirstOrDefault(c => c.Value == value) is { } c) o.SelectedChoice = c; } else o.Text = value;
        }
    }

    private const double StaleSeconds = 10;
    private static readonly System.Text.RegularExpressions.Regex NotAReading = new(@"Resolution|Critical|Warning|Limit|Threshold|Low|High", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions Json_ = new() {
        // Persian goes to the model as letters: escaped (پ…) the model can not read it and makes words up in its place.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
}
