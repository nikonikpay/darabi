namespace Mazesta.Diagnostics.Gpu;

/// <summary>
/// How much work goes into one GPU submission. After each submission the host waits for it, reads results back and checks a sample on the
/// CPU, and the GPU sits idle meanwhile. A fixed small batch left a fast card idle for a large share of the time: 16 dispatches took about
/// 6 ms on an RTX 3090 against 2-3 ms of readback and checking, so the card showed about 70 % load during tuning. The batch doubles while a
/// submission is shorter than half the target and halves when it runs past twice the target, so the host's share stays a few percent on a
/// fast card and a slow card still gets short submissions (far from the driver's two-second timeout).
/// </summary>
internal sealed class BatchSizer(int initial = 16, int min = 1, int max = 4096)
{
    public int Count { get; private set; } = Math.Clamp(initial, min, max);

    public void Record(TimeSpan took, TimeSpan target)
    {
        if (took < target / 2) Count = Math.Min(max, Count * 2);
        else if (took > target * 2) Count = Math.Max(min, Count / 2);
    }
}
