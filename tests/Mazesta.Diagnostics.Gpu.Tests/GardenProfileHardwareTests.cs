using Xunit; using Xunit.Abstractions; using Mazesta.Diagnostics.Gpu.Benchmarks; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;
[Trait("Category", "Hardware")]
/// <summary>Not a check: with MAZESTA_PROFILE set, prints how long each pass of a frame takes on the card (GPU timestamps, 1080p, heavy quality, averaged over six seconds of the walk).</summary>
public class GardenProfileHardwareTests(ITestOutputHelper o)
{
    [Theory, InlineData(false), InlineData(true)]
    public void Passes(bool rt)
    {
        if (Environment.GetEnvironmentVariable("MAZESTA_PROFILE") is not { Length: > 0 } || GpuDevices.Resolve("") is null) return;
        using var s = new D3D12Session(GpuDevices.Resolve("")!);
        if (rt && !GpuFeatures.SupportsInlineRayTracing(GpuDevices.Resolve("")!)) return;
        var g = new GardenGpu(s, GardenScene.Embedded);
        using var r = new GardenRaster(s, g, 1920, 1080, [], 3, rt) { Profile = true };
        for (int i = 0; i < 5; i++) s.Run(l => r.Draw(l, i / 30f, r.OwnTarget));
        var sums = new Dictionary<string, double>(); var order = new List<string>(); int n = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < 6) { float t = (float)sw.Elapsed.TotalSeconds; s.Run(l => r.Draw(l, t, r.OwnTarget)); foreach (var (name, ms) in r.PassTimes()) { if (!sums.ContainsKey(name)) { sums[name] = 0; order.Add(name); } sums[name] += ms; } n++; }
        o.WriteLine($"rt={rt} {n} frames, total {sums.Values.Sum() / n:F2} ms");
        foreach (var name in order) o.WriteLine($"   {name,-36} {sums[name] / n,6:F2} ms  {100 * sums[name] / sums.Values.Sum(),4:F1} %");
    }
}

[Trait("Category", "Hardware")]
/// <summary>Not a check: with MAZESTA_PROFILE set, prints what the processor does in a live frame of the weather at each level - the air, the bodies, their collisions, the plants - and how much of the machine it keeps busy.</summary>
public class GardenSimLoadHardwareTests(ITestOutputHelper o)
{
    [Theory, InlineData("on"), InlineData("high")]
    public void Processor(string level)
    {
        if (Environment.GetEnvironmentVariable("MAZESTA_PROFILE") is not { Length: > 0 } || GpuDevices.Resolve("") is null) return;
        using var s = new D3D12Session(GpuDevices.Resolve("")!);
        var build = System.Diagnostics.Stopwatch.StartNew();
        var g = new GardenGpu(s, GardenScene.Embedded, null, null, WeatherLevel.Parse(level));
        o.WriteLine($"{level}: built in {build.Elapsed.TotalSeconds:F1} s, grid {g.Weather!.VoxelBytes / 1048576} MB, air {g.Weather.FieldBytes / 1048576} MB ({g.Weather.Field!.Cells:N0} cells, {g.Weather.Field.SolidCells:N0} solid), working set {Environment.WorkingSet / 1048576} MB");
        for (int i = 0; i < 10; i++) g.Update(i / 60f);
        var phases = new double[8]; double wind = 0, bodies = 0, collide = 0, total = 0; int n = 0;
        var cpu0 = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime; var wall = System.Diagnostics.Stopwatch.StartNew();
        while (wall.Elapsed.TotalSeconds < 5)
        {
            var t = System.Diagnostics.Stopwatch.StartNew(); g.Update(1f + (float)wall.Elapsed.TotalSeconds); total += t.Elapsed.TotalMilliseconds;
            for (int k = 0; k < 6; k++) phases[k] += g.Weather.Field!.Phase[k]; wind += g.Weather.WindSeconds * 1000; collide += g.Weather.CollideSeconds * 1000; bodies += g.Weather.LastSeconds * 1000 - g.Weather.WindSeconds * 1000 - g.Weather.CollideSeconds * 1000; n++;
        }
        o.WriteLine($"   phases: weather {phases[0] / n:F2}, force {phases[1] / n:F2}, advect {phases[2] / n:F2}, divergence {phases[3] / n:F2}, sweeps {phases[4] / n:F2}, gradient {phases[5] / n:F2}");
        double busy = (System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalSeconds / wall.Elapsed.TotalSeconds;
        o.WriteLine($"{level}: {n} frames, {total / n:F2} ms a frame: air {wind / n:F2}, bodies {bodies / n:F2}, collisions {collide / n:F2}; {busy:F1} cores busy of {Environment.ProcessorCount}");
    }
}
