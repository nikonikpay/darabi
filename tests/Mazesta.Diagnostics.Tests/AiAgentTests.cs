using Xunit; using System.Text.Json.Nodes; using Mazesta.Diagnostics.Ai;
namespace Mazesta.Diagnostics.Tests;

public class AiAgentTests
{
    private sealed class Script(params ChatReply[] replies) : IChatModel
    {
        private int _i; public List<string> Seen { get; } = []; public List<bool> HadTools { get; } = []; public List<bool> Forced { get; } = [];
        public Task<ChatReply> CompleteAsync(JsonArray messages, JsonArray? tools, bool mustCallTool, Action<string> onText, CancellationToken ct)
        { Seen.Add(messages.ToJsonString()); HadTools.Add(tools is not null); Forced.Add(mustCallTool); var r = replies[Math.Min(_i++, replies.Length - 1)]; if (r.Text.Length > 0) onText(r.Text); return Task.FromResult(r); }
    }
    private static readonly AiTool Sensors = new("get_sensors", "d", """{"type":"object","properties":{}}""", (_, _) => Task.FromResult("""{"cpu":61.5}"""));

    [Fact] public async Task A_tool_result_goes_back_to_the_model_which_then_answers()
    {
        var model = new Script(new("", [new ToolCall("c1", "get_sensors", "{}")]), new("CPU is 61.5", []));
        var text = new List<string>(); var ran = new List<(string, bool)>();
        await AiAgent.RunAsync(model, "sys", [new("user", "how hot?")], [Sensors], text.Add, x => ran.Add((x.Name, x.Ok)), default);
        Assert.Equal(["CPU is 61.5"], text); Assert.Equal([("get_sensors", true)], ran);
        Assert.Contains("\"role\":\"tool\"", model.Seen[1]); Assert.Contains("61.5", model.Seen[1]);
    }

    [Fact] public async Task An_unknown_tool_or_bad_arguments_are_reported_to_the_model_not_invented()
    {
        var model = new Script(new("", [new ToolCall("a", "delete_everything", "{}"), new ToolCall("b", "get_sensors", "{oops")]), new("ok", []));
        var ran = new List<(string, bool)>();
        await AiAgent.RunAsync(model, "s", [new("user", "x")], [Sensors], _ => { }, x => ran.Add((x.Name, x.Ok)), default);
        Assert.Equal([("delete_everything", false), ("get_sensors", false)], ran); Assert.Contains("unknown tool", model.Seen[1]); Assert.Contains("not valid JSON", model.Seen[1]);
    }

    [Fact] public async Task A_failing_tool_is_an_error_result()
    {
        var bad = new AiTool("boom", "d", """{"type":"object"}""", (_, _) => throw new InvalidOperationException("no data"));
        var (result, ok) = await AiAgent.InvokeAsync([bad], new("1", "boom", ""), default);
        Assert.False(ok); Assert.Contains("no data", result);
    }

    [Fact] public async Task The_loop_stops_asking_for_tools_after_the_last_round()
    {
        var model = new Script(new ChatReply("", [new ToolCall("c", "get_sensors", "{}")]));   // asks again every time
        await AiAgent.RunAsync(model, "s", [new("user", "x")], [Sensors], _ => { }, _ => { }, default);
        Assert.Equal(AiAgent.MaxRounds + 1, model.Seen.Count); Assert.False(model.HadTools[^1]); Assert.True(model.HadTools[0]);
    }

    [Fact] public async Task A_long_result_is_cut()
    {
        var big = new AiTool("big", "d", """{"type":"object"}""", (_, _) => Task.FromResult(new string('x', 9000)));
        var model = new Script(new("", [new ToolCall("c", "big", "{}")]), new("done", []));
        await AiAgent.RunAsync(model, "s", [new("user", "x")], [big], _ => { }, _ => { }, default);
        Assert.True(model.Seen[1].Length < AiAgent.MaxResultChars + 600);
    }

    [Fact] public async Task An_earlier_answer_goes_back_with_its_tool_calls_and_their_results()
    {
        var model = new Script(new ChatReply("again", []));
        var ran = new ToolExchange("run_tests", """{"areas":["memory"]}""", """{"started":true,"results":[{"outcome":"Passed"}]}""", true);
        await AiAgent.RunAsync(model, "s", [new("user", "test ram"), new("assistant", "RAM passed", [ran]), new("user", "test cpu")], [Sensors], _ => { }, _ => { }, default);
        var sent = model.Seen[0];
        Assert.Contains("\"tool_calls\"", sent); Assert.Contains("run_tests", sent); Assert.Contains("Passed", sent);
        Assert.True(sent.IndexOf("\"role\":\"tool\"", StringComparison.Ordinal) < sent.IndexOf("RAM passed", StringComparison.Ordinal));   // the result comes before the words about it
    }

    [Fact] public async Task A_request_to_act_makes_only_the_first_turn_a_forced_tool_call()
    {
        var model = new Script(new("", [new ToolCall("c", "get_sensors", "{}")]), new("done", []));
        await AiAgent.RunAsync(model, "s", [new("user", "x")], [Sensors], _ => { }, _ => { }, default, mustAct: true);
        Assert.Equal([true, false], model.Forced);
        var plain = new Script(new ChatReply("hi", []));
        await AiAgent.RunAsync(plain, "s", [new("user", "x")], [Sensors], _ => { }, _ => { }, default);
        Assert.Equal([false], plain.Forced);
    }

    [Fact] public void A_message_costs_its_text_and_its_shortened_results()
    {
        var t = new ChatTurn("assistant", "abc", [new ToolExchange("n", "{}", new string('x', 5000), true)]);
        Assert.Equal(3 + AiAgent.HistoryResultChars + 2 + 1, AiAgent.Length(t));
    }

    [Fact] public void Streamed_tool_call_pieces_are_read()
    {
        var (_, calls) = AiServer.Parse("""{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"x","function":{"name":"get_sensors","arguments":"{\"kind\""}}]}}]}""");
        Assert.Equal("get_sensors", calls[0].Name); Assert.Equal("{\"kind\"", calls[0].Arguments); Assert.Equal("x", calls[0].Id);
    }
}
