using System.IO; using System.Net.Http; using System.Text.Json;
using Mazesta.Core.Hardware; using Mazesta.Monitoring; using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
using Mazesta.Core.Ai; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Ai; using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.Web;

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
        string? error = null; bool starting = false, generating = false, unloaded = false;
        CancellationTokenSource? replyCts = null, startCts = null; Timer? idle = null;
        Confirmation? pending = null; Activity? activity = null;
        _cleanup.Add(() => { idle?.Dispose(); replyCts?.Cancel(); startCts?.Cancel(); });

        // The user's pick among the downloaded models, kept in Data/ai/assistant.json.
        string choiceFile = Path.Combine(files.Root, "assistant.json"); string? chosen = null;
        try { if (File.Exists(choiceFile)) chosen = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(choiceFile))?.GetValueOrDefault("model"); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }

        // The downloaded models this machine can run, largest first; the pick if it is one of them, else the model on offer, else the largest that is there.
        List<AiModel> Usable(AiMachine pc) => [.. AiCatalog.Models.Where(m => files.HasModel(m) && AiFitter.Fit(m, pc).Mode != AiFitMode.TooBig).OrderByDescending(m => m.Bytes)];
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
                status = choice.Status.ToString(),
                model = m is null ? null : new { id = m.Id, name = m.Name, size = Units.FormatMeasured(m.Bytes / (double)AiFitter.Gib, "GB"), downloaded = files.HasModel(m) },
                choices = choice.Status != AiAssistantStatus.Available ? [] : Usable(pc).Select(x => new { id = x.Id, name = x.Name, size = Units.FormatMeasured(x.Bytes / (double)AiFitter.Gib, "GB"), fit = AiFitter.Fit(x, pc).Mode.ToString() }),
                runtimeReady = files.HasRuntime,
                // "paused": the model is unloaded while the assistant's own run tests the graphics card, and comes back afterwards.
                server = starting ? "starting" : server.IsRunning ? "ready" : unloaded ? "paused" : "off",
                busy = generating, error, blocked = runner.IsBusy,
                confirm = pending is { } c ? new { kind = c.Kind, items = c.Items.Select(i => new { name = i.Name, duration = i.Duration }) } : null,
                activity = activity is { } a ? new { kind = a.Kind, name = prog?.Name, percent = prog?.Percent } : null,
                chat = chat?.Id,
                history = chats.List().Select(x => new { id = x.Id, title = x.Title, updated = x.Updated.ToUnixTimeMilliseconds(), count = x.Count }),
                // A run's result goes to the page as the tool returned it: the page draws the outcomes from it, not from the model's words.
                messages = (chat?.Messages ?? []).Select(x => new { role = x.Role, text = x.Text, tools = x.Tools.Select(t => new { name = t.Name, ok = t.Ok, result = t.Name is "run_tests" or "run_benchmark" ? t.Result : null }) }),
            };
        }

        void StopServer() { idle?.Dispose(); idle = null; replyCts?.Cancel(); server.Stop(); }
        void Push() => PushSoon("assistant", State);
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
            await _window.Dispatcher.InvokeAsync(() => { unloaded = true; server.Stop(); Push(); });
            try { return await work().ConfigureAwait(false); }
            finally
            {
                if (m is not null && !ct.IsCancellationRequested)
                    try { await server.StartAsync(m, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                    catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException or HttpRequestException) { error = e.Message; _log.LogWarning(e, "AI assistant did not come back after a GPU run"); }
                await _window.Dispatcher.InvokeAsync(() => { unloaded = false; Push(); });
            }
        }
        var tools = AssistantTools(() => lastSnapshot, Ask, Running, Ui, WithoutModel, page => this.Push("assistantNav", new { page }));

        async Task Start()
        {
            var pc = machine(); var choice = AiAssistantPolicy.Decide(pc);
            var m = (choice.Status == AiAssistantStatus.Available ? Selected(pc, choice) : null) ?? throw new InvalidOperationException(Loc.Get("Assist_Unavailable"));
            if (!files.HasRuntime || !files.HasModel(m)) throw new InvalidOperationException(Loc.Get("Assist_NotDownloaded"));
            if (runner.IsBusy) throw new InvalidOperationException(Loc.Get("Ai_Busy"));
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
                var history = AiAssistantPolicy.Trim(chat.Messages.Where(x => x != reply).Select(x => x.Turn).ToList(), AiAgent.Length);
                // The model's threads call back here; the chat is the UI thread's.
                await AiAgent.RunAsync(server, AiAssistantPolicy.SystemPrompt, history, tools,
                    piece => _window.Dispatcher.BeginInvoke(() => { reply.Text += piece; Push(); }),
                    x => _window.Dispatcher.BeginInvoke(() => { reply.Tools.Add(x); Push(); }), replyCts.Token, AiAssistantPolicy.AsksToAct(text)).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { }
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
