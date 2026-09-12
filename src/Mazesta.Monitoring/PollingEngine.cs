using System.Diagnostics; using Mazesta.Core.Hardware; using Mazesta.Core.Time; using Mazesta.Hardware;
namespace Mazesta.Monitoring;
public enum EngineState { Stopped, Running, Paused, Failed }
public sealed class PollingEngine : IDisposable
{
    public const string KeyPollOverrun = "Engine.PollOverrun", KeyEngineFailed = "Engine.Failed", KeySubscriberFailed = "Engine.SubscriberFailed";
    private readonly IClock _clock; private readonly MonitoringOptions _options; private readonly IEventLog _events;
    private readonly Dictionary<HardwareId, DateTimeOffset> _nextDue = []; private readonly object _lock = new();
    private readonly ManualResetEventSlim _wake = new(false); private Thread? _thread; private volatile bool _stopRequested; private bool _providerStarted;
    private long _sequence; private DateTimeOffset _lastOverrunLog = DateTimeOffset.MinValue; private volatile EngineState _state = EngineState.Stopped;
    public ISensorProvider Provider { get; } public HistoryStore History { get; } public SensorStatistics Statistics { get; }
    public TimeSpan FastInterval { get; private set; }
    public IReadOnlyList<HardwareNode> Hardware => Provider.Hardware;
    public EngineState State { get => _state; private set { if (_state == value) return; _state = value; StateChanged?.Invoke(value); } }
    public event Action<SensorSnapshot>? SnapshotPublished; public event Action<EngineState>? StateChanged;

    public PollingEngine(ISensorProvider provider, IClock clock, MonitoringOptions options, IEventLog events)
    { Provider = provider; _clock = clock; _options = options; _events = events; FastInterval = options.FastInterval; History = new HistoryStore(clock.UtcNow); Statistics = new SensorStatistics(clock.UtcNow); }

    public void Start()
    {
        lock (_lock)
        {
            if (_thread is { IsAlive: true }) return;
            _stopRequested = false; State = EngineState.Running;
            _thread = new Thread(Loop) { Name = "Mazesta.Polling", IsBackground = true }; _thread.Start();
        }
    }
    public void Stop()
    {
        _stopRequested = true; _wake.Set();
        if (_thread is { } t)
        {
            if (t.Join(TimeSpan.FromSeconds(10))) _thread = null;
            else _events.Log(EventLevel.Error, KeyEngineFailed, "Polling thread did not stop within 10 s");
        }
        if (State != EngineState.Failed) State = EngineState.Stopped;
    }
    internal void PrepareForManualTicks()
    {
        try { EnsureProviderStarted(); State = EngineState.Running; }
        catch (Exception ex) { _events.Log(EventLevel.Error, KeyEngineFailed, ex.ToString()); State = EngineState.Failed; }
    }
    public void Pause() { if (State == EngineState.Running) { State = EngineState.Paused; _wake.Set(); } }
    public void Resume() { if (State == EngineState.Paused) { State = EngineState.Running; _wake.Set(); } }
    public void SetFastInterval(TimeSpan interval)
    {
        if (!MonitoringOptions.AllowedFastSeconds.Contains((int)interval.TotalSeconds) || interval.TotalSeconds != Math.Floor(interval.TotalSeconds)) throw new ArgumentOutOfRangeException(nameof(interval));
        lock (_lock) { FastInterval = interval; _options.FastInterval = interval; foreach (var n in Hardware.Where(n => n.Kind != HardwareKind.Storage)) _nextDue[n.Id] = _clock.UtcNow; }
        _wake.Set();
    }
    private void EnsureProviderStarted() { if (_providerStarted) return; _providerStarted = true; Provider.Start(); }
    private void Loop()
    {
        try { EnsureProviderStarted(); }
        catch (Exception ex) { _events.Log(EventLevel.Error, KeyEngineFailed, ex.ToString()); State = EngineState.Failed; return; }
        while (!_stopRequested)
        {
            TimeSpan wait;
            if (State == EngineState.Running) { var sw = Stopwatch.StartNew(); TickOnce(); wait = FastInterval - sw.Elapsed; if (wait < TimeSpan.Zero) wait = TimeSpan.Zero; }
            else wait = TimeSpan.FromMilliseconds(250);
            _wake.Wait(wait); _wake.Reset();
        }
    }
    internal SensorSnapshot? TickOnce()
    {
        if (State != EngineState.Running) return null;
        SensorSnapshot snapshot;
        try
        {
            EnsureProviderStarted();
            var now = _clock.UtcNow; var due = new HashSet<HardwareId>();
            lock (_lock)
                foreach (var n in Hardware)
                    if (!_nextDue.TryGetValue(n.Id, out var d) || d <= now) { due.Add(n.Id); _nextDue[n.Id] = now + _options.CadenceFor(n.Kind); }
            var result = Provider.Poll(new PollRequest(now, due));
            var after = _clock.UtcNow;
            if (after - now > FastInterval && after - _lastOverrunLog > TimeSpan.FromMinutes(1))
            { _lastOverrunLog = after; _events.Log(EventLevel.Warning, KeyPollOverrun, $"Poll took {(after - now).TotalMilliseconds:F0} ms, interval {FastInterval.TotalSeconds} s"); }
            var kinds = Hardware.ToDictionary(n => n.Id, n => n.Kind);
            var readings = new List<SensorReading>(result.Readings.Count);
            foreach (var r in result.Readings)
            {
                result.NodeStatus.TryGetValue(r.Id.Hardware, out var st);
                var cadence = kinds.TryGetValue(r.Id.Hardware, out var k) ? _options.CadenceFor(k) : FastInterval;
                readings.Add(r with { Quality = StaleDetector.Apply(r, st, cadence, after) });
            }
            snapshot = new SensorSnapshot(Interlocked.Increment(ref _sequence), after, readings, result.NodeStatus);
            History.Append(snapshot); Statistics.Apply(snapshot);
        }
        catch (Exception ex) { _events.Log(EventLevel.Error, KeyEngineFailed, ex.ToString()); State = EngineState.Failed; return null; }
        try { SnapshotPublished?.Invoke(snapshot); }
        catch (Exception ex) { _events.Log(EventLevel.Error, KeySubscriberFailed, ex.ToString()); }
        return snapshot;
    }
    public void Dispose() { Stop(); Provider.Dispose(); _wake.Dispose(); }
}
