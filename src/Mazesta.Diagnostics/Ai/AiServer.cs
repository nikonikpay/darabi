using System.Diagnostics; using System.Net; using System.Net.Http; using System.Net.Sockets; using System.Text; using System.Text.Json; using System.Text.Json.Nodes;
using Mazesta.Core.Ai;
namespace Mazesta.Diagnostics.Ai;

/// <summary>A message of the chat. An answer keeps the tools it called and what they returned, so a later turn sees that its earlier results came
/// from a tool, and that an answer without one ran nothing.</summary>
public readonly record struct ChatTurn(string Role, string Text, IReadOnlyList<ToolExchange>? Tools = null);

/// <summary>One tool call of an answer: what was asked, what came back (the JSON the model was given) and whether the tool ran.</summary>
public sealed record ToolExchange(string Name, string Arguments, string Result, bool Ok);

/// <summary>A function the model asked for: its name and the arguments as the JSON text it wrote (which may be wrong, and is checked before use).</summary>
public sealed record ToolCall(string Id, string Name, string Arguments);

/// <summary>One model turn: the text it wrote and the tools it asked for (none when it answered).</summary>
public sealed record ChatReply(string Text, IReadOnlyList<ToolCall> Calls);

/// <summary>What the assistant's loop needs of a model: one turn over the messages so far, the text handed on as it is written. With
/// <paramref name="mustCallTool"/> the turn has to be a tool call.</summary>
public interface IChatModel { Task<ChatReply> CompleteAsync(JsonArray messages, JsonArray? tools, bool mustCallTool, Action<string> onText, CancellationToken ct); }

/// <summary>
/// llama.cpp's own <c>llama-server</c> on the loopback address, serving one downloaded model to the chat page. It is started only when the user
/// asks, listens on 127.0.0.1 alone on a free port, and is killed on stop, on idle and on exit. The page never sees the port: replies are
/// streamed through <see cref="CompleteAsync"/>.
/// </summary>
public sealed class AiServer(AiFiles files, HttpClient http) : IChatModel, IDisposable
{
    private Process? _process; private int _port; private readonly Queue<string> _tail = new();
    public AiModel? Model { get; private set; }
    public bool IsRunning => _process is { HasExited: false };
    public DateTime LastUse { get; private set; } = DateTime.UtcNow;

    public async Task StartAsync(AiModel model, CancellationToken ct)
    {
        if (IsRunning && Model == model) return;
        Stop();
        if (!files.HasRuntime || !files.HasModel(model)) throw new InvalidOperationException("The assistant is not downloaded.");
        _port = FreePort(); lock (_tail) _tail.Clear();
        // No -ngl: llama.cpp fits the model to the GPU (and RAM) itself, which is what lets a 4 GB card carry the 4B model.
        string args = $"-m \"{files.ModelPath(model)}\" --host 127.0.0.1 --port {_port} -c {AiAssistantPolicy.ServerContext} -np 1 --no-webui --jinja";
        var psi = new ProcessStartInfo(files.ServerExe, args) { WorkingDirectory = files.RuntimeDir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        var p = new Process { StartInfo = psi };
        void Keep(string? line) { if (line is null) return; lock (_tail) { _tail.Enqueue(line); while (_tail.Count > 6) _tail.Dequeue(); } }
        p.OutputDataReceived += (_, e) => Keep(e.Data); p.ErrorDataReceived += (_, e) => Keep(e.Data);
        p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine(); _process = p; Model = model; LastUse = DateTime.UtcNow;
        try
        {
            var limit = DateTime.UtcNow.AddMinutes(3);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (p.HasExited) throw new IOException("llama-server exited with " + p.ExitCode + ": " + string.Join(" | ", Tail()));
                if (DateTime.UtcNow > limit) throw new TimeoutException("The model did not load in time.");
                try { using var r = await http.GetAsync($"http://127.0.0.1:{_port}/health", ct).ConfigureAwait(false); if (r.IsSuccessStatusCode) return; }
                catch (HttpRequestException) { }
                await Task.Delay(400, ct).ConfigureAwait(false);
            }
        }
        catch { Stop(); throw; }
    }

