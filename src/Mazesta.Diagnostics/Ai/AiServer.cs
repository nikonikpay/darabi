using System.Diagnostics; using System.Net; using System.Net.Http; using System.Net.Sockets; using System.Runtime.CompilerServices; using System.Text; using System.Text.Json;
using Mazesta.Core.Ai;
namespace Mazesta.Diagnostics.Ai;

public readonly record struct ChatTurn(string Role, string Text);

/// <summary>
/// llama.cpp's own <c>llama-server</c> on the loopback address, serving one downloaded model to the chat page. It is started only when the user
/// asks, listens on 127.0.0.1 alone on a free port, and is killed on stop, on idle and on exit. The page never sees the port: replies are
/// streamed through <see cref="ChatAsync"/>.
/// </summary>
public sealed class AiServer(AiFiles files, HttpClient http) : IDisposable
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
        string args = $"-m \"{files.ModelPath(model)}\" --host 127.0.0.1 --port {_port} -c {AiFitter.Context} -np 1 --no-webui --jinja";
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

    /// <summary>The reply's text as it is written, piece by piece.</summary>
    public async IAsyncEnumerable<string> ChatAsync(IReadOnlyList<ChatTurn> turns, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!IsRunning) throw new InvalidOperationException("The assistant is not running.");
        LastUse = DateTime.UtcNow;
        var body = new
        {
            messages = turns.Select(t => new { role = t.Role, content = t.Text }), stream = true, temperature = 0.6, max_tokens = AiAssistantPolicy.MaxReplyTokens,
            chat_template_kwargs = new { enable_thinking = false },   // Qwen3 would otherwise think aloud first: slow, and not an answer for the customer
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_port}/v1/chat/completions") { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), Encoding.UTF8);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            string data = line[5..].Trim();
            if (data == "[DONE]") break;
            string? piece = Piece(data);
            if (!string.IsNullOrEmpty(piece)) { LastUse = DateTime.UtcNow; yield return piece; }
        }
    }

    /// <summary>The text of one streamed chunk (<c>choices[0].delta.content</c>); null for a chunk with none.</summary>
    public static string? Piece(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            return d.RootElement.TryGetProperty("choices", out var c) && c.GetArrayLength() > 0 && c[0].TryGetProperty("delta", out var delta)
                && delta.TryGetProperty("content", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        }
        catch (JsonException) { return null; }
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
