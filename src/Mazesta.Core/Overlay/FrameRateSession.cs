namespace Mazesta.Core.Overlay;

/// <summary>
/// The average, lowest and highest frame rate of the program in front since the overlay started watching it: over the per-second rates the
/// overlay showed, one per update. A different program in front starts afresh, so a game's numbers never mix with the desktop's; an update with
/// no reading (nothing presenting, a loading screen that stopped drawing) is left out, never counted as zero.
/// </summary>
public sealed class FrameRateSession
{
    private int _process = -1; private double _sum; private int _count;
    public double? Average => _count > 0 ? _sum / _count : null;
    public double? Min { get; private set; }
    public double? Max { get; private set; }
    public int Samples => _count;

    public void Add(FrameRateReading? reading)
    {
        if (reading is null || !double.IsFinite(reading.Fps)) return;
        if (reading.ProcessId != _process) { Reset(); _process = reading.ProcessId; }
        _sum += reading.Fps; _count++;
        Min = Min is { } lo ? Math.Min(lo, reading.Fps) : reading.Fps;
        Max = Max is { } hi ? Math.Max(hi, reading.Fps) : reading.Fps;
    }

    public void Reset() { _process = -1; _sum = 0; _count = 0; Min = Max = null; }
}
