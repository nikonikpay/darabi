using Mazesta.Core.Fans; using Mazesta.Core.Health; using Mazesta.Core.Tuning; using Mazesta.Monitoring; using Mazesta.Persistence; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

/// <summary>
/// The graphics card's fans on a curve of temperature against duty, for as long as the app runs (with or without its window): automatic (the card's own control) or a curve read
/// at the core or at the hot spot, ready-made (silent, standard, performance, full) or saved by the user. The duty is the same for all the card's fans (the driver takes one
/// number); <see cref="GpuFanPlan"/> decides it. The card gets its fans back when the app ends, when the curve loses its temperature for a minute, and on "back to automatic".
/// </summary>
public sealed class GpuFanController : IDisposable
{
    private static GpuFanController? s_current;
    public static GpuFanController Current => s_current ?? throw new InvalidOperationException("GPU fan control is not started.");
    public static GpuFanController Start(PollingEngine engine, IGpuTuningProvider provider, AppPaths paths, ILogger log) => s_current ??= new GpuFanController(engine, provider, paths, log);

    private readonly PollingEngine _engine; private readonly IGpuTuningProvider _provider; private readonly ILogger _log; private readonly string _file; private readonly object _gate = new();
    private GpuFanProfiles _p; private HealthSample _sample = new(null, null, null, null); private long _sampleTicks, _seenTicks = Environment.TickCount64; private Timer? _timer; private int? _last; private bool _active;

    private GpuFanController(PollingEngine engine, IGpuTuningProvider provider, AppPaths paths, ILogger log)
    {
        _engine = engine; _provider = provider; _log = log; _file = GpuFanProfiles.FileIn(paths); _p = GpuFanProfiles.Read(_file);
        engine.SnapshotPublished += OnSnapshot; Arm();
    }

    /// <summary>True while the curve is in force: the process is worth keeping alive without its window.</summary>
    public bool Active { get { lock (_gate) return _p.Current.Mode == "curve"; } }
    public event Action? Changed;

    private void OnSnapshot(Mazesta.Core.Hardware.SensorSnapshot s) { _sample = HealthSampler.From(_engine.Hardware, s.Readings); Interlocked.Exchange(ref _sampleTicks, Environment.TickCount64); }
    private (IGpuTuningDevice Device, IGpuFanControl Fan)? Card() => _provider.Devices.FirstOrDefault(d => d is IGpuFanControl && d.Limits.HasFanControl) is { } d ? (d, (IGpuFanControl)d) : null;

    private void Save() { try { _p.Write(_file); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning(e, "Could not keep the graphics card's fan curve"); } }

    private void Tick()
    {
        lock (_gate)
        {
            if (_p.Current.Mode != "curve" || Card() is not { } c) return;
            var t = c.Device.ReadTelemetry(); double? hot = Environment.TickCount64 - Interlocked.Read(ref _sampleTicks) <= 10_000 ? _sample.GpuHotSpotC : null;
            if (GpuFanPlan.Duty(_p.Current.Points, _p.Current.Source, t.TemperatureC, hot, c.Device.Limits.FanMinPercent!.Value, c.Device.Limits.FanMaxPercent!.Value, _last) is not { } duty)
            {
                if (Environment.TickCount64 - _seenTicks > 60_000 && _last is not null) { c.Fan.SetFanAuto(); _last = null; _log.LogWarning("The graphics card's fans were given back: no temperature for a minute"); }
                return;
            }
            _seenTicks = Environment.TickCount64;
            if (_last == duty && t.FanPercent is { } now && Math.Abs(now - duty) <= 1) return;   // already there; the card's own reading says so
            if (c.Fan.SetFan(duty)) _last = duty; else _log.LogWarning("The driver refused fan duty {Duty}%", duty);
        }
    }

