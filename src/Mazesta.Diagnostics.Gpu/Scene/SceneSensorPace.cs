using Mazesta.Monitoring;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>While a scene run shows its readout, the monitor reads the sensors twice a second (the readout's figures are the monitor's latest reading, and at the app's usual
/// two seconds they would lag the picture); the interval it had is put back when the run ends. Left alone when the monitor already reads that often.</summary>
internal sealed class SceneSensorPace : IDisposable
{
    private static readonly TimeSpan Half = TimeSpan.FromMilliseconds(500);
    private readonly PollingEngine? _engine; private readonly TimeSpan _before;

    public SceneSensorPace(PollingEngine? engine, bool shown)
    {
        if (engine is null || !shown || engine.FastInterval <= Half) return;
        _engine = engine; _before = engine.FastInterval; engine.SetFastInterval(Half);
    }

    public void Dispose() { if (_engine is { } e && e.FastInterval == Half) e.SetFastInterval(_before); }
}
