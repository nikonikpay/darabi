using System.IO; using System.Text.Json; using Mazesta.Core.Fans; using Mazesta.Persistence; using Mazesta.Core.Health; using Mazesta.Core.Providers; using Mazesta.Monitoring; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

/// <summary>
/// Fan control of the board's outputs, for as long as the app runs (with or without its window): auto (the board's own control), a fixed duty, or a curve of temperature
/// against duty from the processor, the graphics card or the hotter of the two. Whatever the app took is given back to the board when it ends, when a curve loses its
/// temperature for a minute, and when the user presses "back to automatic". A duty never goes below <see cref="Floor"/> % (<see cref="PumpFloor"/> % for a pump: a pump that
/// slows down stops the heat leaving the processor), and a processor at <see cref="Guard"/> °C or more puts every held fan at full. A profile sets every output at once: a
/// ready-made one (silent, standard, performance, full) or one the user saved; the outputs known to be pumps are left to the board in a ready-made profile. The tray changes the
/// profile through <c>fan-request.json</c>, which this object watches.
/// </summary>
public sealed class FanController : IDisposable
{
    public const int Floor = 20, PumpFloor = 60, Guard = 85;
    public static string[] Builtin => FanProfiles.Builtin;
    public const string RequestFile = FanProfiles.RequestFile;

    private static FanController? s_current;
    /// <summary>The one controller of this process (made at start-up, so it works with the window closed too).</summary>
    public static FanController Current => s_current ?? throw new InvalidOperationException("Fan control is not started.");
    public static FanController Start(PollingEngine engine, AppPaths paths, ILogger log) => s_current ??= new FanController(engine, paths, log);

    private readonly PollingEngine _engine; private readonly ILogger _log; private readonly string _file, _profilesFile, _requestFile; private readonly object _gate = new();
    private readonly Dictionary<string, double> _lastSet = []; private readonly HashSet<string> _touched = [];
    private Dictionary<string, FanSetting> _settings = []; private FanProfiles _profiles;
    private HealthSample _sample = new(null, null, null, null); private long _sampleTicks; private Timer? _timer; private FileSystemWatcher? _watch; private Timer? _request;
    private readonly Dictionary<string, int> _noRpm = []; private readonly HashSet<string> _pulsing = [];
    private bool _active;

