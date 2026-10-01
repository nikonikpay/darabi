using Xunit; using System.Text.Json; using Mazesta.Core.Ai;
namespace Mazesta.Core.Tests;

public class AiJsonTests
{
    [Fact] public void A_small_result_is_left_as_it_is() => Assert.Equal("""{"a":1}""", AiJson.Shrink("""{"a":1}""", 100));

    [Fact] public void A_long_result_stays_whole_json_keeps_what_failed_and_says_how_many_were_left_out()
    {
        var tests = Enumerable.Range(0, 60).Select(i => new { name = "test " + i, outcome = i == 59 ? "Failed" : "Passed", detail = new string('d', 90) }).ToList();
        string full = JsonSerializer.Serialize(new { started = true, results = tests });
        string s = AiJson.Shrink(full, 2400);
        Assert.True(s.Length <= 2400);
        using var d = JsonDocument.Parse(s);   // still JSON
        var kept = d.RootElement.GetProperty("results").EnumerateArray().ToList();
        Assert.Contains(kept, t => t.GetProperty("outcome").GetString() == "Failed");
        Assert.Equal(60, kept.Count + d.RootElement.GetProperty("resultsOmitted").GetInt32());
    }

    [Fact] public void A_long_text_is_shortened_and_what_is_not_json_is_cut()
    {
        string s = AiJson.Shrink(JsonSerializer.Serialize(new { detail = new string('x', 5000) }), 1000);
        Assert.True(s.Length < 1000); JsonDocument.Parse(s).Dispose();
        Assert.EndsWith("(cut)", AiJson.Shrink(new string('x', 3000), 100));
    }
}
