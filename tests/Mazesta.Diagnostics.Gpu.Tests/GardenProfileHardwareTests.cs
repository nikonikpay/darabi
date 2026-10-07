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
