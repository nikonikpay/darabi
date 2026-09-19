using System.Diagnostics; using System.Net; using System.Net.NetworkInformation; using System.Net.Sockets;
namespace Mazesta.Diagnostics.Network;

/// <summary>
/// Link and latency test (spec §11): ICMP echoes to a chosen target for the duration, then packet loss,
/// latency and jitter. No adapter that is up is <b>Unsupported</b> (there is nothing to test), an adapter
/// that gets no reply is <b>Failed</b>; the other local tests never depend on this one. Nothing is uploaded
/// and the only traffic is the echoes themselves.
/// </summary>
public sealed class NetworkLatencyExecutor(Func<IPAddress, TimeSpan, CancellationToken, Task<long?>>? echo = null, Func<IReadOnlyList<string>>? adapters = null) : ITestExecutor
{
    public const string TargetOption = "target";
    public const double MaxLossPercent = 5;
    public static readonly TestDefinition Definition = new(new TestId("network.latency"), "Test_Network_Latency", 20,
        [new TestOption(TargetOption, "Test_Option_PingTarget", TestOptionKind.Text, "1.1.1.1")]);
    TestDefinition ITestExecutor.Definition => Definition;

    private readonly Func<IPAddress, TimeSpan, CancellationToken, Task<long?>> _echo = echo ?? SystemEcho;
    private readonly Func<IReadOnlyList<string>> _adapters = adapters ?? ActiveAdapters;

    public async Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive.");
        var links = _adapters();
        if (links.Count == 0) return TestRunResult.Unsupported(Definition.Id, started, "No network adapter is connected.");
        string target = (request.Options ?? TestOptions.None(Definition)).Get(TargetOption).Trim();
        IPAddress? address;
        try { address = IPAddress.TryParse(target, out var literal) ? literal : (await Dns.GetHostAddressesAsync(target, ct)).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork); }
        catch (SocketException) { address = null; }
        if (address is null) return TestRunResult.Unsupported(Definition.Id, started, $"'{target}' is not an IP address and did not resolve.");

        var rtts = new List<double>(); int sent = 0; var clock = Stopwatch.StartNew();
        try
        {
            do
            {
                sent++;
                if (await _echo(address, TimeSpan.FromSeconds(1), ct) is { } ms) rtts.Add(ms);
                request.Progress?.Invoke(new TestProgress(Math.Clamp(clock.Elapsed.TotalSeconds / request.DurationSeconds, 0, 1), "Test_Status_Running"));
                await Task.Delay(200, ct);
            }
            while (clock.Elapsed.TotalSeconds < request.DurationSeconds);
        }
        catch (OperationCanceledException) { return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, 0, Describe(links, address, sent, rtts)); }

        double loss = 100.0 * (sent - rtts.Count) / sent;
        return new(Definition.Id, rtts.Count == 0 || loss > MaxLossPercent ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, sent - rtts.Count, Describe(links, address, sent, rtts));
    }

    /// <summary>Jitter as the mean absolute difference of consecutive round trips (RFC 3550 style, without smoothing).</summary>
    internal static double Jitter(IReadOnlyList<double> rtts) => rtts.Count < 2 ? 0 : Enumerable.Range(1, rtts.Count - 1).Average(i => Math.Abs(rtts[i] - rtts[i - 1]));

    private static string Describe(IReadOnlyList<string> links, IPAddress target, int sent, List<double> rtts)
        => $"ICMP to {target}; sent={sent}; lost={100.0 * (sent - rtts.Count) / Math.Max(1, sent):F1}%; "
         + (rtts.Count > 0 ? $"latency min/avg/max {rtts.Min():F0}/{rtts.Average():F1}/{rtts.Max():F0} ms; jitter {Jitter(rtts):F1} ms; " : "no reply; ")
         + "links: " + string.Join(", ", links);

    private static async Task<long?> SystemEcho(IPAddress target, TimeSpan timeout, CancellationToken ct)
    {
        using var ping = new Ping();
        try { var reply = await ping.SendPingAsync(target, timeout, cancellationToken: ct); return reply.Status == IPStatus.Success ? reply.RoundtripTime : null; }
        catch (PingException) { return null; }
    }

    /// <summary>Connected, non-loopback adapters with their link speed - the facts a report needs about the link itself.</summary>
    internal static IReadOnlyList<string> ActiveAdapters() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) && !Mazesta.Core.Hardware.NetworkAdapterFilter.IsVirtualBinding(n.Name))
        .Select(n => $"{n.Name} ({n.Speed / 1_000_000} Mbps)").ToList();
}
