using System.Text.Json; using System.Text.Json.Nodes;
namespace Mazesta.Diagnostics.Ai;

/// <summary>A function the model may call. The app owns it: <see cref="Run"/> returns compact JSON of what was really read, and the model only chooses
/// the tool and its arguments. <see cref="ParametersJson"/> is the JSON schema of the arguments.</summary>
public sealed record AiTool(string Name, string Description, string ParametersJson, Func<JsonElement, CancellationToken, Task<string>> Run);

/// <summary>
/// The assistant's loop: the model answers, or asks for tools; the tools run here, their results go back to it, and it answers from them. Bounded
/// (<see cref="MaxRounds"/>), and every result is cut to <see cref="MaxResultChars"/> so a tool can not fill the model's context.
/// </summary>
public static class AiAgent
{
    public const int MaxRounds = 3, MaxResultChars = 1800;

    public static async Task RunAsync(IChatModel model, string system, IReadOnlyList<ChatTurn> history, IReadOnlyList<AiTool> tools, Action<string> onText, Action<string, bool> onTool, CancellationToken ct)
    {
        var messages = new JsonArray { Message("system", system) };
        foreach (var h in history) messages.Add(Message(h.Role, h.Text));
        var defs = new JsonArray();
        foreach (var t in tools) defs.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = JsonNode.Parse(t.ParametersJson) } });
        for (int round = 0; ; round++)
        {
            // After the last round the tools are withdrawn, so the turn has to be an answer.
            var reply = await model.CompleteAsync(messages, round < MaxRounds ? defs : null, onText, ct).ConfigureAwait(false);
            if (reply.Calls.Count == 0 || round >= MaxRounds) return;
            var calls = new JsonArray();
            foreach (var c in reply.Calls) calls.Add(new JsonObject { ["id"] = c.Id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments } });
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = reply.Text, ["tool_calls"] = calls });
            foreach (var c in reply.Calls)
            {
                var (result, ok) = await InvokeAsync(tools, c, ct).ConfigureAwait(false);
                onTool(c.Name, ok);
                messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = c.Id, ["content"] = result.Length > MaxResultChars ? result[..MaxResultChars] + " …(cut)" : result });
            }
        }
    }

    private static JsonObject Message(string role, string text) => new() { ["role"] = role, ["content"] = text };

    /// <summary>Runs one call. A tool that does not exist, arguments that are not JSON, or a tool that fails give an error the model is told about, never a made-up result.</summary>
    internal static async Task<(string Result, bool Ok)> InvokeAsync(IReadOnlyList<AiTool> tools, ToolCall call, CancellationToken ct)
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
