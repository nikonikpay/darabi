using System.Diagnostics; using System.Net; using System.Net.Http; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.Diagnostics.Network;

/// <summary>
/// Internet speed (spec §5.4): latency, jitter and packet loss by ICMP to 1.1.1.1, then download and upload through parallel HTTPS
/// streams to Cloudflare's public speed-test endpoint (speed.cloudflare.com) - a named destination, used only when the technician
/// starts this benchmark, and the data moved is reported with the result. Without a connection it is Unsupported, with numbers left
/// out rather than zero; the other benchmarks never depend on it.
/// </summary>
public sealed class InternetSpeedBenchmark(HttpMessageHandler? transport = null, Func<IPAddress, TimeSpan, CancellationToken, Task<long?>>? echo = null) : IBenchmark
{
    public const string Server = "speed.cloudflare.com";
    public static readonly TestDefinition Spec = new(new TestId("bench.network.internet"), "Bench_Net_Internet", 30);
    public TestDefinition Definition => Spec;
    public HardwareKind Component => HardwareKind.Network;
    private const int Streams = 6, DownloadChunk = 25_000_000, UploadChunk = 4_000_000;
    private static readonly IPAddress PingTarget = IPAddress.Parse("1.1.1.1");
    private readonly Func<IPAddress, TimeSpan, CancellationToken, Task<long?>> _echo = echo ?? NetworkLatencyExecutor.SystemEcho;

    public async Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return BenchmarkResult.Unsupported(Spec.Id, started, "Duration must be positive.");
        using var http = new HttpClient(transport ?? new SocketsHttpHandler { MaxConnectionsPerServer = Streams, AutomaticDecompression = DecompressionMethods.None }, disposeHandler: transport is null) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MazestaTest/1.0");
        double phase = request.DurationSeconds * 0.4, pingSeconds = request.DurationSeconds * 0.2;
        try
        {
            var (rtts, sent) = await PingAsync(pingSeconds, p => request.Report(p * 0.2), ct).ConfigureAwait(false);
            var (down, downBytes) = await TransferAsync(http, upload: false, phase, p => request.Report(0.2 + p * 0.4), ct).ConfigureAwait(false);
            var (up, upBytes) = await TransferAsync(http, upload: true, phase, p => request.Report(0.6 + p * 0.4), ct).ConfigureAwait(false);
            if (downBytes == 0 && upBytes == 0)
                return BenchmarkResult.Unsupported(Spec.Id, started, $"No data could be exchanged with {Server}; {(rtts.Count == 0 ? "no ping reply either - no internet connection" : "ping works, so HTTPS is blocked here")}.");
            List<BenchmarkMetric> metrics = [];
            if (downBytes > 0) metrics.Add(new("Bench_Net_Download", down, "Mbps"));
            if (upBytes > 0) metrics.Add(new("Bench_Net_Upload", up, "Mbps"));
            if (rtts.Count > 0) metrics.AddRange([new("Bench_Net_Ping", rtts.Average(), "ms"), new("Bench_Net_Jitter", NetworkLatencyExecutor.Jitter(rtts), "ms")]);
            metrics.Add(new("Bench_Net_Loss", 100.0 * (sent - rtts.Count) / Math.Max(1, sent), "%"));
            metrics.Add(new("Bench_Net_DataUsed", (downBytes + upBytes) / 1e6, "MB"));
            return new(Spec.Id, BenchmarkStatus.Completed, started, request.Clock.UtcNow, metrics,
                $"HTTPS to {Server}, {Streams} parallel streams, {phase:0} s down then {phase:0} s up; ICMP to {PingTarget} ({sent} echoes); {(downBytes + upBytes) / 1e6:F0} MB transferred; links: {string.Join(", ", NetworkLatencyExecutor.ActiveAdapters())}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return BenchmarkResult.Cancelled(Spec.Id, started, request.Clock.UtcNow); }
    }

    private async Task<(List<double> Rtts, int Sent)> PingAsync(double seconds, Action<double> progress, CancellationToken ct)
    {
        var rtts = new List<double>(); int sent = 0; var sw = Stopwatch.StartNew();
        do { sent++; if (await _echo(PingTarget, TimeSpan.FromSeconds(1), ct).ConfigureAwait(false) is { } ms) rtts.Add(ms); progress(sw.Elapsed.TotalSeconds / seconds); await Task.Delay(200, ct).ConfigureAwait(false); }
        while (sw.Elapsed.TotalSeconds < seconds);
        return (rtts, sent);
    }

    /// <summary>Megabits per second over the phase and the bytes moved. Streams keep requesting until the phase ends; a failed request
    /// ends only its own stream (a flaky link lowers the number instead of voiding the run), and bytes still count up to the moment of failure.</summary>
    private static async Task<(double Mbps, long Bytes)> TransferAsync(HttpClient http, bool upload, double seconds, Action<double> progress, CancellationToken ct)
    {
        long bytes = 0; var sw = Stopwatch.StartNew(); var payload = new byte[UploadChunk]; Random.Shared.NextBytes(payload);
        using var phase = CancellationTokenSource.CreateLinkedTokenSource(ct); phase.CancelAfter(TimeSpan.FromSeconds(seconds));
        async Task Stream()
        {
            var buffer = new byte[81920];
            try
            {
                while (!phase.IsCancellationRequested)
                {
                    if (upload)
                    {
                        using var content = new CountingContent(payload, n => Interlocked.Add(ref bytes, n));
                        using var response = await http.PostAsync($"https://{Server}/__up", content, phase.Token).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                    }
                    else
                    {
                        using var response = await http.GetAsync($"https://{Server}/__down?bytes={DownloadChunk}", HttpCompletionOption.ResponseHeadersRead, phase.Token).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        await using var body = await response.Content.ReadAsStreamAsync(phase.Token).ConfigureAwait(false);
                        for (int n; (n = await body.ReadAsync(buffer, phase.Token).ConfigureAwait(false)) > 0;) Interlocked.Add(ref bytes, n);
                    }
                    progress(sw.Elapsed.TotalSeconds / seconds);
                }
            }
            catch (Exception e) when (e is OperationCanceledException or HttpRequestException or IOException && !ct.IsCancellationRequested) { }
        }
        await Task.WhenAll(Enumerable.Range(0, Streams).Select(_ => Stream())).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return (bytes * 8 / 1e6 / Math.Max(0.001, sw.Elapsed.TotalSeconds), bytes);
    }

    /// <summary>An upload body that reports bytes as they are written to the connection, so a request cut off at the end of the phase still counts what it sent.</summary>
    private sealed class CountingContent(byte[] data, Action<int> sent) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
        {
            for (int offset = 0; offset < data.Length; offset += 65536) { int n = Math.Min(65536, data.Length - offset); await stream.WriteAsync(data.AsMemory(offset, n), ct).ConfigureAwait(false); sent(n); }
        }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override bool TryComputeLength(out long length) { length = data.Length; return true; }
    }
}
