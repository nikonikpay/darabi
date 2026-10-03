namespace Mazesta.Core.Overlay;

/// <summary>The link as the last echoes showed it. Each figure is null until enough echoes exist to give it: a ping needs one reply, a loss
/// <see cref="PingWindow.MinForLoss"/> echoes sent, a jitter two replies in a row.</summary>
public sealed record PingReading(double? PingMs, double? LossPercent, double? JitterMs);

/// <summary>
/// The last echoes sent to one address (a lost one is kept as null), and what they say now: the newest reply's time, the share that got no
/// reply, and the jitter - the mean difference between replies that followed each other, as the network latency test computes it.
/// </summary>
public sealed class PingWindow(int size = 30)
{
    public const int MinForLoss = 5;
    private readonly Queue<double?> _echoes = new();

    public void Add(double? roundTripMs) { _echoes.Enqueue(roundTripMs); while (_echoes.Count > size) _echoes.Dequeue(); }
    public void Clear() => _echoes.Clear();

    public PingReading Read()
    {
        double?[] all = [.. _echoes];
        double? loss = all.Length >= MinForLoss ? 100.0 * all.Count(e => e is null) / all.Length : null;
        var steps = new List<double>();
        for (int i = 1; i < all.Length; i++) if (all[i] is { } now && all[i - 1] is { } before) steps.Add(Math.Abs(now - before));
        return new(all.Length > 0 ? all[^1] : null, loss, steps.Count > 0 ? steps.Average() : null);
    }
}
