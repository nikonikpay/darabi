namespace Mazesta.Core.Overlay;

/// <summary>The frame rate of the program in front, from the times it presented frames. <see cref="Low1Fps"/> is null until there are enough
/// frames to say what the slowest 1% were.</summary>
public sealed record FrameRateReading(double Fps, double? Low1Fps, double FrameTimeMs, int ProcessId, string? App);

/// <summary>
/// Frame statistics from present times (seconds, ascending). FPS and frame time are over the last second of frames; the 1% low is the rate of
/// the 99th-percentile frame time over the last <see cref="LowWindowSeconds"/> seconds (the common "1% low" of frame-rate tools), given only
/// once there are <see cref="MinFramesForLow"/> frame intervals, so a short burst is never reported as a low. No frames recently: no reading.
/// </summary>
public static class FrameTimeStats
{
    public const double LowWindowSeconds = 10, StaleSeconds = 2.5;
    public const int MinFramesForLow = 100;

    /// <param name="presents">Present times in seconds, ascending.</param>
    /// <param name="now">The current time on the same clock; the newest frame must be within <see cref="StaleSeconds"/> of it (events arrive
    /// about a second late, so "the last second" is measured back from the newest frame, not from now).</param>
    public static FrameRateReading? Compute(IReadOnlyList<double> presents, double now, int processId, string? app)
    {
        if (presents.Count < 2) return null;
        double newest = presents[^1];
        if (now - newest > StaleSeconds) return null;
        int first = presents.Count - 1;
        while (first > 0 && newest - presents[first - 1] <= 1.0) first--;
        if (first == presents.Count - 1) first = presents.Count - 2;   // under 2 fps: the last interval alone
        double span = newest - presents[first];
        if (span <= 0) return null;
        double fps = (presents.Count - 1 - first) / span;

        var intervals = new List<double>();
        for (int i = presents.Count - 1; i > 0 && newest - presents[i - 1] <= LowWindowSeconds; i--) intervals.Add(presents[i] - presents[i - 1]);
        double? low = null;
        if (intervals.Count >= MinFramesForLow)
        {
            intervals.Sort();
            double p99 = intervals[Math.Min(intervals.Count - 1, (int)Math.Ceiling(intervals.Count * 0.99) - 1)];
            if (p99 > 0) low = 1 / p99;
        }
        return new FrameRateReading(fps, low, 1000 / fps, processId, app);
    }
}
