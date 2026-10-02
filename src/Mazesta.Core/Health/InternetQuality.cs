namespace Mazesta.Core.Health;

/// <summary>What an internet connection suits, as Cloudflare's speed test scores it: 1 (bad) to 5 (great) stars each for streaming, gaming and
/// video calls. Null where a figure it needs was not measured.</summary>
public sealed record InternetScores(int? Streaming, int? Gaming, int? VideoCalls);

/// <summary>
/// Cloudflare's "Aggregated Internet Measurement" scoring, as published in its open speed-test library (github.com/cloudflare/speedtest,
/// src/config/internalConfig.ts, MIT): each figure earns points by thresholds, each use sums the points of the figures it depends on, and the sum
/// is placed in one of five classes. The same thresholds, so a result reads the way speed.cloudflare.com would grade it; the measurement is
/// Mazesta's own (Cloudflare also measures packet loss over WebRTC, here it is ICMP).
/// </summary>
public static class InternetQuality
{
    /// <summary>Speeds in Mbps, times in ms, loss as a percentage. <paramref name="loadedPingMs"/> is the latency while the line was busy downloading or
    /// uploading (the larger of the two), from which the increase over the idle latency is taken.</summary>
    public static InternetScores Score(double? downMbps, double? idlePingMs, double? jitterMs, double? lossPercent, double? loadedPingMs)
    {
        // Missing loss counts 0 points, as Cloudflare's own scoring does; the other figures are required.
        int lossPts = lossPercent is { } l ? Step(l / 100, [0.01, 0.05, 0.25, 0.5], [10, 5, 0, -10, -20]) : 0;
        int? latency = idlePingMs is { } p ? Step(p, [10, 20, 50, 100, 500], [20, 10, 5, 0, -10, -20]) : null;
        int? increase = idlePingMs is { } i && loadedPingMs is { } ld ? Step(Math.Max(0, ld - i), [10, 20, 50, 100, 500], [20, 10, 5, 0, -10, -20]) : null;
        int? jitter = jitterMs is { } j ? Step(j, [10, 20, 100, 500], [10, 5, 0, -10, -20]) : null;
        int? down = downMbps is { } d ? Step(d * 1e6, [1e6, 10e6, 50e6, 100e6], [0, 5, 10, 20, 30]) : null;
        return new(
            Stars(latency + lossPts + down + increase, [15, 20, 40, 60]),
            Stars(latency + lossPts + increase, [5, 15, 25, 30]),
            Stars(latency + jitter + lossPts + increase, [5, 15, 25, 40]));
    }

    private static int? Stars(int? points, double[] thresholds) => points is { } p ? Step(Math.Max(0, p), thresholds, [1, 2, 3, 4, 5]) : null;

    private static int Step(double value, double[] domain, int[] range)
    {
        int i = 0;
        while (i < domain.Length && value >= domain[i]) i++;
        return range[i];
    }
}
