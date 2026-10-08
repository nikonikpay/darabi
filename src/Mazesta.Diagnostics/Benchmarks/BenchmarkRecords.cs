using System.Text.Json; using System.Text.Json.Serialization;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>A kept benchmark result: when it was measured, its headline number, and every number it measured.</summary>
public sealed record BenchmarkRecord(DateTimeOffset At, string Key, double Value, string Unit, IReadOnlyList<BenchmarkMetric> Metrics);

/// <summary>What a new run is against the best kept one on this system. <see cref="Previous"/> is null the first time; <see cref="ChangePercent"/>
/// is signed so that positive is always better (a latency that fell is positive). <see cref="Saved"/> is true only when the run became the record.</summary>
public sealed record BenchmarkComparison(BenchmarkRecord Current, BenchmarkRecord? Previous, double? ChangePercent, bool Saved);

/// <summary>The part a benchmark's result belongs to when it is compared with other systems: a CPU benchmark is compared CPU model with CPU
/// model, a storage one drive model with drive model. <see cref="None"/> is never compared with others (an internet speed measures the line).</summary>
public enum PeerPart { None, Cpu, Memory, Gpu, Drive }

/// <summary>The one number a benchmark is ranked by, and which way is better. Everything else it measures is kept alongside but not ranked.
/// <see cref="Version"/> is the benchmark's workload version: results of different versions are never compared, so it is raised whenever a
/// benchmark's work changes (another matrix size, another scene) and the shared comparison lists start over for it.</summary>
public sealed record HeadlineMetric(string Key, bool HigherIsBetter, PeerPart Part = PeerPart.None, int Version = 1);