    private string[] Tail() { lock (_tail) return [.. _tail]; }

    /// <summary>One turn: the reply streams through <paramref name="onText"/>; tool calls arrive in pieces and are put together here.</summary>
    public async Task<ChatReply> CompleteAsync(JsonArray messages, JsonArray? tools, bool mustCallTool, Action<string> onText, CancellationToken ct)
    {
        if (!IsRunning) throw new InvalidOperationException("The assistant is not running.");
        LastUse = DateTime.UtcNow;
        var body = new JsonObject
        {
            // Qwen3's own advice for answers without thinking (temperature 0.7, top-p 0.8, top-k 20), a little cooler for tool use, and a presence
            // penalty against the loops a quantized model falls into.
            ["messages"] = JsonNode.Parse(messages.ToJsonString()), ["stream"] = true, ["temperature"] = 0.5, ["top_p"] = 0.8, ["top_k"] = 20, ["presence_penalty"] = 1.0,
            ["max_tokens"] = AiAssistantPolicy.MaxReplyTokens,
            ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },   // Qwen3 would otherwise think aloud first: slow, and not an answer for the customer
        };
        if (tools is { Count: > 0 }) { body["tools"] = JsonNode.Parse(tools.ToJsonString()); if (mustCallTool) body["tool_choice"] = "required"; }
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_port}/v1/chat/completions") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), Encoding.UTF8);
        var text = new StringBuilder(); var calls = new SortedDictionary<int, (string Id, string Name, StringBuilder Args)>();
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            string data = line[5..].Trim();
            if (data == "[DONE]") break;
            var (piece, deltas) = Parse(data);
            if (!string.IsNullOrEmpty(piece)) { LastUse = DateTime.UtcNow; text.Append(piece); onText(piece); if (AiText.Looping(text.ToString())) break; }
            foreach (var d in deltas)
            {
                var c = calls.TryGetValue(d.Index, out var have) ? have : (Id: "", Name: "", Args: new StringBuilder());
                calls[d.Index] = (d.Id ?? c.Id, d.Name ?? c.Name, c.Args.Append(d.Arguments));
            }
        }
        return new(text.ToString(), [.. calls.Values.Where(c => c.Name.Length > 0).Select((c, i) => new ToolCall(c.Id.Length > 0 ? c.Id : "call_" + i, c.Name, c.Args.ToString()))]);
    }

    /// <summary>The text of one streamed chunk (<c>choices[0].delta.content</c>); null for a chunk with none.</summary>
    public static string? Piece(string json) => Parse(json).Text;

    internal readonly record struct CallDelta(int Index, string? Id, string? Name, string? Arguments);

    /// <summary>One streamed chunk: its text and the pieces of tool calls it carries (<c>delta.tool_calls</c>: an index, then a name and argument fragments).</summary>
    internal static (string? Text, IReadOnlyList<CallDelta> Calls) Parse(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            if (!d.RootElement.TryGetProperty("choices", out var c) || c.GetArrayLength() == 0 || !c[0].TryGetProperty("delta", out var delta)) return (null, []);
            string? text = delta.TryGetProperty("content", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            var calls = new List<CallDelta>();
            if (delta.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array)
                foreach (var x in tc.EnumerateArray())
                {
                    x.TryGetProperty("function", out var f);
                    string? Str(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    calls.Add(new(x.TryGetProperty("index", out var i) && i.TryGetInt32(out var n) ? n : 0, Str(x, "id"), Str(f, "name"), Str(f, "arguments")));
                }
            return (text, calls);
        }
        catch (JsonException) { return (null, []); }
    }

    public void Stop()
    {
        var p = _process; _process = null; Model = null;
        if (p is null) return;
        try { if (!p.HasExited) { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        p.Dispose();
    }

    public void Dispose() => Stop();

    private static int FreePort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return port; }
}
