using System.Buffers.Binary; using System.Diagnostics; using System.Globalization; using System.Net; using System.Net.Sockets;
namespace Mazesta.Diagnostics.Network;

/// <summary>
/// LAN test against a partner computer running the app's LAN partner (<see cref="LanPeerServer"/>): the round-trip time at rest, then TCP
/// throughput out and in, each for two fifths of the run, with the round-trip time measured again while the download fills the link (latency
/// under load: a large rise is a queue in the router or the adapter's driver). Every byte in both directions is checked against the agreed
/// pattern (<see cref="LanPattern"/>); one damaged byte, or an upload the partner did not receive in full, fails the test. A partner that cannot
/// be reached is Unsupported; a connection lost half way is Inconclusive (the partner may simply have been closed), never a pass.
/// </summary>
public sealed class LanExecutor : ITestExecutor
{
    public const string PeerOption = "peer";
    public static readonly TestDefinition Definition = new(new TestId("network.lan"), "Test_Network_Lan", 30,
        [new TestOption(PeerOption, "Test_Option_LanPeer", TestOptionKind.Text, "")]);
    TestDefinition ITestExecutor.Definition => Definition;

    public async Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds < 3) return TestRunResult.Unsupported(Definition.Id, started, "The LAN test needs at least 3 seconds.");
        string text = (request.Options ?? TestOptions.None(Definition)).Get(PeerOption).Trim();
        if (text.Length == 0) return TestRunResult.Unsupported(Definition.Id, started, "No LAN partner was given: start the LAN partner on another computer and enter its address.");
        var endpoint = Parse(text);
        if (endpoint is null) return TestRunResult.Unsupported(Definition.Id, started, $"'{text}' is not an IPv4 address (optionally with :port).");

        var total = TimeSpan.FromSeconds(request.DurationSeconds); var clock = Stopwatch.StartNew(); uint seed = (uint)Environment.TickCount;
        void Progress() => request.Progress?.Invoke(new TestProgress(Math.Clamp(clock.Elapsed / total, 0, 1), "Test_Status_Running"));
        request.Note("Log_Lan_Start", "TCP to the partner: echo 64 B (rest) → send pattern (up) → receive pattern + echo (down, loaded)   check: every byte == splitmix64(seed + k/8)", endpoint.ToString());
        List<double> idle, loaded; double up, down; long sent, received, badUp, badDown, gotUp;
        try
        {
            using (var echo = await Connect(endpoint, LanMode.Echo, seed, ct).ConfigureAwait(false))
                idle = await Echo(echo, total * 0.2, Progress, ct).ConfigureAwait(false);
            request.Note("Log_Lan_Idle", null, Median(idle));

            using (var sink = await Connect(endpoint, LanMode.Receive, seed, ct).ConfigureAwait(false))
            {
                var pattern = new LanPattern(seed); var buffer = new byte[LanPeerServer.ChunkBytes]; var sw = Stopwatch.StartNew(); var stream = sink.GetStream();
                while (sw.Elapsed < total * 0.4) { ct.ThrowIfCancellationRequested(); pattern.Fill(buffer); await stream.WriteAsync(buffer, ct).ConfigureAwait(false); Progress(); }
                sent = pattern.Position; double seconds = sw.Elapsed.TotalSeconds;
                sink.Client.Shutdown(SocketShutdown.Send);
                var answer = new byte[16]; await stream.ReadExactlyAsync(answer, ct).ConfigureAwait(false);
                gotUp = BinaryPrimitives.ReadInt64LittleEndian(answer); badUp = BinaryPrimitives.ReadInt64LittleEndian(answer.AsSpan(8));
                up = sent * 8 / seconds / 1e6;
            }
            request.Note("Log_Lan_Up", null, up.ToString("F0", CultureInfo.InvariantCulture), gotUp, badUp);

            using (var source = await Connect(endpoint, LanMode.Send, seed ^ 0x5A5A5A5A, ct).ConfigureAwait(false))
            using (var echo = await Connect(endpoint, LanMode.Echo, seed, ct).ConfigureAwait(false))
            {
                var window = total * 0.4; var sw = Stopwatch.StartNew();
                var echoing = Echo(echo, window, null, ct);
                var pattern = new LanPattern(seed ^ 0x5A5A5A5A); var buffer = new byte[LanPeerServer.ChunkBytes]; var stream = source.GetStream(); badDown = 0;
                while (sw.Elapsed < window) { ct.ThrowIfCancellationRequested(); int n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false); if (n == 0) throw new EndOfStreamException("the partner closed the connection"); badDown += pattern.Check(buffer.AsSpan(0, n)); Progress(); }
                received = pattern.Position; down = received * 8 / sw.Elapsed.TotalSeconds / 1e6;
                loaded = await echoing.ConfigureAwait(false);
            }
            request.Note("Log_Lan_Down", null, down.ToString("F0", CultureInfo.InvariantCulture), received, badDown, Median(loaded));
        }
        catch (OperationCanceledException) { return TestRunResult.Cancelled(Definition.Id, started, request.Clock.UtcNow); }
        catch (SocketException e) when (clock.Elapsed < TimeSpan.FromSeconds(6) && e.SocketErrorCode is SocketError.ConnectionRefused or SocketError.TimedOut or SocketError.HostUnreachable or SocketError.NetworkUnreachable)
        {
            return TestRunResult.Unsupported(Definition.Id, started, $"The LAN partner at {endpoint} did not answer ({e.SocketErrorCode}): is the partner switched on there, and does its firewall let port {endpoint.Port} in?");
        }
        catch (InvalidDataException e) { return new(Definition.Id, TestOutcome.Failed, started, request.Clock.UtcNow, 1, $"LAN test with {endpoint}: {e.Message}."); }
        catch (Exception e) when (e is IOException or SocketException or EndOfStreamException)
        {
            return new(Definition.Id, TestOutcome.Inconclusive, started, request.Clock.UtcNow, 0, $"The connection to {endpoint} was lost during the test ({e.Message}); nothing is concluded about the network from a run that did not finish.");
        }
        long errors = badUp + badDown + (gotUp != sent ? 1 : 0);
        string detail = string.Create(CultureInfo.InvariantCulture,
            $"LAN test with {endpoint}: upload {up:F0} Mbit/s ({sent / 1e6:F0} MB sent, {gotUp / 1e6:F0} MB received by the partner, {badUp} wrong), download {down:F0} Mbit/s ({received / 1e6:F0} MB, {badDown} wrong); "
            + $"round trip at rest {Describe(idle)}, while downloading {Describe(loaded)}")
            + (gotUp != sent ? "; the partner received a different amount than was sent" : "");
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, errors, detail);
    }

    public static IPEndPoint? Parse(string text)
    {
        string host = text; int port = LanPeerServer.Port;
        int colon = text.LastIndexOf(':');
        if (colon > 0) { if (!int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535) return null; host = text[..colon]; }
        return IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork ? new IPEndPoint(ip, port) : null;
    }

    private static async Task<TcpClient> Connect(IPEndPoint endpoint, LanMode mode, uint seed, CancellationToken ct)
    {
        var client = new TcpClient { NoDelay = true };
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try { await client.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { client.Dispose(); throw new SocketException((int)SocketError.TimedOut); }
        }
        var hello = new byte[LanPeerServer.HelloBytes]; LanPeerServer.Magic.CopyTo(hello); hello[8] = (byte)mode; BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(12), seed);
        await client.GetStream().WriteAsync(hello, ct).ConfigureAwait(false);
        return client;
    }

    /// <summary>Round trips of a 64-byte message, one every 50 ms for <paramref name="length"/>, in milliseconds.</summary>
    private static async Task<List<double>> Echo(TcpClient client, TimeSpan length, Action? progress, CancellationToken ct)
    {
        var stream = client.GetStream(); var message = new byte[LanPeerServer.EchoBytes]; var back = new byte[LanPeerServer.EchoBytes]; var rtts = new List<double>(); var sw = Stopwatch.StartNew();
        while (sw.Elapsed < length)
        {
            Random.Shared.NextBytes(message); long t = Stopwatch.GetTimestamp();
            await stream.WriteAsync(message, ct).ConfigureAwait(false); await stream.ReadExactlyAsync(back, ct).ConfigureAwait(false);
            rtts.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
            if (!back.AsSpan().SequenceEqual(message)) throw new InvalidDataException("an echo came back different from what was sent");
            progress?.Invoke(); await Task.Delay(50, ct).ConfigureAwait(false);
        }
        return rtts;
    }

    private static double Median(List<double> x) => x.Count == 0 ? double.NaN : x.Order().ElementAt(x.Count / 2);
    private static string Describe(List<double> x) => x.Count == 0 ? "not measured"
        : string.Create(CultureInfo.InvariantCulture, $"median {Median(x):F2} ms, P95 {x.Order().ElementAt(Math.Min(x.Count - 1, (int)(x.Count * 0.95))):F2} ms (n={x.Count})");
}
