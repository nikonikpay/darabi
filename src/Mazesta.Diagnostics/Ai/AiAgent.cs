using System.Text.Json; using System.Text.Json.Nodes;
namespace Mazesta.Diagnostics.Ai;

/// <summary>A function the model may call. The app owns it: <see cref="Run"/> returns compact JSON of what was really read, and the model only chooses
/// the tool and its arguments. <see cref="ParametersJson"/> is the JSON schema of the arguments.</summary>
public sealed record AiTool(string Name, string Description, string ParametersJson, Func<JsonElement, CancellationToken, Task<string>> Run);

/// <summary>
/// The assistant's loop: the model answers, or asks for tools; the tools run here, their results go back to it, and it answers from them. Bounded
/// (<see cref="MaxRounds"/>, <see cref="MaxCallsPerRound"/>), and what the model is sent of a result is made to fit <see cref="MaxResultChars"/>
/// as whole JSON (<see cref="Mazesta.Core.Ai.AiJson"/>) so a tool can not fill its context; the page and the chat keep the full result. An earlier
/// answer goes back to the model with the tool calls it made and their results (smaller, <see cref="HistoryResultChars"/>), as they happened.
/// </summary>
public static class AiAgent
{
    public const int MaxRounds = 3, MaxCallsPerRound = 4, MaxResultChars = 2400, HistoryResultChars = 500;

    /// <param name="mustAct">The first turn has to call a tool (the user asked for an action, see <c>AiAssistantPolicy.AsksToAct</c>).</param>
    /// <param name="first">Calls the app made for the model, from what the message plainly asks (see <c>AppGuide.Route</c>): they run first, as if
    /// the model had asked for them, and the model then only answers from their results, with no tools of its own.</param>
    public static async Task RunAsync(IChatModel model, string system, IReadOnlyList<ChatTurn> history, IReadOnlyList<AiTool> tools, Action<string> onText, Action<ToolExchange> onTool,
        CancellationToken ct, bool mustAct = false, IReadOnlyList<ToolCall>? first = null)
    {
        var messages = new JsonArray { Message("system", system) }; int n = 0;
        foreach (var h in history)
        {
            if (h.Role == "assistant" && h.Tools is { Count: > 0 } done)
            {
                var ids = done.Select(_ => "h" + n++).ToList(); var past = new JsonArray();
                for (int i = 0; i < done.Count; i++) past.Add(new JsonObject { ["id"] = ids[i], ["type"] = "function", ["function"] = new JsonObject { ["name"] = done[i].Name, ["arguments"] = done[i].Arguments } });
                messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = "", ["tool_calls"] = past });
                for (int i = 0; i < done.Count; i++) messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = ids[i], ["content"] = Mazesta.Core.Ai.AiJson.Shrink(done[i].Result, HistoryResultChars) });
                if (h.Text.Length == 0) continue;
            }
            messages.Add(Message(h.Role, h.Role == "assistant" ? Cut(h.Text, Mazesta.Core.Ai.AiAssistantPolicy.HistoryReplyChars) : h.Text));
        }
        var defs = new JsonArray();
        foreach (var t in tools) defs.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = JsonNode.Parse(t.ParametersJson) } });
        if (first is { Count: > 0 })
        {
            await CallAsync(first).ConfigureAwait(false);
            await model.CompleteAsync(messages, null, false, onText, ct).ConfigureAwait(false);
            return;
        }
        for (int round = 0; ; round++)
        {
            // After the last round the tools are withdrawn, so the turn has to be an answer.
            var reply = await model.CompleteAsync(messages, round < MaxRounds ? defs : null, mustAct && round == 0, onText, ct).ConfigureAwait(false);
            if (reply.Calls.Count == 0 || round >= MaxRounds) return;
            await CallAsync(reply.Calls, reply.Text).ConfigureAwait(false);
        }

        async Task CallAsync(IReadOnlyList<ToolCall> todo, string text = "")
        {
            todo = [.. todo.Take(MaxCallsPerRound)];
            var calls = new JsonArray();
            foreach (var c in todo) calls.Add(new JsonObject { ["id"] = c.Id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments } });
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = text, ["tool_calls"] = calls });
            foreach (var c in todo)
            {
                var (result, ok) = await InvokeAsync(tools, c, ct).ConfigureAwait(false);
                onTool(new(c.Name, c.Arguments, result, ok));
                messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = c.Id, ["content"] = Mazesta.Core.Ai.AiJson.Shrink(result, MaxResultChars) });
            }
        }
    }

    private static JsonObject Message(string role, string text) => new() { ["role"] = role, ["content"] = text };
    private static string Cut(string s, int max) => s.Length > max ? s[..max] + " …(cut)" : s;

    /// <summary>What a message costs in the history, in characters: its text and its tools' results as they would be sent back.</summary>
    public static int Length(ChatTurn t) => t.Text.Length + (t.Tools?.Sum(x => Math.Min(x.Result.Length, HistoryResultChars) + x.Arguments.Length + x.Name.Length) ?? 0);

    /// <summary>Runs one call. A tool that does not exist, arguments that are not JSON, or a tool that fails give an error the model is told about, never a made-up result.</summary>
    public static async Task<(string Result, bool Ok)> InvokeAsync(IReadOnlyList<AiTool> tools, ToolCall call, CancellationToken ct)
    {
        var tool = tools.FirstOrDefault(t => t.Name == call.Name);
        if (tool is null) return (Error("unknown tool"), false);
        JsonElement args;
        try { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments); args = d.RootElement.Clone(); }
        catch (JsonException) { return (Error("the arguments are not valid JSON"), false); }
        try { return (await tool.Run(args, ct).ConfigureAwait(false), true); }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is not OutOfMemoryException) { return (Error("the tool failed: " + e.Message), false); }
    }

    private static string Error(string why) => new JsonObject { ["error"] = why }.ToJsonString();
}
