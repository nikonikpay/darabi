using System.Net; using System.Net.Http; using Xunit; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Network; using Mazesta.Diagnostics.Tests.Fakes;
namespace Mazesta.Diagnostics.Tests;

public class InternetSpeedTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
    private static TestExecutionRequest Request(int seconds = 2) => new(seconds, new FakeClock(T0), null, null);

    /// <summary>Answers every download with 1 MB and accepts every upload; or fails every request (no internet).</summary>
    private sealed class FakeServer(bool reachable) : HttpMessageHandler
    {
        public List<string> Hosts { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Hosts) Hosts.Add(request.RequestUri!.Host);
            await Task.Delay(20, ct);
            if (!reachable) throw new HttpRequestException("No such host is known.");
            if (request.Content is not null) await request.Content.CopyToAsync(Stream.Null, ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(request.Method == HttpMethod.Get ? new byte[1_000_000] : []) };
        }
    }

    [Fact] public async Task Measures_download_upload_latency_and_the_data_it_used_and_talks_only_to_the_named_server()
    {
        var server = new FakeServer(reachable: true);
        var r = await new InternetSpeedBenchmark(server, (_, _, _) => Task.FromResult<long?>(12)).RunAsync(Request(), CancellationToken.None);
        Assert.Equal(BenchmarkStatus.Completed, r.Status);
        foreach (var key in new[] { "Bench_Net_Download", "Bench_Net_Upload", "Bench_Net_Ping", "Bench_Net_DataUsed" }) Assert.True(r.Metrics.Single(m => m.Key == key).Value > 0, key);
        Assert.Equal(0, r.Metrics.Single(m => m.Key == "Bench_Net_Loss").Value);
        Assert.All(server.Hosts, h => Assert.Equal(InternetSpeedBenchmark.Server, h));
    }
    [Fact] public async Task Without_a_connection_it_is_Unsupported_with_no_numbers()
    {
        var r = await new InternetSpeedBenchmark(new FakeServer(reachable: false), (_, _, _) => Task.FromResult<long?>(null)).RunAsync(Request(), CancellationToken.None);
        Assert.Equal(BenchmarkStatus.Unsupported, r.Status); Assert.Empty(r.Metrics); Assert.Contains("no internet", r.Detail);
    }
    [Fact] public async Task A_cancelled_run_returns_no_numbers()
    {
        using var cts = new CancellationTokenSource(); cts.CancelAfter(100);
        var r = await new InternetSpeedBenchmark(new FakeServer(true), (_, _, _) => Task.FromResult<long?>(12)).RunAsync(Request(5), cts.Token);
        Assert.Equal(BenchmarkStatus.Cancelled, r.Status); Assert.Empty(r.Metrics);
    }
}
