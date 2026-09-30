using Xunit; using Mazesta.Diagnostics.Network; using Mazesta.Diagnostics.Tests.Fakes;
namespace Mazesta.Diagnostics.Tests;

public class LanTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
    private static TestExecutionRequest Request(int seconds, string peer) => new(seconds, new FakeClock(T0), null, null, new TestOptions(LanExecutor.Definition, new Dictionary<string, string> { [LanExecutor.PeerOption] = peer }));

    [Fact] public void The_pattern_checks_clean_whatever_the_pieces_it_arrives_in_and_counts_damaged_bytes()
    {
        var data = new byte[10_007]; new LanPattern(42).Fill(data);
        var check = new LanPattern(42); long bad = 0; int at = 0;
        foreach (int piece in new[] { 3, 5, 8, 1000, 13, 8978 }) { bad += check.Check(data.AsSpan(at, piece)); at += piece; }
        Assert.Equal(0, bad); Assert.Equal(data.Length, check.Position);
        data[17] ^= 1; data[5000] ^= 0x80; data[5001] ^= 0x80;
        Assert.Equal(3, new LanPattern(42).Check(data)); Assert.NotEqual(0, new LanPattern(43).Check(data.AsSpan(0, 64)));
    }

    [Theory, InlineData("192.168.1.20", "192.168.1.20:47315"), InlineData("10.0.0.5:6000", "10.0.0.5:6000")]
    public void Addresses_are_ipv4_with_an_optional_port(string text, string endpoint) => Assert.Equal(endpoint, LanExecutor.Parse(text)!.ToString());
    [Theory, InlineData("pc-2"), InlineData("10.0.0.5:0"), InlineData("::1")]
    public void Anything_else_is_refused(string text) => Assert.Null(LanExecutor.Parse(text));

    [Fact] public async Task A_session_with_a_real_partner_over_loopback_passes_with_both_directions_measured()
    {
        await using var partner = new LanPeerServer(port: 0);
        var r = await new LanExecutor().RunAsync(Request(3, $"127.0.0.1:{partner.BoundPort}"), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, r.Outcome); Assert.Equal(0, r.ErrorCount);
        Assert.Matches(@"upload \d+ Mbit/s .* 0 wrong\), download \d+ Mbit/s .* 0 wrong\)", r.Detail);
        Assert.Contains("while downloading median", r.Detail); Assert.True(partner.Sessions >= 4);
    }

    [Fact] public async Task No_partner_is_Unsupported_and_no_address_too()
    {
        Assert.Equal(TestOutcome.Unsupported, (await new LanExecutor().RunAsync(Request(3, "127.0.0.1:1"), CancellationToken.None)).Outcome);
        Assert.Equal(TestOutcome.Unsupported, (await new LanExecutor().RunAsync(Request(3, ""), CancellationToken.None)).Outcome);
    }
}
