using System.IO; using System.Net.Http;
using Mazesta.Core.Hardware; using Mazesta.Monitoring; using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
using Mazesta.Core.Ai; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Ai; using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    /// <summary>The chat server while the assistant is on; the AI page's benchmark refuses to run beside it (it would measure a busy GPU).</summary>
    private AiServer? _aiServer; private BenchmarkRunLog? _benchRunLog; private Func<string?>? _benchSystemHash;
    private sealed class ChatMessage(string role, string text) { public string Role { get; } = role; public string Text { get; set; } = text; public List<(string Name, bool Ok)> Tools { get; } = []; }

    /// <summary>The idle time after which the assistant's server is stopped and its GPU memory given back.</summary>
    private static readonly TimeSpan AssistantIdle = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The assistant page: a chat with the model the machine is offered (see <see cref="AiAssistantPolicy"/>), served by llama.cpp's llama-server
    /// on the loopback. It is opt-in: nothing is downloaded or started until the user asks. The chat lives in memory only. In this version the
    /// model can only talk; it gets no tools and its system prompt says so.
    /// </summary>
    private void RegisterAssistant(AiFiles files, Func<AiMachine> machine, BenchmarkRunner runner)
    {
        var server = _aiServer = new AiServer(files, AiHttp); _cleanup.Add(server.Dispose);
        var engine = _sp.GetRequiredService<PollingEngine>(); SensorSnapshot? lastSnapshot = null;
        void OnSnapshot(SensorSnapshot s) => lastSnapshot = s;
        engine.SnapshotPublished += OnSnapshot; _cleanup.Add(() => engine.SnapshotPublished -= OnSnapshot);
        var tools = AssistantTools(() => lastSnapshot);
        var chat = new List<ChatMessage>(); string? error = null; bool starting = false, generating = false;
        CancellationTokenSource? replyCts = null, startCts = null; Timer? idle = null;
        _cleanup.Add(() => { idle?.Dispose(); replyCts?.Cancel(); startCts?.Cancel(); });

        object State()
        {
            var pc = machine(); var choice = AiAssistantPolicy.Decide(pc); var m = choice.Model;
            return new
            {
                status = choice.Status.ToString(),
                model = m is null ? null : new { id = m.Id, name = m.Name, size = Units.FormatMeasured(m.Bytes / (double)AiFitter.Gib, "GB"), downloaded = files.HasModel(m) },
                runtimeReady = files.HasRuntime,
                server = starting ? "starting" : server.IsRunning ? "ready" : "off",
                busy = generating, error, blocked = runner.IsBusy,
                messages = chat.Select(x => new { role = x.Role, text = x.Text, tools = x.Tools.Select(t => new { name = t.Name, ok = t.Ok }) }),
            };
        }

        void StopServer() { idle?.Dispose(); idle = null; replyCts?.Cancel(); server.Stop(); }

        async Task Start()
        {
            var m = AiAssistantPolicy.Decide(machine()).Model ?? throw new InvalidOperationException(Loc.Get("Assist_Unavailable"));
            if (!files.HasRuntime || !files.HasModel(m)) throw new InvalidOperationException(Loc.Get("Assist_NotDownloaded"));
            if (runner.IsBusy) throw new InvalidOperationException(Loc.Get("Ai_Busy"));
            if (starting || server.IsRunning) return;
            error = null; starting = true; startCts = new CancellationTokenSource(); PushSoon("assistant", State);
            try
            {
                await server.StartAsync(m, startCts.Token).ConfigureAwait(true);
                _log.LogInformation("AI assistant started: {Model}", m.Id);
                idle = new Timer(_ => _window.Dispatcher.BeginInvoke(() =>
                {
                    if (!generating && DateTime.UtcNow - server.LastUse > AssistantIdle) { StopServer(); PushSoon("assistant", State); }
                }), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException or HttpRequestException)
            {
                error = e.Message; _log.LogWarning(e, "AI assistant failed to start");
            }
            finally { starting = false; startCts.Dispose(); startCts = null; PushSoon("assistant", State); }
        }

        async Task Send(string text)
        {
            text = text.Trim();
            if (text.Length is 0 or > 2000) throw new ArgumentException("message length");
            if (generating || !server.IsRunning) throw new InvalidOperationException("The assistant is not ready.");
            error = null; chat.Add(new("user", text)); var reply = new ChatMessage("assistant", ""); chat.Add(reply);
            generating = true; replyCts = new CancellationTokenSource(); PushSoon("assistant", State);
            try
            {
                var history = AiAssistantPolicy.Trim<ChatMessage>(chat.Where(x => x != reply).ToList(), x => x.Text.Length);
                // The model's threads call back here; the chat is the UI thread's.
                await AiAgent.RunAsync(server, AiAssistantPolicy.SystemPrompt, [.. history.Select(x => new ChatTurn(x.Role, x.Text))], tools,
                    piece => _window.Dispatcher.BeginInvoke(() => { reply.Text += piece; PushSoon("assistant", State); }),
                    (name, ok) => _window.Dispatcher.BeginInvoke(() => { reply.Tools.Add((name, ok)); PushSoon("assistant", State); }), replyCts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (e is IOException or HttpRequestException or InvalidOperationException)
            {
                error = e.Message; _log.LogWarning(e, "AI assistant reply failed");
            }
            finally
            {
                generating = false; replyCts.Dispose(); replyCts = null;
                if (reply.Text.Length == 0) chat.Remove(reply);
                PushSoon("assistant", State);
            }
        }

        Method("assistant.state", _ => State());
        MethodAsync("assistant.exec", async p =>
        {
            switch (Str(p, "cmd"))
            {
                case "start": await Start(); break;
                case "stop": startCts?.Cancel(); StopServer(); break;
                case "send": await Send(Str(p, "text")); break;
                case "cancel": replyCts?.Cancel(); break;
                case "clear": if (!generating) chat.Clear(); break;
                default: throw new ArgumentException("unknown command");
            }
            PushSoon("assistant", State);
            return null;
        });
    }
}
