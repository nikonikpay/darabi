namespace Mazesta.Core.Overlay;

/// <summary>The frame rate of the program in front, from the times it presented frames. <see cref="Low1Fps"/> is null until there are enough
/// frames to say what the slowest 1% were.</summary>
public sealed record FrameRateReading(double Fps, double? Low1Fps, double FrameTimeMs, int ProcessId, string? App, double? Low01Fps = null);

/// <summary>
/// Frame statistics from present times (seconds, ascending). FPS and frame time are over the last second of frames; the 1% low is the rate of
/// the 99th-percentile frame time over the last <see cref="LowWindowSeconds"/> seconds (the common "1% low" of frame-rate tools), given only
/// once there are <see cref="MinFramesForLow"/> frame intervals, so a short burst is never reported as a low. No frames recently: no reading.
/// </summary>
public static class FrameTimeStats
{
    public const double LowWindowSeconds = 10, StaleSeconds = 2.5;
    public const int MinFramesForLow = 100;
    /// <summary>The 0.1% low (the 99.9th-percentile frame time) looks back further and needs a thousand frame intervals before it is given: a
    /// single hitch in a short window would otherwise be the whole "0.1%".</summary>
    public const double Low01WindowSeconds = 30;
    public const int MinFramesForLow01 = 1000;

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

        double? low = Low(presents, newest, LowWindowSeconds, MinFramesForLow, 0.99), low01 = Low(presents, newest, Low01WindowSeconds, MinFramesForLow01, 0.999);
        return new FrameRateReading(fps, low, 1000 / fps, processId, app, low01);
    }

    private static double? Low(IReadOnlyList<double> presents, double newest, double window, int minFrames, double percentile)
    {
        var intervals = new List<double>();
        for (int i = presents.Count - 1; i > 0 && newest - presents[i - 1] <= window; i--) intervals.Add(presents[i] - presents[i - 1]);
        if (intervals.Count < minFrames) return null;
        intervals.Sort();
        double p = intervals[Math.Min(intervals.Count - 1, (int)Math.Ceiling(intervals.Count * percentile) - 1)];
        return p > 0 ? 1 / p : null;
    }
}
