using System.Globalization; using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.Diagnostics.Network;

/// <summary>
/// The internet speed as a test: the Benchmarks page's measurement (download and upload over HTTPS to speed.cloudflare.com, ping, jitter, loss,
/// latency under load and Cloudflare's grading), so a technician running the network tests sees the speed too. It passes when data was really
/// moved both ways - a working connection, measured; a speed is not judged good or bad here, it is shown. No data moved is Unsupported (no internet,
/// or HTTPS blocked), never a pass. The connection is used only when this test is chosen; the data it moved is reported.
/// </summary>
public sealed class InternetSpeedExecutor(InternetSpeedBenchmark? benchmark = null) : ITestExecutor
{
    public static readonly TestDefinition Definition = new(new TestId("network.speed"), "Test_Network_Speed", 30);
    TestDefinition ITestExecutor.Definition => Definition;
    private readonly InternetSpeedBenchmark _bench = benchmark ?? new();

    public async Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        request.Note("Log_NetSpeed_Start", $"HTTPS https://{InternetSpeedBenchmark.Server}/__down and /__up, parallel streams; ICMP to 1.1.1.1");
        var r = await _bench.RunAsync(request, ct).ConfigureAwait(false);
        double? M(string key) => r.Metrics.FirstOrDefault(m => m.Key == key)?.Value;
        string F(double v) => v.ToString(v >= 100 ? "F0" : "F1", CultureInfo.InvariantCulture);
        switch (r.Status)
        {
            case BenchmarkStatus.Cancelled: return TestRunResult.Cancelled(Definition.Id, r.StartedAt, r.FinishedAt);
            case BenchmarkStatus.Completed when M("Bench_Net_Download") is { } down && M("Bench_Net_Upload") is { } up:
                request.Note("Log_NetSpeed_Result", null, F(down), F(up), M("Bench_Net_Ping") is { } p ? F(p) : "—", M("Bench_Net_Jitter") is { } j ? F(j) : "—");
                if (M("Bench_Net_Score_Gaming") is { } g)
                    request.Note("Log_NetSpeed_Scores", "Cloudflare AIM scoring (github.com/cloudflare/speedtest)", M("Bench_Net_Score_Streaming"), g, M("Bench_Net_Score_Calls"));
                return new(Definition.Id, TestOutcome.Passed, r.StartedAt, r.FinishedAt, 0,
                    $"download {F(down)} Mbps; upload {F(up)} Mbps; " + string.Join("; ", r.Metrics.Where(m => m.Key is not ("Bench_Net_Download" or "Bench_Net_Upload"))
                        .Select(m => $"{m.Key.Replace("Bench_Net_", "", StringComparison.Ordinal).ToLowerInvariant()} {F(m.Value)} {m.Unit}")) + "; " + r.Detail);
            case BenchmarkStatus.Completed:
                // Data went one way only: the connection is half working, which is a fault worth showing, not a pass.
                return new(Definition.Id, TestOutcome.Failed, r.StartedAt, r.FinishedAt, 1, "data moved one way only; " + r.Detail);
            default: return TestRunResult.Unsupported(Definition.Id, r.StartedAt, r.Detail ?? "no internet connection");
        }
    }
}
