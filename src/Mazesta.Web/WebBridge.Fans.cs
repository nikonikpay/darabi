using System.IO; using System.Text.Json; using System.Threading; using Mazesta.Core.Fans; using Mazesta.Core.Health; using Mazesta.Core.Providers; using Mazesta.Monitoring;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    private sealed class FanSetting { public string Mode { get; set; } = "auto"; public double Percent { get; set; } = 50; public string Source { get; set; } = "cpu"; public List<FanPoint> Points { get; set; } = []; }

    /// <summary>
    /// Fan control of the board's outputs: auto (the board's own control), a fixed duty, or a curve of temperature against duty from the processor, the graphics card or the
    /// hotter of the two. The app holds an output only while it runs: whatever it took is given back to the board when it closes, when a curve loses its temperature for
    /// a minute, and when the user presses "back to automatic". A duty never goes below <see cref="Floor"/> %, and a processor at <see cref="Guard"/> °C or more
    /// puts every held fan at full; both are said on the page.
    /// </summary>
    private const int Floor = 20, Guard = 85;
    private void RegisterFans()
    {
        var engine = _sp.GetRequiredService<PollingEngine>(); string file = Path.Combine(_paths.ConfigDir, "fans.json"); var gate = new object();
        Dictionary<string, FanSetting> settings = [];
        try { if (File.Exists(file)) settings = JsonSerializer.Deserialize<Dictionary<string, FanSetting>>(File.ReadAllText(file)) ?? []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { _log.LogWarning(e, "The fan settings could not be read; starting from the board's own control"); }
        var lastSet = new Dictionary<string, double>(); var touched = new HashSet<string>();
        HealthSample sample = new(null, null, null, null); long sampleTicks = 0; Timer? timer = null;

        void OnSnapshot(Mazesta.Core.Hardware.SensorSnapshot s) { sample = HealthSampler.From(engine.Hardware, s.Readings); Interlocked.Exchange(ref sampleTicks, Environment.TickCount64); }
        engine.SnapshotPublished += OnSnapshot;
        void OnStatus(Mazesta.Core.Hardware.ProviderStatus st)
        {
            if (Source() is { } fs && fs.Fans() is { Count: > 0 } found) _log.LogInformation("Fan outputs: {Fans}", string.Join(" | ", found.Select(f => $"{f.Name} {f.Percent:0}% {f.Rpm:0}rpm [{f.MinPercent}-{f.MaxPercent}] ({f.Id})")));
        }
        engine.Provider.StatusChanged += OnStatus;
        IFanControlSource? Source() => engine.Provider as IFanControlSource;
        void Save() { try { Directory.CreateDirectory(_paths.ConfigDir); File.WriteAllText(file, JsonSerializer.Serialize(settings)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning(e, "Could not keep the fan settings"); } }
        double? Temp(string source) => source switch { "gpu" => sample.GpuTempC, "max" => sample.CpuTempC is { } c ? Math.Max(c, sample.GpuTempC ?? c) : sample.GpuTempC, _ => sample.CpuTempC };

        void Give(string id) { Source()?.SetAuto(id); touched.Remove(id); lastSet.Remove(id); }
        void Tick()
        {
            lock (gate)
            {
                if (Source() is not { } src) return;
                bool stale = Environment.TickCount64 - Interlocked.Read(ref sampleTicks) > 60_000; bool hot = sample.CpuTempC is >= Guard && !stale;
                foreach (var ch in src.Fans())
                {
                    if (!settings.TryGetValue(ch.Id, out var s) || s.Mode == "auto") continue;
                    double? want = s.Mode == "manual" ? s.Percent : Temp(s.Source) is { } t && !stale ? FanCurve.Evaluate(s.Points, t) : null;
                    if (want is null) { if (stale && touched.Contains(ch.Id)) { Give(ch.Id); _log.LogWarning("Fan {Fan} given back: no temperature for a minute", ch.Name); } continue; }
                    if (hot) want = 100;
                    double target = Math.Clamp(want.Value, Math.Max(Floor, ch.MinPercent), Math.Max(Floor, ch.MaxPercent));
                    // Speeding up is at once; slowing down a few percent a time, so a fan does not hunt up and down with the temperature.
                    if (lastSet.TryGetValue(ch.Id, out var last) && target < last) target = Math.Max(target, last - 3);
                    if (lastSet.TryGetValue(ch.Id, out last) && Math.Abs(last - target) < 1 && ch.Manual) continue;
                    if (src.SetManual(ch.Id, target)) { lastSet[ch.Id] = target; touched.Add(ch.Id); }
                }
            }
        }
        void Arm() { lock (gate) { bool any = settings.Values.Any(v => v.Mode != "auto"); if (any && timer is null) timer = new Timer(_ => { try { Tick(); } catch (Exception e) { _log.LogWarning(e, "Fan control tick failed"); } }, null, 1000, 2000); else if (!any && timer is not null) { timer.Dispose(); timer = null; } } }
        void GiveAll() { lock (gate) { foreach (var id in touched.ToList()) Give(id); } }
        _cleanup.Add(() => { engine.SnapshotPublished -= OnSnapshot; engine.Provider.StatusChanged -= OnStatus; lock (gate) { timer?.Dispose(); timer = null; } GiveAll(); });
        Arm();

        object State(string? error = null)
        {
            var src = Source(); var channels = src?.Fans() ?? [];
            lock (gate)
                return new
                {
                    supported = channels.Count > 0, error, floor = Floor, guard = Guard, cpu = sample.CpuTempC, gpu = sample.GpuTempC,
                    presets = new[] { "silent", "standard", "performance", "full" }.ToDictionary(n => n, n => FanCurve.Preset(n)!.Select(p => new[] { p.Temp, p.Percent })),
                    channels = channels.Select(c =>
                    {
                        settings.TryGetValue(c.Id, out var s); s ??= new FanSetting();
                        return new
                        {
                            id = c.Id, name = c.Name, part = c.Part, percent = c.Percent, rpm = c.Rpm, held = c.Manual, mode = s.Mode, manual = s.Percent, source = s.Source,
                            points = (s.Points.Count >= FanCurve.MinPoints ? s.Points : FanCurve.Preset("standard")!).Select(p => new[] { p.Temp, p.Percent }), min = Math.Max(Floor, c.MinPercent), max = c.MaxPercent,
                        };
                    }),
                };
        }

        Method("fans.state", _ => State());
        Method("fans.set", p =>
        {
            string id = Str(p, "id"), mode = Str(p, "mode");
            if (Source() is not { } src || src.Fans().All(f => f.Id != id)) throw new ArgumentException("id");
            if (mode is not ("auto" or "manual" or "curve")) throw new ArgumentException("mode");
            lock (gate)
            {
                var s = settings.TryGetValue(id, out var cur) ? cur : settings[id] = new FanSetting();
                if (mode == "manual") s.Percent = Math.Clamp(p.TryGetProperty("percent", out var pv) && pv.TryGetDouble(out var d) ? d : s.Percent, Floor, 100);
                if (mode == "curve")
                {
                    s.Source = Str(p, "source") is "gpu" or "max" ? Str(p, "source") : "cpu";
                    var raw = p.TryGetProperty("points", out var pts) && pts.ValueKind == JsonValueKind.Array ? pts.EnumerateArray().Where(a => a.GetArrayLength() == 2).Select(a => new FanPoint(a[0].GetDouble(), a[1].GetDouble())).ToList() : [];
                    var cleaned = FanCurve.Clean(raw.Count > 0 ? raw : s.Points) ?? throw new ArgumentException("points");
                    s.Points = [.. cleaned];
                }
                s.Mode = mode; lastSet.Remove(id);
                if (mode == "auto") Give(id);
                Save();
            }
            Arm(); if (mode != "auto") Tick();
            _log.LogInformation("Fan {Id}: {Mode}", id, mode);
            return State();
        });
        Method("fans.reset", _ => { lock (gate) { foreach (var s in settings.Values) s.Mode = "auto"; Save(); } GiveAll(); Arm(); return State(); });
    }
}
