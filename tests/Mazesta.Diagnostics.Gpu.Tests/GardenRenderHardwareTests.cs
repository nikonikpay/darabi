using System.IO.Compression; using ComputeSharp; using Xunit;
using Mazesta.Diagnostics.Gpu.Benchmarks; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

/// <summary>Draws the garden on the real GPU. A frame drawn twice at one moment must be the same bits (the check frames rely on it), and
/// the picture must be a picture (sky above, lit garden below, not one flat colour). With MAZESTA_RENDER_DIR set, the frames are also
/// written there as PNG files to look at (MAZESTA_RENDER_WIDTH: how wide, 960 unless said; MAZESTA_RENDER_TIMES: at which moments).</summary>
[Trait("Category", "Hardware")]
public class GardenRenderHardwareTests
{
    private static bool NoGpu => GpuDevices.Resolve("") is null;
    private static int W => int.TryParse(Environment.GetEnvironmentVariable("MAZESTA_RENDER_WIDTH"), out int w) && w is >= 320 and <= 3840 ? w / 16 * 16 : 960;
    private static int H => W * 9 / 16;
    /// <summary>The moments drawn for looking at: round the walk (the garden, the terrace, the hall's rooms), or those MAZESTA_RENDER_TIMES lists.</summary>
    private static float[] Times => Environment.GetEnvironmentVariable("MAZESTA_RENDER_TIMES") is { Length: > 0 } list
        ? [.. list.Split(',').Select(t => float.Parse(t, System.Globalization.CultureInfo.InvariantCulture))] : [0f, 12f, 21f, 27f, 36f, 42f, 48f, 54f, 60f, 72f, 78f, 87f];

    [Theory, InlineData(1u), InlineData(2u), InlineData(3u), InlineData(4u)]
    public void The_rasterised_garden_is_a_stable_picture(uint load)
    {
        if (NoGpu) return;
        using var s = new D3D12Session(GpuDevices.Resolve("")!);
        var g = new GardenGpu(s, GardenScene.Embedded, GardenScene.Mode.Raster);
        using var r = new GardenRaster(s, g, W, H, [], load);
        Check(r, $"garden-raster-load{load}");
        if (Environment.GetEnvironmentVariable("MAZESTA_RENDER_DIR") is { Length: > 0 } dir && r.Level.ReflectionDivisor > 0)
        {
            r.Capture(0); var (px, w, h) = r.ReflectionImage();
            Png.Write(Path.Combine(dir, $"garden-raster-load{load}-reflection.png"), px, w, h);
        }
    }

    [Fact] public void The_ray_traced_garden_is_a_stable_picture()
    {
        if (NoGpu || !GpuFeatures.SupportsInlineRayTracing(GpuDevices.Resolve("")!)) return;
        using var s = new D3D12Session(GpuDevices.Resolve("")!);
        var g = new GardenGpu(s, GardenScene.Embedded, GardenScene.Mode.RayTraced);
        using var r = new GardenRay(s, g, W, H, []);
        Check(r, "garden-ray");
    }

    /// <summary>Not a check but the tool that makes Scene\garden.light: with MAZESTA_BAKE_LIGHT naming a file, the light bounced round
    /// the embedded garden is worked out by the ray tracer and written there (see <see cref="GardenLightBaker"/>). Build again afterwards.</summary>
    [Fact] public void Bakes_the_garden_s_bounced_light_when_asked()
    {
        if (Environment.GetEnvironmentVariable("MAZESTA_BAKE_LIGHT") is not { Length: > 0 } to || NoGpu || !GpuFeatures.SupportsInlineRayTracing(GpuDevices.Resolve("")!)) return;
        using var s = new D3D12Session(GpuDevices.Resolve("")!);
        var volume = GardenLightBaker.Bake(s, GardenScene.Embedded);
        using var f = File.Create(to); volume.Write(f);
    }

    private static void Check(GardenRenderer r, string name)
    {
        var a = r.Capture(1.234f); var b = r.Capture(1.234f);
        Assert.Equal(a, b);
        // frames shown one after another build on each other (the ray tracer gathers light across them): a check frame drawn after
        // them must still be the first one's bits
        for (int k = 0; k < 4; k++) r.Capture(2f + k / 30f, live: true);
        Assert.Equal(a, r.Capture(1.234f));
        Assert.True(a.Distinct().Count() > 2000, "the frame is nearly one colour");
        string? dir = Environment.GetEnvironmentVariable("MAZESTA_RENDER_DIR");
        if (dir is { Length: > 0 })
            foreach (float t in Times)
            {
                Png.Write(Path.Combine(dir, FormattableString.Invariant($"{name}-t{t:00.##}.png")), r.Capture(t), r.Width, r.Height);
                if (Environment.GetEnvironmentVariable("MAZESTA_RENDER_LIVE") is not { Length: > 0 }) continue;
                // also as the last of ten frames shown a thirtieth of a second apart: what the test's window shows
                uint[] shown = []; for (int k = 9; k >= 0; k--) shown = r.Capture(t - k / 30f, live: true);
                Png.Write(Path.Combine(dir, FormattableString.Invariant($"{name}-t{t:00.##}-shown.png")), shown, r.Width, r.Height);
            }
        // MAZESTA_RENDER_TIME: also how long a frame takes, round the whole walk (drawn off screen and read back: slower than the test's own window)
        if (dir is { Length: > 0 } && Environment.GetEnvironmentVariable("MAZESTA_RENDER_TIME") is { Length: > 0 })
        {
            const int n = 96; var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < n; k++) r.Capture(k * GardenCamera.Loop / n);
            File.AppendAllText(Path.Combine(dir, "timing.txt"), $"{name} {r.Width}x{r.Height}: {sw.Elapsed.TotalMilliseconds / n:F2} ms a frame, {n} frames round the walk" + Environment.NewLine);
        }
    }
}

/// <summary>A minimal RGBA PNG writer for the render checks.</summary>
internal static class Png
{
    public static void Write(string path, uint[] rgba, int width, int height)
    {
        var raw = new byte[(width * 4 + 1) * height];
        for (int y = 0; y < height; y++)
        {
            raw[y * (width * 4 + 1)] = 0;
            Buffer.BlockCopy(rgba, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);
        }
        using var f = File.Create(path);
        f.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Chunk(f, "IHDR", [.. Be(width), .. Be(height), 8, 6, 0, 0, 0]);
        using (var ms = new MemoryStream()) { using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(raw); Chunk(f, "IDAT", ms.ToArray()); }
        Chunk(f, "IEND", []);
    }
    private static byte[] Be(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
    private static void Chunk(Stream s, string type, byte[] data)
    {
        s.Write(Be(data.Length)); var t = System.Text.Encoding.ASCII.GetBytes(type); s.Write(t); s.Write(data);
        uint c = 0xFFFFFFFF; foreach (byte x in t.Concat(data)) { c ^= x; for (int k = 0; k < 8; k++) c = (c >> 1) ^ (0xEDB88320u & (uint)-(int)(c & 1)); }
        s.Write(Be((int)~c));
    }
}