/// <summary>
/// The best result of each benchmark on each system, and nothing else: a run is kept only when it beats (or first sets) the record, so a slower
/// run never replaces a faster one, whether it happens now or after the app restarts. A benchmark is ranked by its headline number only; a run
/// that is not complete, or did not measure its headline, is never compared and never kept. Results of different options (another drive,
/// another GPU) are different records. Nothing leaves the machine: the file is <c>Data/benchmarks/records.json</c>.
/// </summary>
public sealed class BenchmarkRecords
{
    private static readonly Dictionary<string, HeadlineMetric> Headlines = new()
    {
        ["bench.cpu.single"] = new("Bench_Cpu_Gflops", true, PeerPart.Cpu), ["bench.cpu.multi"] = new("Bench_Cpu_Gflops", true, PeerPart.Cpu),
        ["bench.memory"] = new("Bench_Mem_Read", true, PeerPart.Memory, Version: 3), ["bench.storage"] = new("Bench_Storage_SeqRead", true, PeerPart.Drive, Version: 2),
        ["bench.gpu.rt"] = new("Bench_Gpu_Rt_Fps", true, PeerPart.Gpu), ["bench.gpu.ai"] = new("Bench_Gpu_Ai_Fp32", true, PeerPart.Gpu),
        ["bench.gpu.scene.d3d"] = new("Bench_Scene_ScoreGpu", true, PeerPart.Gpu, 35),   // v35: a frame is sent to the card in pieces and shown before the wait, so the card is no longer idle while the processor records (the same picture, bit for bit); v34: NVIDIA Image Scaling is gone (the frame is the lens's picture, as before it); v33: the weather's air is a fluid simulated on every core (a stable-fluids grid of 0.67 million cells that the wind goes round the hall in, not a formula; the plants still lean as before), leaves and twigs collide as bodies (four points each, and with one another through a spatial hash), the mesh smoothing option is gone, NIS sharpens at the preview's 35 %, and the CPU score is 30 x the frames a second (it was 3 x, for a frame that took a tenth of the time); v32: the garden has weather - rain, gusts that carry leaves and twigs, simulated on every core of the processor and tested against a 29 MB grid of the garden's solids (20,000 more objects, the processor's time per frame about doubles, the card's unchanged), NVIDIA Image Scaling sharpens the frame by default, and the frame draws one geometry pass fewer (the ambient occlusion's depth is the MSAA prepass's, resolved) and culls the pool's mirror image to the part of the picture it shows; v31: the camera's passes (depth, the light, the mirror image) skip what is out of its view - the same picture, drawn faster; v30: the graphics score no longer multiplies by the pixel count (a 4K run scored more than a 720p one at the same rate); v29: ranked by the graphics score (points, not frames a second; the card's own time per frame, per Full HD of pixels), with a CPU, a RAM and an overall score beside it; v28: the hall lit by its two chandeliers alone (the lamp that hung from nothing between them is gone: one lamp and one shadow cube fewer); v27: the plants' wood as modelled (the trees' twigs gone, the shrubs drawn without stems: fewer triangles), the courtyard's shadow map looked up closer to the surface, the ray-traced key light on floors; v26: the hall's mirror, the roof's planters, smoothed ray-traced shadows, lamps of shorter reach; v25: one benchmark with a ray-tracing switch (the separate ray-traced garden, bench.gpu.scene.rt, is gone: with the switch on the same frame finds its shadows and mirror images with rays, and is kept as a record of its own); softer window light in the hall, a third less exposure there, the logo lit by its surroundings, a spout over the upper basin. v24 (RT v25): the sun is low while the walk is in the hall and the hall's lamps burn by day; larger leaves on the small-leaved trees and the shrubs; steam from the kettle alone; in the Direct3D test a pixel asks only the lamps in reach of it, a depth pass goes before the frame's light, and the key light's shadow maps are drawn every few frames. v23 (RT v24): a sharper lens (half the blur out of focus, a crisp edge in it), darker corners (ambient occlusion reaches 0.8 m and counts for the lamps too), and in the Direct3D test the key light's beams in the hall's air. v22 (RT v23): the garden's small life: 140 fireflies at night (16 of them lights), 24 butterflies by day, steam over the kettle and the samovar; the fountain throws 832 smaller droplets (388). v21 (RT v22): both tests go through a whole day in one walk (GardenDay): the sun crosses the sky, its light comes in at the hall's stained windows, the lamps are lit at dusk, the way back is by night; the Direct3D test draws its shadow maps every frame. v20 (RT v21): the garden's plants keep their leaves' outlines (the trees by the pool, the shrubs, the geraniums: 9.6 M triangles a frame, 7.0 before), the lanterns and the stained panes are whole, the pool's coping has a footing. v19 (RT v20): the owner's V12 scene (the hall's plasterwork, chandeliers, paintings and tea counter, pots round the pool, more trees, benches), the trees beyond the walls in leaf, and a longer walk (162 s) that visits them. v18 (RT v19): the building keeps its bevelled edges (1.28 M triangles). v17 (RT v18): the orsi, the door, the windcatchers and the furniture keep more of their carving (1.03 M triangles). v16 (RT v17): the ray tracer gathers light across the frames it shows (a temporal denoiser); its rays differ from moment to moment. v15 (RT v16): the fountain's jet and the streams over its rim are continuous water. v14 (RT v15): the plants lean and flutter in a wind. v13 (RT v14): Direct3D surfaces mirror the garden round them (two pictures drawn once), not the sky alone. v12 (RT v13): Direct3D lights the garden with the light bounced round it, from a volume the ray tracer worked out beforehand. v11 (RT v12): the hall's lamps and the lanterns cast shadows in Direct3D (a cube of depths each); the hall's lamps are brighter. v10 (RT v11): the frame drawn in light's own units and passed through a lens (glow, depth of field). v9 (RT v10): textures at twice the size each way (1024 and 512). v8 (RT v9): the owner's V10 scene whole (furnished hall, gate, fountain with its water, every plant), a longer walk that goes through the hall, metal-roughness shading; Direct3D with a sky map, ambient occlusion from a depth pass and reversed depth; ray traced with a new light rig, a moving moon and mirror sphere, light coloured by the stained panes. v7 (RT v8): the V8 building (carved windcatchers, detailed columns and orsi), normal-mapped stone, plaster and wood, the mountains round the horizon. v6 (RT v7): the camera walks at the visual test's pace by the clock, whole walks; the ray-traced one has one setting (4 rays). v2: the courtyard V4 scene; RT at 4 rays a pixel. v3: the same 128-view tour on every card, timed on the frames alone. v4: the V6 building, leaves as cut-outs on their own trees, the still scene's shadow drawn once. v5: a clouded sky, rooms behind the stained windows, foliage shading; RT denoised. RT v6: its glowing orbs and mirror sphere where their lights are
        ["bench.network.internet"] = new("Bench_Net_Download", true),
    };
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly string _file; private readonly object _lock = new();
    private Dictionary<string, SystemRecords> _systems;