    private FanController(PollingEngine engine, AppPaths paths, ILogger log)
    {
        _engine = engine; _log = log; _file = Path.Combine(paths.ConfigDir, "fans.json"); _profilesFile = Path.Combine(paths.ConfigDir, "fan-profiles.json"); _requestFile = Path.Combine(paths.ConfigDir, RequestFile);
        try { if (File.Exists(_file)) _settings = JsonSerializer.Deserialize<Dictionary<string, FanSetting>>(File.ReadAllText(_file)) ?? []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { _log.LogWarning(e, "The fan settings could not be read; starting from the board's own control"); }
        _profiles = FanProfiles.Read(_profilesFile);
        engine.SnapshotPublished += OnSnapshot; engine.Provider.StatusChanged += OnStatus;
        Arm();
        try
        {
            Directory.CreateDirectory(paths.ConfigDir);
            _watch = new FileSystemWatcher(paths.ConfigDir, RequestFile) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size, EnableRaisingEvents = true };
            _watch.Changed += (_, _) => ReadRequest(); _watch.Created += (_, _) => ReadRequest();
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { _log.LogInformation("The tray's fan requests are not watched: {Message}", e.Message); }
        ReadRequest();   // one the tray left for an app that was not running yet
    }

    /// <summary>True while any output is set to something other than auto: the process is worth keeping alive without its window.</summary>
    public bool Active { get { lock (_gate) return _settings.Values.Any(v => v.Mode != "auto"); } }
    /// <summary>Raised when <see cref="Active"/> may have changed.</summary>
    public event Action? Changed;
    public string Profile { get { lock (_gate) return _profiles.Active; } }

    private IFanControlSource? Source() => _engine.Provider as IFanControlSource;
    private void OnSnapshot(Mazesta.Core.Hardware.SensorSnapshot s) { _sample = HealthSampler.From(_engine.Hardware, s.Readings); Interlocked.Exchange(ref _sampleTicks, Environment.TickCount64); }
    private void OnStatus(Mazesta.Core.Hardware.ProviderStatus st)
    {
        if (Source() is { } fs && fs.Fans() is { Count: > 0 } found) _log.LogInformation("Fan outputs: {Fans}", string.Join(" | ", found.Select(f => $"{f.Name} {f.Percent:0}% {f.Rpm:0}rpm [{f.MinPercent}-{f.MaxPercent}] ({f.Id})")));
        ReadRequest();   // a request that came before the sensors were up
    }
    private double? Temp(string source) => source switch { "gpu" => _sample.GpuTempC, "max" => _sample.CpuTempC is { } c ? Math.Max(c, _sample.GpuTempC ?? c) : _sample.GpuTempC, _ => _sample.CpuTempC };

    private void SaveSettings() { try { Directory.CreateDirectory(Path.GetDirectoryName(_file)!); File.WriteAllText(_file, JsonSerializer.Serialize(_settings)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning(e, "Could not keep the fan settings"); } }
    private void SaveProfiles() { try { _profiles.Write(_profilesFile); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning(e, "Could not keep the fan profiles"); } }

    /// <summary>What kind of output this is: what the user (or a measurement) said, else what the name says.</summary>
    private string KindOf(FanChannel c)
    {
        if (_profiles.Labels.TryGetValue(c.Id, out var l) && l.Kind is { Length: > 0 } k) return k;
        string n = c.Name;
        if (n.Contains("pump", StringComparison.OrdinalIgnoreCase) || n.Contains("AIO", StringComparison.OrdinalIgnoreCase)) return "pump";
        if (n.StartsWith("CPU Fan", StringComparison.OrdinalIgnoreCase) || n.StartsWith("CPU_FAN", StringComparison.OrdinalIgnoreCase)) return "cpu";
        return "case";
    }
    private int FloorOf(FanChannel c) => KindOf(c) == "pump" ? PumpFloor : Floor;

    private void Give(string id) { Source()?.SetAuto(id); _touched.Remove(id); _lastSet.Remove(id); }
    private void Tick()
    {
        lock (_gate)
        {
            if (Source() is not { } src) return;
            bool stale = Environment.TickCount64 - Interlocked.Read(ref _sampleTicks) > 60_000; bool hot = _sample.CpuTempC is >= Guard && !stale;
            foreach (var ch in src.Fans())
            {
                if (_pulsing.Contains(ch.Id) || !_settings.TryGetValue(ch.Id, out var s) || s.Mode == "auto") continue;
                double? want = s.Mode == "manual" ? s.Percent : Temp(s.Source) is { } t && !stale ? FanCurve.Evaluate(s.Points, t) : null;
                if (want is null) { if (stale && _touched.Contains(ch.Id)) { Give(ch.Id); _log.LogWarning("Fan {Fan} given back: no temperature for a minute", ch.Name); } continue; }
                if (hot) want = 100;
                int floor = FloorOf(ch);
                double target = Math.Clamp(want.Value, Math.Max(floor, ch.MinPercent), Math.Max(floor, ch.MaxPercent));
                // Speeding up is at once; slowing down a few percent a time, so a fan does not hunt up and down with the temperature.
                if (_lastSet.TryGetValue(ch.Id, out var last) && target < last) target = Math.Max(target, last - 3);
                if (_lastSet.TryGetValue(ch.Id, out last) && Math.Abs(last - target) < 1 && ch.Manual) continue;
                if (src.SetManual(ch.Id, target)) { _lastSet[ch.Id] = target; _touched.Add(ch.Id); }
            }
        }
    }
    private void Arm()
    {
        lock (_gate)
        {
            bool any = _settings.Values.Any(v => v.Mode != "auto");
            if (any && _timer is null) _timer = new Timer(_ => { try { Tick(); } catch (Exception e) { _log.LogWarning(e, "Fan control tick failed"); } }, null, 0, 2000);
            else if (!any && _timer is not null) { _timer.Dispose(); _timer = null; }
            if (any != _active) { _active = any; ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke()); }
        }
    }

    // ——— What the page and the tray ask for ———

    public object State(string? error = null)
    {
        var channels = Source()?.Fans() ?? [];
        lock (_gate)
            return new
            {
                supported = channels.Count > 0, error, floor = Floor, pumpFloor = PumpFloor, guard = Guard, cpu = _sample.CpuTempC, gpu = _sample.GpuTempC, profile = _profiles.Active,
                profiles = new { builtin = Builtin, custom = _profiles.Custom.Keys.OrderBy(k => k).ToList() },
                presets = new[] { "silent", "standard", "performance", "full" }.ToDictionary(n => n, n => FanCurve.Preset(n)!.Select(p => new[] { p.Temp, p.Percent })),
                channels = channels.Select(c =>
                {
                    _settings.TryGetValue(c.Id, out var s); s ??= new FanSetting(); _profiles.Labels.TryGetValue(c.Id, out var label);
                    _noRpm[c.Id] = c.Rpm is null ? _noRpm.GetValueOrDefault(c.Id) + 1 : 0;
                    return new
                    {
                        id = c.Id, name = label?.Name is { Length: > 0 } ln ? ln : c.Name, boardName = c.Name, kind = KindOf(c), part = c.Part, percent = c.Percent, rpm = c.Rpm, held = c.Manual, mode = s.Mode, manual = s.Percent, source = s.Source,
                        // An output that has never shown a speed has no fan wired to it (or its speed is not reported): the page folds it away.
                        wired = c.Rpm is not null || _noRpm[c.Id] < 4 || c.Manual,
                        points = (s.Points.Count >= FanCurve.MinPoints ? s.Points : FanCurve.Preset("standard")!).Select(p => new[] { p.Temp, p.Percent }), min = Math.Max(FloorOf(c), c.MinPercent), max = c.MaxPercent,
                    };
                }),
            };
    }

    public void Set(string id, string mode, double? percent, string? source, IReadOnlyList<FanPoint>? points)
    {
        if (Source() is not { } src || src.Fans().FirstOrDefault(f => f.Id == id) is not { } ch) throw new ArgumentException("id");
        if (mode is not ("auto" or "manual" or "curve")) throw new ArgumentException("mode");
        lock (_gate)
        {
            var s = _settings.TryGetValue(id, out var cur) ? cur : _settings[id] = new FanSetting();
            if (mode == "manual") s.Percent = Math.Clamp(percent ?? s.Percent, FloorOf(ch), 100);
            if (mode == "curve")
            {
                s.Source = source is "gpu" or "max" ? source : "cpu";
                var cleaned = FanCurve.Clean(points is { Count: > 0 } ? points : s.Points) ?? throw new ArgumentException("points");
                s.Points = [.. cleaned];
            }
            s.Mode = mode; _lastSet.Remove(id);
            if (mode == "auto") Give(id);
            _profiles.Active = "custom"; SaveProfiles(); SaveSettings();
        }
        Arm(); if (mode != "auto") Tick();
        _log.LogInformation("Fan {Id}: {Mode}", id, mode);
    }

    /// <summary>Back to the board's own control, everywhere.</summary>
    public void Reset()
    {
        lock (_gate) { foreach (var s in _settings.Values) s.Mode = "auto"; _profiles.Active = "auto"; SaveSettings(); SaveProfiles(); GiveAllLocked(); }
        Arm();
    }

    /// <summary>Puts a profile on every output at once; false if there is no such profile or the sensors are not up yet.</summary>
    public bool Apply(string name)
    {
        if (name == "auto") { Reset(); _log.LogInformation("Fan profile: auto"); return true; }
        if (Source() is not { } src || src.Fans() is not { Count: > 0 } channels) return false;
        lock (_gate)
        {
            if (Builtin.Contains(name))
            {
                var pts = FanCurve.Preset(name)!;
                foreach (var c in channels)
                {
                    if (KindOf(c) == "pump") { _settings[c.Id] = new FanSetting { Mode = "auto" }; Give(c.Id); continue; }   // a pump is not slowed by a profile
                    _settings[c.Id] = new FanSetting { Mode = "curve", Source = "max", Points = [.. pts] };
                    _lastSet.Remove(c.Id);
                }
            }
            else if (_profiles.Custom.TryGetValue(name, out var saved))
            {
                foreach (var c in channels)
                {
                    if (saved.TryGetValue(c.Id, out var s)) { _settings[c.Id] = Clone(s); _lastSet.Remove(c.Id); }
                    else { _settings[c.Id] = new FanSetting { Mode = "auto" }; Give(c.Id); }
                }
            }
            else return false;
            _profiles.Active = name; SaveSettings(); SaveProfiles();
        }
        Arm(); Tick();
        _log.LogInformation("Fan profile: {Profile}", name);
        return true;
    }

    /// <summary>Keeps what every output is set to now as a profile of this name (built-in names cannot be taken).</summary>
    public void Save(string name)
    {
        name = name.Trim(); if (name.Length is 0 or > 40 || Builtin.Contains(name, StringComparer.OrdinalIgnoreCase) || name.Equals("custom", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("name");
        lock (_gate) { _profiles.Custom[name] = _settings.ToDictionary(kv => kv.Key, kv => Clone(kv.Value)); _profiles.Active = name; SaveProfiles(); }
    }

    public void Delete(string name) { lock (_gate) { if (_profiles.Custom.Remove(name)) { if (_profiles.Active == name) _profiles.Active = "custom"; SaveProfiles(); } } }

    /// <summary>The user says what an output is: a name and/or a kind (cpu, pump, case); empty clears.</summary>
    public void Label(string id, string? name, string? kind)
    {
        if (Source() is not { } src || src.Fans().All(f => f.Id != id)) throw new ArgumentException("id");
        if (kind is { Length: > 0 } && kind is not ("cpu" or "pump" or "case")) throw new ArgumentException("kind");
        lock (_gate)
        {
            var l = _profiles.Labels.TryGetValue(id, out var cur) ? cur : _profiles.Labels[id] = new FanLabel();
            if (name is not null) l.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim()[..Math.Min(40, name.Trim().Length)];
            if (kind is not null) l.Kind = string.IsNullOrWhiteSpace(kind) ? null : kind;   // null leaves it as it is, empty clears it
            if (l.Name is null && l.Kind is null) _profiles.Labels.Remove(id);
            SaveProfiles();
        }
    }

    /// <summary>
    /// Finds out what an output drives: it is put at full for a few seconds (only up, so nothing can overheat), the speed its fan reports is watched, and it is given back. A fan
    /// speeds up (it is a fan: the answer says by how much); a speed that stays where it was while the duty rose is a pump on a fixed supply or a fan that cannot be controlled;
    /// no speed at all means nothing is wired to it. The user hears which one it is too. The kind is only suggested, never set.
    /// </summary>
    public async Task<object> IdentifyAsync(string id)
    {
        if (Source() is not { } src || src.Fans().FirstOrDefault(f => f.Id == id) is not { } before) throw new ArgumentException("id");
        bool held; lock (_gate) held = _settings.TryGetValue(id, out var s) && s.Mode != "auto";
        double? start = before.Rpm, duty0 = before.Percent; double? peak = start;
        bool wasHeld = before.Manual;
        try
        {
            lock (_gate) { _pulsing.Add(id); src.SetManual(id, 100); _touched.Add(id); }
            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                if (src.Fans().FirstOrDefault(f => f.Id == id)?.Rpm is { } r && (peak is null || r > peak)) peak = r;
            }
        }
        finally
        {
            lock (_gate) { _pulsing.Remove(id); if (held) { _lastSet.Remove(id); Tick(); } else Give(id); }
        }
        string answer = peak is null ? "none" : start is null ? "fan" : peak >= start * 1.12 ? "fan" : "fixed";
        string? suggest = answer == "fan" ? (KindOf(before) == "pump" ? "case" : null) : answer == "fixed" && peak >= 1200 ? "pump" : null;
        _log.LogInformation("Fan {Id} identified: {Start} -> {Peak} rpm ({Answer})", id, start, peak, answer);
        return new { id, answer, start, peak, duty = duty0, suggest };
    }

    // ——— The tray's requests ———

    private void ReadRequest()
    {
        try
        {
            if (!File.Exists(_requestFile)) return;
            string text; try { text = File.ReadAllText(_requestFile); } catch (IOException) { _request ??= new Timer(_ => ReadRequest(), null, 500, Timeout.Infinite); return; }   // still being written
            using var doc = JsonDocument.Parse(text); string name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (name.Length == 0) { File.Delete(_requestFile); return; }
            if (Source() is not { } src || src.Fans().Count == 0) { _request?.Dispose(); _request = new Timer(_ => ReadRequest(), null, 3000, Timeout.Infinite); return; }   // the sensors are not up: the file stays and is read again
            File.Delete(_requestFile); _profiles = FanProfiles.Read(_profilesFile);   // the tray may have been the last to write it
            if (!Apply(name)) _log.LogWarning("The tray asked for the fan profile {Name}, which is not known", name);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { _log.LogInformation("A fan request was not read: {Message}", e.Message); }
    }

    private static FanSetting Clone(FanSetting s) => new() { Mode = s.Mode, Percent = s.Percent, Source = s.Source, Points = [.. s.Points] };
    private void GiveAllLocked() { foreach (var id in _touched.ToList()) Give(id); }

    public void Dispose()
    {
        _engine.SnapshotPublished -= OnSnapshot; _engine.Provider.StatusChanged -= OnStatus; _watch?.Dispose(); _request?.Dispose();
        lock (_gate) { _timer?.Dispose(); _timer = null; GiveAllLocked(); }
        s_current = null;
    }
}
