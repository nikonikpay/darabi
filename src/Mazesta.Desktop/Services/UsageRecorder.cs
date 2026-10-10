using System.Text.Json.Nodes; using Mazesta.Core.Hardware; using Mazesta.Core.Tuning; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Evidence; using Mazesta.Monitoring; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Persistence;
namespace Mazesta.Desktop.Services;

/// <summary>
/// Writes what the app does into the usage log (<see cref="UsageLog"/>): each test and benchmark that finished, with its figures; the tuning searches come from the tuning
/// page's view model and the assistant's use from its page, through <see cref="Record"/>. It only writes: sending is the app's uploader, and only when the setting is on.
/// What is recorded are measurements and the names of parts and settings, never anything the user typed, a path, a computer name or a user name.
/// </summary>
public sealed class UsageRecorder
{
    private readonly UsageLog _log;
    public UsageRecorder(UsageLog log, TestEngine tests, BenchmarkRunner benchmarks, PollingEngine monitor)
    {
        _log = log;
        tests.TestCompleted += (id, r) => { var o = UsageData.Test(id.Value, r); AddSensors(o, monitor, r.StartedAt, r.FinishedAt); Record("test.run", o); };
        benchmarks.Finished += run => { var o = UsageData.Bench(run); AddSensors(o, monitor, run.Result.StartedAt, run.Result.FinishedAt); Record("bench.run", o); };
    }

    /// <summary>What the machine itself measured over the run's own time (processor and card: temperature, clock, power; average and peak), left out where it reported none.</summary>
    private static void AddSensors(JsonObject o, PollingEngine monitor, DateTimeOffset from, DateTimeOffset? to)
    {
        if (to is not { } end) return;
        var s = new JsonObject();
        void Put(string key, SensorStat? v) { if (v is { } x && double.IsFinite(x.Average)) { s[key] = Math.Round(x.Average, 1); s[key + "Max"] = Math.Round(x.Max, 1); } }
        try
        {
            Put("cpuTemp", SensorEvidence.CpuTemperature(monitor, from, end));
            Put("cpuClock", SensorEvidence.ReadFirst(monitor, HardwareKind.Cpu, from, end, SensorRole.CpuEffectiveClockAverage, SensorRole.CpuCoreClockAverage));
            Put("cpuPower", SensorEvidence.Read(monitor, HardwareKind.Cpu, SensorRole.CpuPackagePower, from, end));
            Put("gpuTemp", SensorEvidence.Read(monitor, HardwareKind.Gpu, SensorRole.GpuCoreTemp, from, end));
            Put("gpuClock", SensorEvidence.Read(monitor, HardwareKind.Gpu, SensorRole.GpuCoreClock, from, end));
            Put("gpuPower", SensorEvidence.Read(monitor, HardwareKind.Gpu, SensorRole.GpuPower, from, end));
        }
        catch (InvalidOperationException) { }   // the sensor list changed while it was read: the event goes without them
        if (s.Count > 0) o["sensors"] = s;
    }
    public UsageLog Log => _log;
    public void Record(string kind, JsonObject? data = null) => _log.Append(kind, data);
}

/// <summary>The figures of an event as the log keeps them. A value that was not measured is left out, never written as zero.</summary>
public static class UsageData
{
    private static double? Round(double? v, int digits = 2) => v is { } x && double.IsFinite(x) ? Math.Round(x, digits) : null;
    private static void Put(JsonObject o, string key, double? v, int digits = 2) { if (Round(v, digits) is { } r) o[key] = r; }

    public static JsonObject Test(string id, TestRunResult r)
    {
        var o = new JsonObject { ["test"] = id, ["outcome"] = r.Outcome.ToString(), ["errors"] = r.ErrorCount };
        if (r.FinishedAt is { } end) o["seconds"] = Math.Round((end - r.StartedAt).TotalSeconds);
        return o;
    }

    public static JsonObject Bench(RecordedBenchmark run)
    {
        var r = run.Result; var o = new JsonObject { ["benchmark"] = run.Definition.Id.Value, ["status"] = r.Status.ToString(), ["seconds"] = Math.Round((r.FinishedAt - r.StartedAt).TotalSeconds) };
        if (r.Metrics.Count > 0) { var m = new JsonObject(); foreach (var x in r.Metrics.Take(40)) if (Round(x.Value) is { } v) m[x.Key] = v; o["metrics"] = m; }
        if (run.Options is { Count: > 0 } opts) { var p = new JsonObject(); foreach (var kv in opts.Take(12)) { string v = kv.Key == "gpu" ? kv.Value.Split('|')[0] : kv.Value; p[kv.Key] = v.Length > 80 ? v[..80] : v; } o["options"] = p; }   // (the card by its name; its adapter id is not kept)
        return o;
    }

    public static JsonObject? Measurement(LoadMeasurement? m)
    {
        if (m is null) return null;
        var o = new JsonObject { ["errors"] = m.Errors, ["deviceLost"] = m.DeviceLost };
        Put(o, "clockMHz", m.MedianClockMHz); Put(o, "peakClockMHz", m.PeakClockMHz); Put(o, "powerW", m.AveragePowerW); Put(o, "tempC", m.AverageTemperatureC);
        Put(o, "maxTempC", m.MaxTemperatureC); Put(o, "hotSpotC", m.MaxHotSpotC); Put(o, "volts", m.AverageVoltageV, 3); Put(o, "score", m.Throughput);
        return o;
    }

    public static JsonObject Settings(GpuTuningSettings? s)
    {
        var o = new JsonObject();
        if (s is null) return o;
        o["coreOffset"] = s.CoreOffsetMHz; o["memoryOffset"] = s.MemoryOffsetMHz;
        if (s.MaxClockMHz is { } c) o["capMHz"] = c;
        if (s.PowerLimitW is { } w) o["powerLimitW"] = w;
        return o;
    }

    /// <summary>An automatic search's end: what it was, whether it worked and - when it did not - why, with the stock and tuned measurements (temperature, power, clock, score).</summary>
    public static JsonObject Auto(string kind, string gpu, AutoTuneOutcome outcome)
    {
        var o = new JsonObject { ["kind"] = kind, ["gpu"] = gpu, ["verdict"] = outcome.Verdict.ToString(), ["reason"] = outcome.ReasonKey };
        if (outcome.Detail is { Length: > 0 } d) o["detail"] = d.Length > 200 ? d[..200] : d;
        if (outcome.Settings is { } s) o["settings"] = Settings(s);
        if (Measurement(outcome.Baseline) is { } b) o["stock"] = b;
        if (Measurement(outcome.Tuned) is { } t) o["tuned"] = t;
        if (Measurement(outcome.SceneBaseline) is { } sb) o["sceneStock"] = sb;
        if (Measurement(outcome.SceneTuned) is { } st) o["sceneTuned"] = st;
        if (Measurement(outcome.MemoryBaseline) is { } mb) o["memoryStock"] = mb;
        if (Measurement(outcome.MemoryTuned) is { } mt) o["memoryTuned"] = mt;
        return o;
    }

    public static JsonObject Scene(string gpu, GpuTuningSettings now, LoadMeasurement m, string? problem)
    {
        var o = new JsonObject { ["gpu"] = gpu, ["settings"] = Settings(now), ["clean"] = problem is null };
        if (Measurement(m) is { } r) o["result"] = r;
        if (problem is not null) o["problem"] = problem.Length > 200 ? problem[..200] : problem;
        return o;
    }
}