    private void Arm()
    {
        lock (_gate)
        {
            bool curve = _p.Current.Mode == "curve";
            if (curve && _timer is null) { _seenTicks = Environment.TickCount64; _timer = new Timer(_ => { try { Tick(); } catch (Exception e) { _log.LogWarning(e, "GPU fan control tick failed"); } }, null, 0, 2000); }
            else if (!curve && _timer is not null) { _timer.Dispose(); _timer = null; }
            if (curve != _active) { _active = curve; ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke()); }
        }
    }

    private void GiveBackLocked() { if (_last is not null || _p.Current.Mode == "curve") Card()?.Fan.SetFanAuto(); _last = null; }

    // ——— What the page asks for ———

    public object State(string? error = null)
    {
        var card = Card(); var t = card?.Device.ReadTelemetry();
        lock (_gate)
            return new
            {
                supported = card is not null, error, name = card?.Device.Name, min = card?.Device.Limits.FanMinPercent, max = card?.Device.Limits.FanMaxPercent,
                coreGuard = GpuFanPlan.CoreGuard, hotGuard = GpuFanPlan.HotGuard, core = t?.TemperatureC, hot = _sample.GpuHotSpotC, percent = t?.FanPercent, held = _last,
                mode = _p.Current.Mode, source = _p.Current.Source == "hot" ? "hot" : "core", profile = _p.Active,
                points = (_p.Current.Points.Count >= FanCurve.MinPoints ? _p.Current.Points : FanCurve.Preset("standard")!).Select(p => new[] { p.Temp, p.Percent }),
                profiles = new { builtin = GpuFanProfiles.Builtin, custom = _p.Custom.Keys.OrderBy(k => k).ToList() },
                presets = new[] { "silent", "standard", "performance", "full" }.ToDictionary(n => n, n => FanCurve.Preset(n)!.Select(p => new[] { p.Temp, p.Percent })),
            };
    }

    /// <summary>Automatic, or the curve on the core's or the hot spot's temperature.</summary>
    public void Set(string mode, string? source, IReadOnlyList<FanPoint>? points)
    {
        if (Card() is null) throw new ArgumentException("card");
        if (mode is not ("auto" or "curve")) throw new ArgumentException("mode");
        lock (_gate)
        {
            var s = _p.Current;
            if (mode == "curve")
            {
                s.Source = source is "hot" ? "hot" : "core";
                s.Points = [.. FanCurve.Clean(points is { Count: > 0 } ? points : s.Points.Count > 0 ? s.Points : FanCurve.Preset("standard")!) ?? throw new ArgumentException("points")];
            }
            else GiveBackLocked();
            s.Mode = mode; _last = null; _p.Active = "custom"; Save();
        }
        Arm(); if (mode == "curve") Tick();
        _log.LogInformation("GPU fans: {Mode}", mode);
    }

    public void Reset() { lock (_gate) { GiveBackLocked(); _p.Current.Mode = "auto"; _p.Active = "auto"; Save(); } Arm(); }

    /// <summary>Puts a profile on; false if there is no such profile or the card has no fan control.</summary>
    public bool Apply(string name)
    {
        if (name == "auto") { Reset(); return true; }
        if (Card() is null) return false;
        lock (_gate)
        {
            if (GpuFanProfiles.Builtin.Contains(name)) _p.Current = new() { Mode = "curve", Source = "core", Points = [.. FanCurve.Preset(name)!] };
            else if (_p.Custom.TryGetValue(name, out var saved)) _p.Current = new() { Mode = saved.Mode, Source = saved.Source, Points = [.. saved.Points] };
            else return false;
            _last = null; _p.Active = name; Save();
        }
        Arm(); Tick(); _log.LogInformation("GPU fan profile: {Profile}", name);
        return true;
    }

    /// <summary>Keeps the curve in force as a profile of this name (built-in names cannot be taken).</summary>
    public void Save(string name)
    {
        name = name.Trim(); if (name.Length is 0 or > 40 || GpuFanProfiles.Builtin.Contains(name, StringComparer.OrdinalIgnoreCase) || name.Equals("custom", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("name");
        lock (_gate)
        {
            if (_p.Current.Mode != "curve") throw new ArgumentException("curve");
            _p.Custom[name] = new() { Mode = "curve", Source = _p.Current.Source, Points = [.. _p.Current.Points] }; _p.Active = name; Save();
        }
    }

    public void Delete(string name) { lock (_gate) { if (_p.Custom.Remove(name)) { if (_p.Active == name) _p.Active = "custom"; Save(); } } }

    public void Dispose()
    {
        _engine.SnapshotPublished -= OnSnapshot;
        lock (_gate) { _timer?.Dispose(); _timer = null; GiveBackLocked(); }
        s_current = null;
    }
}
