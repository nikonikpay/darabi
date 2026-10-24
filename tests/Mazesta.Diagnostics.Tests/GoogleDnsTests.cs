using Xunit; using Mazesta.Diagnostics.Ai;
namespace Mazesta.Diagnostics.Tests;

public class GoogleDnsTests
{
    [Fact] public void Address_records_are_taken_and_the_shortest_ttl_kept_and_aliases_skipped()
    {
        var (addresses, ttl) = GoogleDns.Parse("""{"Status":0,"Answer":[{"name":"huggingface.co.","type":5,"TTL":300,"data":"cdn.example."},{"name":"cdn.example.","type":1,"TTL":120,"data":"18.155.1.2"},{"name":"cdn.example.","type":1,"TTL":60,"data":"18.155.1.3"}]}""");
        Assert.Equal(["18.155.1.2", "18.155.1.3"], addresses.Select(a => a.ToString())); Assert.Equal(60, ttl);
    }
    [Fact] public void An_answer_with_no_records_gives_none() => Assert.Empty(GoogleDns.Parse("""{"Status":3}""").Addresses);
    [Fact] public async Task An_address_literal_needs_no_lookup() => Assert.Equal("1.2.3.4", (await GoogleDns.ResolveAsync("1.2.3.4", CancellationToken.None))[0].ToString());
}
