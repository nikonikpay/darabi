namespace Mazesta.Diagnostics.Storage;

/// <summary>
/// Every request's latency over a whole run in logarithmic buckets 5 % wide (1 µs to about 100 s), so percentiles describe the entire run,
/// not only its last few thousand requests, in a fixed few kilobytes. A percentile is the upper edge of its bucket: at most 5 % above the true
/// value, never below it. The count, mean and maximum are exact. P99.9 needs at least 1000 requests, otherwise it is not given.
/// </summary>
public sealed class LatencyHistogram
{
    private const double Growth = 1.05, MinMicroseconds = 1;
    private static readonly double LogGrowth = Math.Log(Growth);
    private readonly long[] _buckets = new long[380];
    private double _sumMs;

    public long Count { get; private set; }
    public double MaxMs { get; private set; }
    public double MeanMs => Count == 0 ? double.NaN : _sumMs / Count;

    public void Add(double ms)
    {
        if (!double.IsFinite(ms) || ms < 0) return;
        double us = Math.Max(MinMicroseconds, ms * 1000);
        int i = Math.Min(_buckets.Length - 1, (int)Math.Ceiling(Math.Log(us / MinMicroseconds) / LogGrowth));
        _buckets[i]++; Count++; _sumMs += ms; MaxMs = Math.Max(MaxMs, ms);
    }

    /// <summary>The latency <paramref name="q"/> of all requests stayed at or under (0 &lt; q ≤ 1), in ms; NaN when there are too few requests to say.</summary>
    public double Percentile(double q)
    {
        if (Count == 0 || q is <= 0 or > 1 || (q > 0.999 - 1e-9 && Count < 1000)) return double.NaN;
        long target = (long)Math.Ceiling(q * Count), seen = 0;
        for (int i = 0; i < _buckets.Length; i++)
        {
            seen += _buckets[i];
            if (seen >= target) return Math.Min(MaxMs, MinMicroseconds * Math.Pow(Growth, i) / 1000);
        }
        return MaxMs;
    }

    /// <summary>"mean 0.081 ms, P50 0.07, P99 0.21, P99.9 0.9, max 3.2 ms (n=12345)", leaving out what cannot be said.</summary>
    public string Describe()
    {
        if (Count == 0) return "no requests";
        string P(string name, double q) => Percentile(q) is var v && double.IsFinite(v) ? $", {name} {v:F3}" : "";
        return $"mean {MeanMs:F3} ms{P("P50", 0.5)}{P("P95", 0.95)}{P("P99", 0.99)}{P("P99.9", 0.999)}, max {MaxMs:F3} ms (n={Count})";
    }
}
