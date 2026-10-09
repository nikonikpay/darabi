using System.Text.Json.Nodes; using Mazesta.Persistence; using Xunit;
namespace Mazesta.Persistence.Tests;

public sealed class UsageLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-usage-" + Guid.NewGuid().ToString("N"));
    private UsageLog Make() => new(AppPaths.Create(_dir));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact] public void The_install_id_is_random_stable_and_nothing_else()
    {
        string id = Make().InstallId;
        Assert.Equal(32, id.Length); Assert.All(id, c => Assert.True(Uri.IsHexDigit(c)));
        Assert.Equal(id, Make().InstallId);   // read back from the file by a new instance
        Assert.DoesNotContain(Environment.MachineName.ToLowerInvariant(), id.ToLowerInvariant());
    }

    [Fact] public void Lines_come_back_in_order_and_only_the_unsent_ones()
    {
        var log = Make();
        log.Append("a", new JsonObject { ["n"] = 1 }); log.Append("b"); log.Append("c", new JsonObject { ["n"] = 3 });
        var all = log.Unsent(10);
        Assert.Equal(["a", "b", "c"], all.Select(l => (string)JsonNode.Parse(l.Json)!["k"]!));
        log.MarkSent(all[1].Seq);
        Assert.Equal(["c"], Make().Unsent(10).Select(l => (string)JsonNode.Parse(l.Json)!["k"]!));   // the mark survives a restart
        Assert.Single(log.Unsent(1));
    }

    [Fact] public void An_event_without_figures_has_no_data_field_and_a_count_is_capped()
    {
        var log = Make(); log.Append("app.start");
        Assert.Null(JsonNode.Parse(log.Unsent(1)[0].Json)!["d"]);
        for (int i = 0; i < 5; i++) log.Append("x");
        Assert.Equal(3, log.Unsent(3).Count);
    }
}