    public BenchmarkRecords(string dataRoot)
    {
        _file = Path.Combine(dataRoot, "benchmarks", "records.json");
        _systems = Load(_file);
        foreach (var s in _systems.Values)
            foreach (var (key, record) in s.Best.ToList())
            {
                string to = Normalized(key);
                if (to == key) continue;
                s.Best.Remove(key); bool higher = Headline(key.Split('|')[0])?.HigherIsBetter ?? true;
                if (!s.Best.TryGetValue(to, out var had) || (higher ? record.Value > had.Value : record.Value < had.Value)) s.Best[to] = record;
            }
    }

    public static HeadlineMetric? Headline(string benchmarkId) => Headlines.GetValueOrDefault(benchmarkId);

    /// <summary>The record key: the benchmark, the options it ran with in a fixed order, and its workload's version from the second on. A record of
    /// an earlier workload stays in the file under its old key but is never compared with the new one (the first version's keys had no version,
    /// so they are still its own).</summary>
    public static string RecordKey(string benchmarkId, IReadOnlyDictionary<string, string>? options)
    {
        options = Effective(benchmarkId, options);
        string key = Normalized( options is null || options.Count == 0 ? benchmarkId : benchmarkId + "|" + string.Join("|", options.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => $"{o.Key}={o.Value}")));
        return Headline(benchmarkId)?.Version is > 1 and int v ? key + "|v=" + v.ToString(System.Globalization.CultureInfo.InvariantCulture) : key;
    }

    /// <summary>The garden benchmarks gained a resolution and a quality option after their first records and comparison lists existed. At the
    /// settings they measured before (the defaults) the work is the same, so those options are left out of the keys: the old records and lists
    /// stay valid, and only a run at other settings starts a list of its own.</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> Unchanged = new()
    {
        ["bench.gpu.scene.d3d"] = new() { ["resolution"] = "2560x1440", ["quality"] = "3", ["raytracing"] = "off", ["weather"] = "on" },
    };
    /// <summary>Options that do not change what is measured (the readout over the garden is laid over the finished frame, outside the time
    /// counted): they are in no key, so a run with it hidden is the same record and the same list.</summary>
    private static readonly HashSet<string> NotTheWork = new(StringComparer.Ordinal) { "overlay", "fullscreen" };
    public static IReadOnlyDictionary<string, string>? Effective(string benchmarkId, IReadOnlyDictionary<string, string>? options)
    {
        if (options is null) return null;
        // The quality does not apply with ray tracing (it is not offered): whatever it was left at, a ray-traced run is the default quality's.
        if (benchmarkId == "bench.gpu.scene.d3d" && options.GetValueOrDefault("raytracing") == "on" && options.TryGetValue("quality", out var qv) && qv != "3")
            options = options.ToDictionary(o => o.Key, o => o.Key == "quality" ? "3" : o.Value);
        var legacy = Unchanged.GetValueOrDefault(benchmarkId);
        return legacy is null && !options.Keys.Any(NotTheWork.Contains) ? options : options.Where(o => !NotTheWork.Contains(o.Key) && !(legacy is not null && legacy.TryGetValue(o.Key, out var v) && v == o.Value)).ToDictionary(o => o.Key, o => o.Value);
    }

    /// <summary>A graphics card is chosen as "name|LUID", and Windows gives the adapter another LUID at every start: with it in the key a
    /// card's record was lost at the next restart. The key names the card alone; records kept under the old keys are moved at load.</summary>
    internal static string Normalized(string key) => System.Text.RegularExpressions.Regex.Replace(key, @"(\|gpu=[^|]*)\|\d+(?=\||$)", "$1");

    public BenchmarkRecord? Best(string system, string recordKey)
    {
        lock (_lock) return _systems.TryGetValue(system, out var s) ? s.Best.GetValueOrDefault(recordKey) : null;
    }

    /// <summary>Versions up to 0.6 named a system before its hardware was read, so their records sit under "<c>machine | </c>" with no CPU or GPU.
    /// They are moved to the machine's full key once it is known, where that key has no record of the same benchmark yet.</summary>
    public void AdoptUnnamed(string system, string machine)
    {
        string old = machine + " | ";
        lock (_lock)
        {
            if (old == system || !_systems.Remove(old, out var legacy)) return;
            var s = _systems.TryGetValue(system, out var found) ? found : _systems[system] = new SystemRecords(legacy.Name, []);
            foreach (var (key, record) in legacy.Best) s.Best.TryAdd(key, record);
            Save();
        }
    }

    /// <summary>Compares a finished run with the record and keeps it if it is better. Null when the run cannot be ranked.</summary>
    public BenchmarkComparison? Offer(string system, string systemName, string recordKey, BenchmarkResult result)
    {
        if (Rank(result) is not { } current) return null;
        var headline = Headlines[result.Id.Value];
        lock (_lock)
        {
            var previous = _systems.TryGetValue(system, out var s) ? s.Best.GetValueOrDefault(recordKey) : null;
            var comparison = Compare(current, previous, headline.HigherIsBetter);
            if (comparison.Saved)
            {
                s ??= _systems[system] = new SystemRecords(systemName, []);
                s.Best[recordKey] = current;
                Save();
            }
            return comparison;
        }
    }

    /// <summary>The run as a record, or null when it is not complete or lacks its headline number (unranked runs are never kept).</summary>
    internal static BenchmarkRecord? Rank(BenchmarkResult result)
    {
        if (result.Status != BenchmarkStatus.Completed || Headline(result.Id.Value) is not { } headline) return null;
        var m = result.Metrics.FirstOrDefault(x => x.Key == headline.Key);
        return m is null || double.IsNaN(m.Value) || double.IsInfinity(m.Value) || m.Value <= 0 ? null : new BenchmarkRecord(result.FinishedAt, m.Key, m.Value, m.Unit, result.Metrics);
    }

    /// <summary>Pure: the comparison of a ranked run with the kept record. An equal result does not replace the older record.</summary>
    internal static BenchmarkComparison Compare(BenchmarkRecord current, BenchmarkRecord? previous, bool higherIsBetter)
    {
        if (previous is null || previous.Key != current.Key || previous.Value <= 0) return new(current, null, null, true);
        double change = (current.Value - previous.Value) / previous.Value * 100 * (higherIsBetter ? 1 : -1);
        return new(current, previous, change, change > 0);
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            string tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_systems, Json));
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // a record that cannot be written is only lost, never half-written
    }

    private static Dictionary<string, SystemRecords> Load(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, SystemRecords>>(File.ReadAllText(file), Json) ?? [] : []; }
        catch (JsonException)
        {
            // A damaged file is set aside, not overwritten by the next record, so the old records can still be recovered by hand.
            try { File.Move(file, file + $".damaged-{DateTime.Now:yyyyMMdd-HHmmss}"); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    private sealed record SystemRecords(string Name, Dictionary<string, BenchmarkRecord> Best);
}
