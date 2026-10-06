using System.IO; using System.Net.Http;
using Mazesta.Core.Ai; using Mazesta.Core.Hardware; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization;
using Mazesta.Diagnostics; using Mazesta.Diagnostics.Ai; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Memory; using Mazesta.Monitoring;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>Downloads of many gigabytes: no overall timeout (a slow line is not an error), only a stalled connect; cancel is the user's.</summary>
    private static readonly HttpClient AiHttp = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(20) }) { Timeout = Timeout.InfiniteTimeSpan, DefaultRequestHeaders = { { "User-Agent", "MazestaTest/1.0 (+https://www.dfmrendering.com)" } } };

    /// <summary>A download in progress or the last one's failure, per catalog id ("runtime" for llama.cpp).</summary>
    private sealed class AiTransfer { public DownloadProgress Progress; public string? Error; public CancellationTokenSource? Cts; }

    /// <summary>
    /// The AI page: which of the catalog's models this machine can run (estimated from the model's own figures and this machine's memory, before
    /// any download), the downloads the user asks for, and the llama.cpp benchmark of a downloaded model on the GPU or the CPU. One download at
    /// a time; a benchmark runs through the shared benchmark runner, so it never overlaps a test, another benchmark or the GPU tuning.
    /// </summary>
    private void RegisterAi()
    {
        var files = new AiFiles(_paths.DataRoot, AiHttp); var results = new AiResults(files.Root); var bench = new LlamaBenchmark(files);
        var runner = _sp.GetRequiredService<BenchmarkRunner>(); var engine = _sp.GetRequiredService<PollingEngine>();
        var inventory = _sp.GetRequiredService<InventoryCache>(); var details = _sp.GetRequiredService<HardwareDetailsCache>();
        var transfers = new Dictionary<string, AiTransfer>(); string? active = null;
        (string Model, string Device, double Percent)? running = null; string? runError = null;
        double? bandwidth = null; string? bandwidthOf = null;

        // The GPU a model would use: the one with the most memory of its own (a discrete card over the integrated one).
        (string Name, long Vram)? Gpu() => engine.Hardware.Where(n => n.Kind == HardwareKind.Gpu && n.ParentId is null)
            .Select(n => (n.Name, Vram: (long)(VramBytes(engine, n.Name) ?? 0))).Where(g => g.Vram > 0).OrderByDescending(g => g.Vram).Cast<(string, long)?>().FirstOrDefault();
        async Task Bandwidth(string gpu)
        {
            if (bandwidthOf == gpu) return;
            var inv = await inventory.GetAsync().ConfigureAwait(true); var d = await details.GetAsync().ConfigureAwait(true);
            var info = inv.Gpus.FirstOrDefault(g => string.Equals(BenchmarkPeers.PartName(g.Name), BenchmarkPeers.PartName(gpu), StringComparison.OrdinalIgnoreCase));
            var gd = info is null ? null : d.Gpus.FirstOrDefault(x => x.PnpDeviceId is not null && string.Equals(x.PnpDeviceId, info.PnpDeviceId, StringComparison.OrdinalIgnoreCase));
            bandwidth = AiFitter.GpuBandwidth(gd?.BusWidthBits, gd?.MaxMemoryClockMhz); bandwidthOf = gpu;
            PushSoon("ai", State);
        }
        AiMachine Machine()
        {
            var ram = new Win32MemoryProbe().Read(); var gpu = Gpu();
            if (gpu is { } g && bandwidthOf != g.Name) _ = Bandwidth(g.Name);
            return new(gpu?.Name, gpu?.Vram, gpu is { } x && bandwidthOf == x.Name ? bandwidth : null, ram.TotalBytes, ram.AvailableBytes);
        }
        static string Size(double bytes) => bytes < AiFitter.Gib ? Units.FormatMeasured(bytes / 1048576, "MB") : Units.FormatMeasured(bytes / AiFitter.Gib, "GB");
        static string Rate(double? v) => v is { } x ? Units.FormatMeasured(x, "tok/s") : "";
        object? Transfer(string id) => transfers.GetValueOrDefault(id) is { } t ? new
        {
            active = t.Cts is not null, error = t.Error, percent = t.Progress.Total > 0 ? t.Progress.Done * 100.0 / t.Progress.Total : 0,
            done = Size(t.Progress.Done), speed = t.Progress.BytesPerSecond > 0 ? Units.FormatMeasured(t.Progress.BytesPerSecond / 1048576, "MB/s") : null,
        } : null;
        object? Measured(AiModel m, string device) => results.Get(m.Id, device) is { } r ? new
        {
            prompt = r.PromptTokensPerSecond is { } pp ? Rate(pp) : null, gen = Rate(r.GenerationTokensPerSecond), genRaw = r.GenerationTokensPerSecond,
            at = r.At.ToLocalTime().ToString("yyyy/MM/dd HH:mm", Loc.Culture), detail = r.Detail,
        } : null;

        object State()
        {
            var pc = Machine(); var recommended = AiFitter.Recommend(AiCatalog.Models, pc);
            var cpuOnly = pc with { VramBytes = null };
            return new
            {
                machine = new
                {
                    gpu = pc.GpuName, vram = pc.VramBytes is { } v ? Size(v) : null, bandwidth = pc.GpuBandwidthBytesPerSecond is { } b ? Units.FormatMeasured(b / 1e9, "GB/s") : null,
                    ram = Size(pc.RamTotalBytes), ramFree = Size(pc.RamAvailableBytes),
                },
                runtime = new { ready = files.HasRuntime, build = AiCatalog.Runtime.Build, size = Size(AiCatalog.Runtime.Bytes), transfer = Transfer("runtime") },
                busy = runner.IsBusy, downloading = active, error = runError,
                running = running is { } r ? new { model = r.Model, device = r.Device, percent = r.Percent } : null,
                recommended = recommended?.Id,
                models = AiCatalog.Models.Select(m =>
                {
                    var fit = AiFitter.Fit(m, pc); var cpu = AiFitter.Fit(m, cpuOnly);
                    return new
                    {
                        id = m.Id, name = m.Name, @params = m.Params, quant = m.Quant, size = Size(m.Bytes), license = m.License, moe = m.MixtureOfExperts,
                        tier = Loc.Get(m.TierKey), purpose = Loc.Get(m.PurposeKey), context = m.ContextMax,
                        fit = new
                        {
                            mode = fit.Mode.ToString(), need = Size(fit.NeedBytes), share = Math.Round(fit.GpuShare * 100), tight = fit.Tight,
                            ceiling = fit.CeilingTokensPerSecond is { } c ? Rate(c) : null, maxContext = fit.MaxContext,
                        },
                        cpuFits = cpu.Mode == AiFitMode.Cpu,
                        downloaded = files.HasModel(m), partial = files.PartialBytes(m) is > 0 and var part ? Size(part) : null, transfer = Transfer(m.Id),
                        gpu = Measured(m, "gpu"), cpu = Measured(m, "cpu"),
                    };
                }),
                // Image, video, audio and 3D models: judged from their published figures only, never downloaded or run by the app.
                generative = Enum.GetValues<AiMedia>().Select(media => new
                {
                    media = media.ToString(), suggested = AiGenCatalog.Suggest(media, pc)?.Id,
                    models = AiGenCatalog.Models.Where(g => g.Media == media).Select(g =>
                    {
                        var v = AiGenCatalog.Judge(g, pc);
                        return new
                        {
                            id = g.Id, name = g.Name, @params = g.Params, min = Size(g.MinVram), full = Size(g.FullVram), nvidiaOnly = g.NvidiaOnly, license = g.License,
                            runtime = g.Runtime, source = g.Source, purpose = Loc.Get(g.PurposeKey), note = g.NoteKey is { } n ? Loc.Get(n) : null,
                            fit = v.Fit.ToString(), block = v.Block?.ToString(),
                        };
                    }),
                }),
            };
        }

        async Task Download(string id)
        {
            if (active is not null) throw new InvalidOperationException("Another download is in progress.");
            var model = id == "runtime" ? null : AiCatalog.Find(id) ?? throw new ArgumentException("unknown model");
            var cts = new CancellationTokenSource(); var t = transfers[id] = new AiTransfer { Cts = cts }; active = id;
            var progress = new Progress<DownloadProgress>(p => { t.Progress = p; PushSoon("ai", State); });
            PushSoon("ai", State);
            try
            {
                if (model is null) await files.GetRuntimeAsync(progress, cts.Token).ConfigureAwait(true);
                else await files.GetModelAsync(model, progress, cts.Token).ConfigureAwait(true);
                transfers.Remove(id);
                _log.LogInformation("AI download finished: {Id}", id);
            }
            catch (OperationCanceledException) { transfers.Remove(id); }
            catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                t.Error = e.Message; _log.LogWarning(e, "AI download failed: {Id}", id);
            }
            finally { cts.Dispose(); t.Cts = null; active = null; PushSoon("ai", State); }
        }

        void OnProgress(TestId id, double p) { if (id == LlamaBenchmark.Spec.Id && running is { } r) { running = r with { Percent = p * 100 }; PushSoon("ai", State); } }
        runner.Progress += OnProgress; _cleanup.Add(() => runner.Progress -= OnProgress);

        async Task Run(string id, string device)
        {
            var model = AiCatalog.Find(id) ?? throw new ArgumentException("unknown model");
            if (device is not ("gpu" or "cpu")) throw new ArgumentException("unknown device");
            if (_aiServer?.IsRunning == true) { runError = Loc.Get("Assist_StopFirst"); PushSoon("ai", State); return; }   // the chat model holds the GPU: a measurement beside it would be wrong
            runError = null; running = (id, device, 0); PushSoon("ai", State);
            try
            {
                var options = new Dictionary<string, string> { [LlamaBenchmark.ModelOption] = id, [LlamaBenchmark.DeviceOption] = device, [LlamaBenchmark.GpuOption] = Gpu()?.Name ?? "" };
                var result = await runner.RunAsync(bench, bench.Definition.DefaultDurationSeconds, options).ConfigureAwait(true);
                if (result is null) runError = runner.BlockedBy is { } h ? Loc.Get($"Workload_Busy_{h}") : Loc.Get("Ai_Busy");
                else if (result.Status == BenchmarkStatus.Completed && result.Metrics.FirstOrDefault(x => x.Key == "Bench_Ai_Gen") is { } gen)
                    results.Set(id, device, new(result.FinishedAt, result.Metrics.FirstOrDefault(x => x.Key == "Bench_Ai_Prompt")?.Value, gen.Value, result.Detail));
                else if (result.Status != BenchmarkStatus.Cancelled) runError = result.Detail;
            }
            finally { running = null; PushSoon("ai", State); }
        }

        // The assistant's download: llama.cpp first (it carries llama-server), then the model the machine is offered; one after the other, as the page allows one download at a time.
        async Task EnableAssistant(AiModel model)
        {
            if (!files.HasRuntime) { await Download("runtime"); if (transfers.ContainsKey("runtime")) return; }
            if (!files.HasModel(model)) await Download(model.Id);
        }

        Method("ai.state", _ => State());
        MethodAsync("ai.exec", async p =>
        {
            string id = Str(p, "id");
            switch (Str(p, "cmd"))
            {
                case "download": _ = Download(id); break;
                case "cancelDownload": transfers.GetValueOrDefault(id)?.Cts?.Cancel(); break;
                case "delete":
                    if (active == id || running?.Model == id) throw new InvalidOperationException("The model is in use.");
                    files.DeleteModel(AiCatalog.Find(id) ?? throw new ArgumentException("unknown model")); transfers.Remove(id); break;
                case "run": await Run(id, Str(p, "device")); break;
                case "assistantEnable":
                    if (active is not null) throw new InvalidOperationException("Another download is in progress.");
                    _ = EnableAssistant(AiAssistantPolicy.Decide(Machine()).Model ?? throw new InvalidOperationException(Loc.Get("Assist_Unavailable"))); break;
                case "cancel": if (running is not null) runner.Cancel(); break;
                case "openFolder": Directory.CreateDirectory(files.Root); Open(files.Root); break;
                default: throw new ArgumentException("unknown command");
            }
            PushSoon("ai", State);
            return null;
        });
        RegisterAssistant(files, Machine, runner);

        // After an update of the app that moved to a newer llama.cpp build, an assistant that was set up is brought along: the new build (and the
        // model, if the one offered to this machine changed) is fetched once, then the old build is removed. Never for a copy that had no assistant.
        async Task BringAlong()
        {
            if (!await Task.Run(() => files.StaleRuntimes().Count > 0).ConfigureAwait(true)) return;
            if (!files.HasRuntime && active is null && AiAssistantPolicy.Decide(Machine()).Model is { } model)
            {
                _log.LogInformation("The assistant's llama.cpp build changed with this version; fetching {Build}", AiCatalog.Runtime.Build);
                await EnableAssistant(model).ConfigureAwait(true);
            }
            await Task.Run(files.RemoveStaleRuntimes).ConfigureAwait(true);
        }
        _ = BringAlong();
    }
}
