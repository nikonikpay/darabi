using System.IO; using System.Net.Http; using System.Text.Json;
using Mazesta.Core.Hardware; using Mazesta.Monitoring; using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
using Mazesta.Core.Ai; using Mazesta.Core.Software; using Mazesta.Reporting; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Ai; using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>The chat server while the assistant is on; the AI page's benchmark refuses to run beside it (it would measure a busy GPU).</summary>
    private AiServer? _aiServer; private BenchmarkRunLog? _benchRunLog; private Func<string?>? _benchSystemHash;
    private TestCenterViewModel? _testVm; private Dictionary<string, BenchmarkComparison?>? _benchCompared;

    /// <summary>What the assistant asks the user before it starts a test or a benchmark: the list, and the answer the tool is waiting for (which of
    /// the items the user kept; null for "no").</summary>
    private sealed record Confirmation(string Kind, IReadOnlyList<(string Name, string Duration)> Items, TaskCompletionSource<bool[]?> Decision);

    /// <summary>A test or benchmark the assistant started, shown beside the chat while it runs.</summary>
    private sealed record Activity(string Kind, Func<(string Name, double Percent)?> Progress);

    /// <summary>The idle time after which the assistant's server is stopped and its GPU memory given back.</summary>
    private static readonly TimeSpan AssistantIdle = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The assistant, in its own column beside every page: a chat with a model the machine can run (the one offered by <see cref="AiAssistantPolicy"/>,
    /// or any other downloaded model that fits, chosen by the user), served by llama.cpp's llama-server on the loopback. It is opt-in: nothing is
    /// downloaded or started until the user asks. The chats are kept on this computer (<see cref="AiChats"/>) and the user deletes them. The model
    /// reads the machine through read-only tools, opens the app's pages, and may start a test or a benchmark, but only after the user confirmed it
    /// on the page (see <see cref="AssistantTools"/>).
    /// </summary>
    private void RegisterAssistant(AiFiles files, Func<AiMachine> machine, BenchmarkRunner runner)
    {
        var server = _aiServer = new AiServer(files, AiHttp); _cleanup.Add(server.Dispose);
        var engine = _sp.GetRequiredService<PollingEngine>(); SensorSnapshot? lastSnapshot = null;
        void OnSnapshot(SensorSnapshot s) => lastSnapshot = s;
        engine.SnapshotPublished += OnSnapshot; _cleanup.Add(() => engine.SnapshotPublished -= OnSnapshot);
        var chats = new AiChats(files.Root); AiChat? chat = null;
        string? error = null; bool starting = false, generating = false, unloaded = false, ownRun = false;
        CancellationTokenSource? replyCts = null, startCts = null; Timer? idle = null;
        Confirmation? pending = null; Activity? activity = null;
        _cleanup.Add(() => { idle?.Dispose(); replyCts?.Cancel(); startCts?.Cancel(); });

        // The user's pick among the downloaded models, kept in Data/ai/assistant.json.
        string choiceFile = Path.Combine(files.Root, "assistant.json"); string? chosen = null;
        try { if (File.Exists(choiceFile)) chosen = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(choiceFile))?.GetValueOrDefault("model"); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }

        // The downloaded models this machine can run and that can hold a chat, largest first; the pick if it is one of them, else the model on offer,
        // else the largest that is there.
        List<AiModel> Usable(AiMachine pc) => [.. AiCatalog.Models.Where(m => m.Bytes >= AiAssistantPolicy.MinChatModelBytes && files.HasModel(m) && AiFitter.Fit(m, pc, AiAssistantPolicy.ServerContext).Mode != AiFitMode.TooBig).OrderByDescending(m => m.Bytes)];
        AiModel? Selected(AiMachine pc, AiAssistantChoice choice)
        {
            var usable = Usable(pc);
            return usable.FirstOrDefault(m => m.Id == chosen) ?? usable.FirstOrDefault(m => m.Id == choice.Model?.Id) ?? usable.FirstOrDefault() ?? choice.Model;
        }

        object State()
        {
            var pc = machine(); var choice = AiAssistantPolicy.Decide(pc); var m = choice.Status == AiAssistantStatus.Available ? Selected(pc, choice) : null;
            var prog = activity?.Progress();
            return new
            {
                // Before the first reading the card's memory is not known yet: that is "Reading", not "no card".
                status = lastSnapshot is null && choice.Status == AiAssistantStatus.NoGpu ? "Reading" : choice.Status.ToString(),
                model = m is null ? null : new { id = m.Id, name = m.Name, size = Units.FormatMeasured(m.Bytes / (double)AiFitter.Gib, "GB"), downloaded = files.HasModel(m) },
                choices = choice.Status != AiAssistantStatus.Available ? [] : Usable(pc).Select(x => new { id = x.Id, name = x.Name, size = Units.FormatMeasured(x.Bytes / (double)AiFitter.Gib, "GB"), fit = AiFitter.Fit(x, pc, AiAssistantPolicy.ServerContext).Mode.ToString() }),
                runtimeReady = files.HasRuntime,
                // "paused": the model is unloaded while the assistant's own run tests the graphics card, and comes back afterwards.
                server = starting ? "starting" : server.IsRunning ? "ready" : unloaded ? "paused" : "off",
                busy = generating, error, blocked = runner.IsBusy,
                confirm = pending is { } c ? new { kind = c.Kind, items = c.Items.Select(i => new { name = i.Name, duration = i.Duration }) } : null,
                activity = activity is { } a ? new { kind = a.Kind, name = prog?.Name, percent = prog?.Percent } : null,
                chat = chat?.Id,
                history = chats.List().Select(x => new { id = x.Id, title = x.Title, updated = x.Updated.ToUnixTimeMilliseconds(), count = x.Count }),
                // A run's result goes to the page as the tool returned it: the page draws the outcomes from it, not from the model's words.
                // So are a file the assistant made (the chat offers it with an Open button) and a program's verdict (drawn as a card).
                messages = (chat?.Messages ?? []).Select(x => new { role = x.Role, text = x.Text, tools = x.Tools.Select(t => new { name = t.Name, ok = t.Ok, result = t.Name is "run_tests" or "run_benchmark" or "export_report" or "check_software" or "run_windows_command" ? t.Result : null }) }),
            };
        }

        void StopServer() { idle?.Dispose(); idle = null; replyCts?.Cancel(); server.Stop(); }
        void Push() => PushSoon("assistant", State);
        // The card's memory is known from the first reading on: the page learns it then, not on its next look.
        void OnFirst(SensorSnapshot _) { engine.SnapshotPublished -= OnFirst; Push(); }
        engine.SnapshotPublished += OnFirst; _cleanup.Add(() => engine.SnapshotPublished -= OnFirst);
        void Keep() { if (chat is null) return; try { chats.Save(chat); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning(e, "Could not keep the assistant's chat"); } }
        Task<T> Ui<T>(Func<Task<T>> work) => _window.Dispatcher.InvokeAsync(work).Task.Unwrap();

        // The tools ask here. A test or a benchmark starts only on a "yes" from the page, for the items the user left ticked; stopping the assistant or
        // cancelling the reply is a "no".
        async Task<bool[]?> Ask(string kind, IReadOnlyList<(string, string)> items, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _window.Dispatcher.InvokeAsync(() => { pending = new(kind, items, tcs); Push(); });
            using var reg = ct.Register(() => tcs.TrySetResult(null));
            try { return await tcs.Task.ConfigureAwait(false); }
            finally { await _window.Dispatcher.InvokeAsync(() => { pending = null; Push(); }); }
        }
        async Task<T> Running<T>(Activity a, Func<Task<T>> work)
        {
            await _window.Dispatcher.InvokeAsync(() => { activity = a; Push(); });
            using var ticker = new CancellationTokenSource();
            _ = Task.Run(async () => { while (!ticker.IsCancellationRequested) { await Task.Delay(1000).ConfigureAwait(false); _ = _window.Dispatcher.BeginInvoke(Push); } });
            try { return await work().ConfigureAwait(false); }
            finally { ticker.Cancel(); await _window.Dispatcher.InvokeAsync(() => { activity = null; Push(); }); }
        }
        // A run on the graphics card: the model is unloaded first, so the card's memory and its clocks are the test's, and loaded again afterwards
        // for the answer (unless the reply was stopped meanwhile).
        async Task<string> WithoutModel(Func<Task<string>> work, CancellationToken ct)
        {
            var m = server.Model;
            await _window.Dispatcher.InvokeAsync(() => { unloaded = true; ownRun = true; server.Stop(); Push(); });
            try { return await work().ConfigureAwait(false); }
            finally
            {
                if (m is not null && !ct.IsCancellationRequested)
                    try { await server.StartAsync(m, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                    catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException or HttpRequestException) { error = e.Message; _log.LogWarning(e, "AI assistant did not come back after a GPU run"); }
                await _window.Dispatcher.InvokeAsync(() => { unloaded = false; ownRun = false; Push(); });
            }
        }
        // A test, a benchmark or the GPU tuning started from its own page: the model is unloaded for it the same way (a reply being written is
        // stopped), so no measurement shares the machine with it, and it is loaded again when the gate is free. The assistant's own runs do this in
        // WithoutModel above.
        var gate = _sp.GetRequiredService<Diagnostics.WorkloadGate>(); AiModel? resume = null;
        void OnGate(Diagnostics.Workload? holder)
        {
            if (holder is not null)
            {
                // On the thread that took the gate, before the load starts: the model's process is gone before anything is measured.
                if (ownRun || !(server.IsRunning || starting)) return;
                resume = server.Model; startCts?.Cancel(); replyCts?.Cancel(); server.Stop();
                _window.Dispatcher.BeginInvoke(() => { idle?.Dispose(); idle = null; unloaded = true; Push(); });
                _log.LogInformation("AI assistant unloaded for a {Load}", holder);
                return;
            }
            _window.Dispatcher.BeginInvoke(async () =>
            {
                if (resume is null) return;
                resume = null; unloaded = false;
                if (gate.Holder is null && !server.IsRunning) try { await Start().ConfigureAwait(true); } catch (InvalidOperationException e) { error = e.Message; }
                Push();
            });
        }
        gate.Changed += OnGate; _cleanup.Add(() => gate.Changed -= OnGate);
        // The parts the programs' tiers are judged on, and the line the model is told about this computer: read once (the inventory is cached), the
        // card's memory from its sensors as the AI page reads it.
        var inventory = _sp.GetRequiredService<InventoryCache>(); (bool?, bool?)? gpuApi = null;
        async Task<SoftMachine> SoftPc()
        {
            var inv = await inventory.GetAsync().ConfigureAwait(false); var pc = machine();
            string? gpu = pc.GpuName ?? inv.Gpus.FirstOrDefault()?.Name?.Trim();
            // The memory, the name and the card's answers all of one card: WMI's figure only of the card so named (and it stops at 4 GB: only below that is it the size).
            long? vram = pc.VramBytes ?? inv.Gpus.Where(g => gpu is not null && string.Equals(g.Name?.Trim(), gpu, StringComparison.OrdinalIgnoreCase))
                .Select(g => g.AdapterRamBytes).FirstOrDefault(b => b is > 0 and < (4L << 30) - (64L << 20));
            var (dx12, dxr) = gpuApi ??= Diagnostics.Gpu.GpuFeatures.Describe(gpu);   // asked of the card once
            return new(inv.Cpu?.Name?.Trim(), inv.Cpu?.PhysicalCores, inv.Cpu?.LogicalProcessors, inv.TotalPhysicalMemoryBytes ?? pc.RamTotalBytes, gpu, vram,
                dx12, dxr, System.Runtime.Intrinsics.X86.Avx2.IsSupported, System.Runtime.Intrinsics.X86.Sse42.IsSupported);
        }
        async Task<string> MachineLine()
        {
            var inv = await inventory.GetAsync().ConfigureAwait(false); var pc = await SoftPc().ConfigureAwait(false);
            static string Gb(long b) => Math.Round(b / 1073741824.0).ToString(System.Globalization.CultureInfo.InvariantCulture) + " GB";
            var parts = new List<string>();
            if (pc.CpuName is { } c) parts.Add($"processor (CPU) {c}" + (pc.Cores is { } k ? $", {k} cores" + (pc.Threads is { } th ? $", {th} threads" : "") : ""));
            if (pc.RamBytes is { } r) parts.Add($"RAM {Gb(r)}" + (inv.MemoryModules.Count > 0 ? $" in {inv.MemoryModules.Count} modules" : ""));
            foreach (var g in inv.Gpus.Where(g => g.Name is not null))
                parts.Add($"graphics card (GPU) {g.Name!.Trim()}" + (pc.VramBytes is { } v && pc.GpuName is not null && Diagnostics.Benchmarks.BenchmarkPeers.PartName(g.Name) == Diagnostics.Benchmarks.BenchmarkPeers.PartName(pc.GpuName) ? $" with {Gb(v)} of its own memory (VRAM)" : ""));
            if (inv.Os?.Caption is { } os) parts.Add($"{os.Trim()} {inv.Os.Version}");
            if (inv.Motherboard is { } mb) parts.Add($"motherboard {mb.Manufacturer} {mb.Product}".Trim());
            foreach (var d in inv.Storage) if (d.FriendlyName is { } dn) parts.Add($"drive {dn.Trim()}" + (d.SizeBytes is { } ds ? $" {Gb(ds)}" : ""));
            return string.Join("; ", parts);
        }
        RegisterApps(SoftPc);
        // Files the assistant made in this session; only these may be opened from the chat.
        var offered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tools = AssistantTools(() => lastSnapshot, Ask, Running, Ui, WithoutModel, (page, target) => this.Push("assistantNav", new { page, target }), SoftPc, path => { lock (offered) offered.Add(path); });

        async Task Start()
        {
            var pc = machine(); var choice = AiAssistantPolicy.Decide(pc);
            var m = (choice.Status == AiAssistantStatus.Available ? Selected(pc, choice) : null) ?? throw new InvalidOperationException(Loc.Get("Assist_Unavailable"));
            if (!files.HasRuntime || !files.HasModel(m)) throw new InvalidOperationException(Loc.Get("Assist_NotDownloaded"));
            if (runner.IsBusy || gate.Holder is not null) throw new InvalidOperationException(Loc.Get("Ai_Busy"));
            if (starting || server.IsRunning) return;
            error = null; starting = true; startCts = new CancellationTokenSource(); Push();
            try
            {
                await server.StartAsync(m, startCts.Token).ConfigureAwait(true);
                _log.LogInformation("AI assistant started: {Model}", m.Id);
                idle = new Timer(_ => _window.Dispatcher.BeginInvoke(() =>
                {
                    if (!generating && DateTime.UtcNow - server.LastUse > AssistantIdle) { StopServer(); Push(); }
                }), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException or HttpRequestException)
            {
                error = e.Message; _log.LogWarning(e, "AI assistant failed to start");
            }
            finally { starting = false; startCts.Dispose(); startCts = null; Push(); }
        }

        async Task Send(string text)
        {
            text = text.Trim();
            if (text.Length is 0 or > 2000) throw new ArgumentException("message length");
            if (generating || !server.IsRunning) throw new InvalidOperationException("The assistant is not ready.");
            error = null; chat ??= AiChats.New(DateTimeOffset.Now);
            chat.Messages.Add(new() { Role = "user", Text = text }); var reply = new AiChatMessage { Role = "assistant" }; chat.Messages.Add(reply);
            chat.Updated = DateTimeOffset.Now; Keep();
            generating = true; replyCts = new CancellationTokenSource(); Push();
            try
            {
                // What the message plainly asks is decided here, not by the model (see AppGuide): a page is opened at once and said in the app's own
                // words; for a question the app first reads what answers it, and the model words the answer from that.
                var route = AppGuide.Route(text);
                if (await Direct(route, reply, replyCts.Token).ConfigureAwait(true)) return;
                var history = AiAssistantPolicy.Trim(chat.Messages.Where(x => x != reply).Select(x => x.Turn).ToList(), AiAgent.Length);
                string prompt = AiAssistantPolicy.Prompt(await MachineLine().ConfigureAwait(true), AppGuide.PageList(Loc.Get));
                // The model's threads call back here; the chat is the UI thread's.
                await AiAgent.RunAsync(server, prompt, history, tools,
                    piece => _window.Dispatcher.BeginInvoke(() => { reply.Text += piece; Push(); }),
                    x => _window.Dispatcher.BeginInvoke(() => { reply.Tools.Add(x); Push(); }), replyCts.Token, route.Intent == AiIntent.None && AiAssistantPolicy.AsksToAct(text), First(route, text)).ConfigureAwait(true);
                // The UI thread may still hold the last pieces; they are in before the reply is tidied.
                await _window.Dispatcher.InvokeAsync(() =>
                {
                    reply.Text = AiText.Unloop(AiText.CutAtMarker(reply.Text));
                    if (WindowsActions.UncheckedIn(reply.Text) is { Count: > 0 } made) reply.Text += "\n\n⚠ " + Loc.Format("Assist_Cmd_Unchecked", string.Join("، ", made));
                });
            }
            catch (OperationCanceledException) { if (reply.Text == Loc.Get("Assist_Dns_Testing") || reply.Text == Loc.Get("Assist_Making")) reply.Text = Loc.Get("Assist_Stopped"); }
            catch (Exception e) when (e is IOException or HttpRequestException or InvalidOperationException)
            {
                error = e.Message; _log.LogWarning(e, "AI assistant reply failed");
            }
            finally
            {
                generating = false; replyCts.Dispose(); replyCts = null;
                if (reply.Text.Length == 0 && reply.Tools.Count == 0) chat.Messages.Remove(reply);
                chat.Updated = DateTimeOffset.Now; Keep(); Push();
            }
        }

        // A page the message names is opened now, with no model: it can not pick the wrong page, and it answers at once. A file asked for is made
        // and offered the same way.
        async Task<bool> Direct(AiRoute route, AiChatMessage reply, CancellationToken ct)
        {
            if (route.Intent == AiIntent.Navigate && route.Place is { } place)
            {
                string? target = route.App?.Id ?? place.Target;
                string args = JsonSerializer.Serialize(new { page = place.Page, target });
                var (result, ok) = await AiAgent.InvokeAsync(tools, new ToolCall("direct", "open_page", args), ct).ConfigureAwait(true);
                reply.Tools.Add(new("open_page", args, result, ok));
                string pageName = Loc.Get(AppGuide.Page(place.Page)!.TitleKey);
                reply.Text = route.App is { } app ? Loc.Format("Assist_OpenedApp", pageName, app.Name)
                    : place.Target is null ? Loc.Format("Assist_Opened", pageName)
                    : Loc.Format("Assist_Pointed", pageName, Loc.Get(place.TitleKey)) + (place.HintKey is { } hint ? " " + Loc.Get(hint) : "");
                return true;
            }
            if (route.Intent == AiIntent.Overlay)
            {
                string args = JsonSerializer.Serialize(new { on = route.On });
                var (result, ok) = await AiAgent.InvokeAsync(tools, new ToolCall("direct", "set_overlay", args), ct).ConfigureAwait(true);
                reply.Tools.Add(new("set_overlay", args, result, ok));
                using var d = JsonDocument.Parse(result);
                reply.Text = !ok || d.RootElement.TryGetProperty("error", out _) ? Loc.Get("Assist_Overlay_Failed")
                    : Loc.Get(d.RootElement.TryGetProperty("shown", out var shown) && shown.GetBoolean() ? "Assist_Overlay_On" : "Assist_Overlay_Off");
                return true;
            }
            if (route.Intent == AiIntent.Help) { reply.Text = Loc.Get("Assist_Help"); return true; }
            // The tray monitor, on or off, or on with the temperature it warns at ("tell me when the CPU passes 80").
            if (route.Intent is AiIntent.Tray or AiIntent.Alert)
            {
                if (route.Intent == AiIntent.Alert && route.Value is not (>= 60 and <= 105))
                {
                    reply.Text = route.Value is null ? Loc.Format("Assist_Alert_Ask", _config.TrayCpuAlertC, _config.TrayGpuAlertC) : Loc.Get("Assist_Alert_Range");
                    return true;
                }
                string args = route.Intent == AiIntent.Tray ? JsonSerializer.Serialize(new { on = route.On })
                    : JsonSerializer.Serialize(new { on = true, cpuAlertC = route.Part is null or "cpu" ? route.Value : null, gpuAlertC = route.Part is null or "gpu" ? route.Value : null });
                var (result, ok) = await AiAgent.InvokeAsync(tools, new ToolCall("direct", "set_tray", args), ct).ConfigureAwait(true);
                reply.Tools.Add(new("set_tray", args, result, ok));
                using var d = JsonDocument.Parse(result);
                string? why = d.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                reply.Text = why is not null ? Loc.Format("Assist_Tray_Failed", why)
                    : route.Intent == AiIntent.Tray ? (route.On ? Loc.Format("Assist_Tray_On", _config.TrayIdleIntervalMinutes, _config.TrayHealthIntervalMinutes, _config.TrayCpuAlertC, _config.TrayGpuAlertC) : Loc.Get("Assist_Tray_Off"))
                    : Loc.Format("Assist_Alert_Set_" + (route.Part ?? "both"), route.Value ?? 0, _config.TrayIdleIntervalMinutes, _config.TrayWatchIntervalSeconds);
                return true;
            }
            if (route.Intent == AiIntent.WinOpen)
            {
                string args = JsonSerializer.Serialize(new { window = route.Ids![0] });
                var (result, ok) = await AiAgent.InvokeAsync(tools, new ToolCall("direct", "open_windows", args), ct).ConfigureAwait(true);
                reply.Tools.Add(new("open_windows", args, result, ok));
                using var d = JsonDocument.Parse(result);
                reply.Text = d.RootElement.TryGetProperty("name", out var n) ? Loc.Format("Assist_WinOpened", n.GetString() ?? "") : Loc.Get("Assist_WinOpen_Failed");
                return true;
            }
            // A Windows command: only from the app's checked list, with what it does and its warning. One that is not there is not made up.
            if (route.Intent is AiIntent.WinCommand or AiIntent.WinCommandUnknown)
            {
                string args = JsonSerializer.Serialize(new { topic = route.Ids is { } ids ? string.Join(",", ids) : "" });
                var cmds = (route.Ids ?? []).Select(WindowsActions.Command).OfType<WinCommand>().ToList();
                reply.Tools.Add(new("windows_command", args, JsonSerializer.Serialize(new { found = cmds.Count > 0, commands = cmds.Select(c => new { id = c.Id, command = c.Command }) }), true));
                reply.Text = cmds.Count == 0
                    ? Loc.Get("Assist_Cmd_None") + "\n" + string.Join("\n", WindowsActions.Commands.Select(c => "- " + Loc.Get("WinCmd_" + c.Id)))
                    : string.Join("\n\n", cmds.Select(c => Loc.Get("WinCmd_" + c.Id) + ":\n" + c.Command + "\n" + Loc.Get("WinCmd_" + c.Id + "_What")
                        + (c.Warn ? "\n⚠ " + Loc.Get("WinCmd_" + c.Id + "_Warn") : "") + (c.Admin ? "\n" + Loc.Get("Assist_Cmd_Admin") : "") + (c.Run is not null ? "\n" + Loc.Get("Assist_Cmd_CanRun") : "")));
                return true;
            }
            if (route.Intent == AiIntent.Drivers)
            {
                await _window.Dispatcher.InvokeAsync(() => { reply.Text = Loc.Get("Drivers_Checking"); Push(); });
                var (result, ok) = await AiAgent.InvokeAsync(tools, new ToolCall("direct", "check_drivers", "{}"), ct).ConfigureAwait(true);
                reply.Tools.Add(new("check_drivers", "{}", result, ok));
                reply.Text = AssistantReplies.Drivers(result);
                if (!ct.IsCancellationRequested) this.Push("assistantNav", new { page = "drivers", target = (string?)null });
                return true;
            }
            if (route.Intent == AiIntent.TestsInfo && _testVm is { } tv)
            {
                // Every test of the part (an area's id prefix), not only the few the chat runs.
                var prefixes = route.Areas!.Select(x => x + ".").ToList();
                var rows = await _window.Dispatcher.InvokeAsync(() => tv.Rows.Where(r => prefixes.Any(p => r.Definition.Id.Value.StartsWith(p, StringComparison.Ordinal)))
                    .Select(r => $"- {r.Name} ({r.Definition.DefaultDurationSeconds} s)" + (r.IsAvailable ? "" : $": {r.UnavailableText}")).ToList());
                reply.Text = Loc.Get("Assist_TestsInfo") + "\n" + string.Join("\n", rows);
                return true;
            }
            // A test run: the app runs it and words the result itself, the outcomes as the engine gave them and the judgment as the checkup made it.
            if (route.Intent == AiIntent.Tests && First(route, "") is [var runCall])
            {
                var (result, ok) = await AiAgent.InvokeAsync(tools, runCall, ct).ConfigureAwait(true);
                reply.Tools.Add(new("run_tests", runCall.Arguments, result, ok));
                reply.Text = AssistantReplies.Tests(result);
                return true;
            }
            // "Diagnose the system": the app runs its smart diagnosis (after the user confirmed on the page), opens its page, and tells what it found.
            if (route.Intent == AiIntent.Checkup)
            {
                var (result, ok) = await AiAgent.InvokeAsync(tools, new ToolCall("direct", "run_checkup", "{}"), ct).ConfigureAwait(true);
                reply.Tools.Add(new("run_checkup", "{}", result, ok));
                reply.Text = AssistantReplies.Checkup(result);
                if (!ct.IsCancellationRequested && result.Contains("\"started\":true", StringComparison.Ordinal)) this.Push("assistantNav", new { page = "checkup", target = (string?)null });
                return true;
            }
            if (route.Intent == AiIntent.Games)
            {
                var (result, ok) = await AiAgent.InvokeAsync(tools, new ToolCall("direct", "get_machine_summary", "{\"part\":\"all\"}"), ct).ConfigureAwait(true);
                reply.Tools.Add(new("get_machine_summary", "{\"part\":\"all\"}", result, ok));
                var pc = await SoftPc().ConfigureAwait(true);
                static string Gb(long? b) => b is { } x ? Math.Round(x / 1073741824.0).ToString(System.Globalization.CultureInfo.InvariantCulture) + " GB" : "—";
                reply.Text = Loc.Format("Assist_Games", pc.GpuName ?? "—", Gb(pc.VramBytes), pc.CpuName ?? "—", Gb(pc.RamBytes));
                return true;
            }
            if (route.Intent == AiIntent.ReportFile)
            {
                string args = JsonSerializer.Serialize(new { format = route.Format, index = route.Index });
                await _window.Dispatcher.InvokeAsync(() => { reply.Text = Loc.Get("Assist_Making"); Push(); });
                var (result, ok) = await AiAgent.InvokeAsync(tools, new ToolCall("direct", "export_report", args), ct).ConfigureAwait(true);
                reply.Tools.Add(new("export_report", args, result, ok));
                using var d = JsonDocument.Parse(result);
                // Making the file is not stopped half way (a half-written PDF helps nobody): when Stop came meanwhile, the file is still offered.
                reply.Text = d.RootElement.TryGetProperty("error", out var e) ? Loc.Format("Assist_FileFailed", e.GetString() ?? "")
                    : Loc.Format("Assist_FileReady", d.RootElement.GetProperty("file").GetString() ?? "", d.RootElement.GetProperty("report").GetString() ?? "");
                return true;
            }
            // A question with one right answer, from what the app reads: the app writes the answer (see AssistantReplies).
            if (route.Intent is AiIntent.Specs or AiIntent.Sensors or AiIntent.Software or AiIntent.SoftwareList or AiIntent.Report or AiIntent.Dns && First(route, "") is { } calls)
            {
                if (route.Intent == AiIntent.Dns) await _window.Dispatcher.InvokeAsync(() => { reply.Text = Loc.Get("Assist_Dns_Testing"); Push(); });
                var results = new List<string>();
                foreach (var c in calls)
                {
                    var (result, ok) = await AiAgent.InvokeAsync(tools, c, ct).ConfigureAwait(true);
                    reply.Tools.Add(new(c.Name, c.Arguments, result, ok)); results.Add(result);
                }
                reply.Text = route.Intent switch
                {
                    AiIntent.Specs => AssistantReplies.Specs(route.Part ?? "all", results[0]) + (results.Count > 1 && AssistantReplies.PartTests(results[1]) is { Length: > 0 } tested ? "\n\n" + tested : ""),
                    AiIntent.Sensors => AssistantReplies.Sensors(route.Kind, route.Part, results[0]),
                    AiIntent.Software or AiIntent.SoftwareList => AssistantReplies.Software(results[0], route.Intent == AiIntent.Software),
                    AiIntent.Report => AssistantReplies.Report(results[0], results[1], route.Kind == "Temperature", route.Part),
                    _ => AssistantReplies.Dns(results[0]),
                };
                // A program asked about is shown on the programs (or games) page as well, with its card marked (not once the reply was stopped).
                if (route.App is { } app && route.Intent == AiIntent.Software && !ct.IsCancellationRequested)
                {
                    string args = JsonSerializer.Serialize(new { page = app.Category == Core.Software.SoftCategory.Game ? "games" : "apps", target = app.Id });
                    var (r2, ok2) = await AiAgent.InvokeAsync(tools, new ToolCall("direct", "open_page", args), ct).ConfigureAwait(true);
                    reply.Tools.Add(new("open_page", args, r2, ok2));
                }
                return true;
            }
            // "How do I…": the place is opened as well as explained, so what the answer says is on the screen.
            if (route.Intent == AiIntent.HowTo && route.Place is { } where) this.Push("assistantNav", new { page = where.Page, target = where.Target });
            return false;
        }

        // For a question the app reads what answers it first; the model then answers from that alone.
        static IReadOnlyList<ToolCall>? First(AiRoute route, string text)
        {
            static ToolCall C(string name, object args) => new("pre_" + name, name, JsonSerializer.Serialize(args));
            return route.Intent switch
            {
                // A part that is tested also gets what its newest recorded test found.
                AiIntent.Specs => route.Part is { } sp && PartHistory.Knows(sp) ? [C("get_machine_summary", new { part = sp }), C("get_part_tests", new { part = sp })] : [C("get_machine_summary", new { part = route.Part })],
                AiIntent.Sensors => [C("get_sensors", new { kind = route.Kind })],
                AiIntent.PcieErrors => [C("get_pcie_errors", new { })],
                AiIntent.Dns => [C("test_dns", new { })],
                AiIntent.Crashes => [C("get_crashes", new { })],
                AiIntent.Software when route.App is { } app => [C("check_software", new { app = app.Id })],
                AiIntent.SoftwareList => [C("check_software", new { category = route.Category?.ToString() })],
                AiIntent.Report => [C("list_reports", new { limit = 5 }), C("get_report", new { index = route.Index })],
                AiIntent.Tests when route.Areas is { Count: > 0 } areas => [C("run_tests", new { areas, all = route.All, together = route.Together, seconds = route.Total ? null : route.Seconds, total_seconds = route.Total ? route.Seconds : null })],
                AiIntent.Benchmarks when route.Areas is { Count: > 0 } benchmarks => [C("run_benchmark", new { benchmarks })],
                AiIntent.HowTo => [C("find_in_app", new { query = text })],
                _ => null,
            };
        }

        static bool[]? Kept(JsonElement p, int count)
        {
            if (!Bool(p, "value")) return null;
            if (!p.TryGetProperty("keep", out var k) || k.ValueKind != JsonValueKind.Array) return [.. Enumerable.Repeat(true, count)];
            var keep = new bool[count];
            foreach (var i in k.EnumerateArray()) if (i.TryGetInt32(out int n) && n >= 0 && n < count) keep[n] = true;
            return keep.Any(x => x) ? keep : null;
        }

        Method("assistant.state", _ => State());
        MethodAsync("assistant.exec", async p =>
        {
            switch (Str(p, "cmd"))
            {
                case "start": await Start(); break;
                case "stop": startCts?.Cancel(); pending?.Decision.TrySetResult(null); StopServer(); break;
                case "send": await Send(Str(p, "text")); break;
                case "cancel": pending?.Decision.TrySetResult(null); replyCts?.Cancel(); break;
                case "confirm": if (pending is { } c) c.Decision.TrySetResult(Kept(p, c.Items.Count)); break;
                case "new": if (!generating) chat = null; break;
                case "open":
                    if (generating) throw new InvalidOperationException(Loc.Get("Assist_Busy"));
                    chat = chats.Load(Str(p, "id")) ?? throw new ArgumentException("unknown chat"); error = null; break;
                case "delete":
                    {
                        string id = Str(p, "id");
                        if (generating && chat?.Id == id) throw new InvalidOperationException(Loc.Get("Assist_Busy"));
                        chats.Delete(id); if (chat?.Id == id) chat = null; break;
                    }
                case "openFile":
                    {
                        // One the assistant made here, or any file in a report's folder (a past chat's file, made before a restart).
                        string path = Path.GetFullPath(Str(p, "path")); bool known; lock (offered) known = offered.Contains(path);
                        known |= path.StartsWith(Path.GetFullPath(_paths.ReportsDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                        if (!known || !File.Exists(path)) throw new ArgumentException("unknown file");
                        Open(path); return null;
                    }
                case "deleteAll": if (generating) throw new InvalidOperationException(Loc.Get("Assist_Busy")); chats.DeleteAll(); chat = null; break;
                case "select":
                    {
                        string id = Str(p, "id");
                        if (!Usable(machine()).Any(x => x.Id == id)) throw new ArgumentException("unknown model");
                        if (generating || starting) throw new InvalidOperationException(Loc.Get("Assist_Busy"));
                        chosen = id; StopServer();
                        try { File.WriteAllText(choiceFile, JsonSerializer.Serialize(new Dictionary<string, string> { ["model"] = id })); }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning(e, "Could not keep the assistant's model choice"); }
                        break;
                    }
                default: throw new ArgumentException("unknown command");
            }
            Push();
            return null;
        });
    }
}
