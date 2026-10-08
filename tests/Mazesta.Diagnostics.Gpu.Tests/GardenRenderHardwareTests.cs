using System.IO.Compression; using ComputeSharp; using Xunit;
using Mazesta.Diagnostics.Gpu.Benchmarks; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

/// <summary>Draws the garden on the real GPU. A frame drawn twice at one moment must be the same bits (the check frames rely on it), and
/// the picture must be a picture (sky above, lit garden below, not one flat colour). With MAZESTA_RENDER_DIR set, the frames are also
/// written there as PNG files to look at (MAZESTA_RENDER_WIDTH: how wide, 960 unless said; MAZESTA_RENDER_TIMES: at which moments;
/// MAZESTA_RENDER_VIEW: from one place instead of the walk's, as six numbers - the eye, then what it looks at).</summary>
[Trait("Category", "Hardware")]
public class GardenRenderHardwareTests
{
    private static bool NoGpu => GpuDevices.Resolve("") is null;
    private static int W => int.TryParse(Environment.GetEnvironmentVariable("MAZESTA_RENDER_WIDTH"), out int w) && w is >= 320 and <= 3840 ? w / 16 * 16 : 960;
    private static int H => W * 9 / 16;
    /// <summary>The moments drawn for looking at: round the walk (the garden, the terrace, the hall's rooms), or those MAZESTA_RENDER_TIMES lists.</summary>
    private static float[] Times => Environment.GetEnvironmentVariable("MAZESTA_RENDER_TIMES") is { Length: > 0 } list
        ? [.. list.Split(',').Select(t => float.Parse(t, System.Globalization.CultureInfo.InvariantCulture))] : [0f, 15f, 30f, 48f, 54f, 66f, 72f, 80f, 90f, 96f, 102f, 120f, 141f];

    [Theory, InlineData(1u), InlineData(2u), InlineData(3u), InlineData(4u)]
    public void The_rasterised_garden_is_a_stable_picture(uint load)
    {
        if (NoGpu) return;
        using var s = new D3D12Session(GpuDevices.Resolve("")!);
        var g = new GardenGpu(s, GardenScene.Embedded);
        using var r = new GardenRaster(s, g, W, H, [], load);
        Check(r, $"garden-raster-load{load}");
        if (Environment.GetEnvironmentVariable("MAZESTA_RENDER_DIR") is { Length: > 0 } dir && r.Level.ReflectionDivisor > 0)
        {
            r.Capture(0); var (px, w, h) = r.ReflectionImage();
            Png.Write(Path.Combine(dir, $"garden-raster-load{load}-reflection.png"), px, w, h);
        }
    }

    [Theory, InlineData(1u), InlineData(3u)]
    public void Skipping_what_the_camera_cannot_see_changes_no_pixel(uint load)
    {
        if (NoGpu) return;
        using var s = new D3D12Session(GpuDevices.Resolve("")!);
        var g = new GardenGpu(s, GardenScene.Embedded);
        using var r = new GardenRaster(s, g, W, H, [], load);
        try
        {
            foreach (float t in Times)
            {
                GardenRaster.ViewCulling = true; var culled = r.Capture(t);
                GardenRaster.ViewCulling = false; var whole = r.Capture(t);
                Assert.True(culled.AsSpan().SequenceEqual(whole), $"the picture at {t} s differs with the unseen skipped");
            }
        }
        finally { GardenRaster.ViewCulling = true; }
    }

    /// <summary>Mean absolute Laplacian of the luma: how much fine detail a picture holds.</summary>
    private static double Detail(uint[] px, int w, int h)
    {
        double sum = 0; static double Y(uint c) => 0.299 * (c & 255) + 0.587 * ((c >> 8) & 255) + 0.114 * ((c >> 16) & 255);
        for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++) sum += Math.Abs(4 * Y(px[y * w + x]) - Y(px[y * w + x - 1]) - Y(px[y * w + x + 1]) - Y(px[(y - 1) * w + x]) - Y(px[(y + 1) * w + x]));
        return sum / ((w - 2) * (h - 2));
    }

    [Fact]
    public void Sharpening_holds_more_fine_detail_than_off_and_the_scaler_at_77_percent_not_much_less()
    {
        if (NoGpu) return;
        using var s = new D3D12Session(GpuDevices.Resolve("")!); var g = new GardenGpu(s, GardenScene.Embedded);
        double Measure(string name) { var nis = NisMode.Parse(name); var (w, h) = nis.Render(W, H); using var r = new GardenRaster(s, g, w, h, [], 3, false, nis, W, H); return Detail(r.Capture(1.234f), r.OutWidth, r.OutHeight); }
        double off = Measure("off"), sharp = Measure("sharpen"), quality = Measure("quality");
        Console.WriteLine($"detail off {off:F3}  sharpen {sharp:F3}  quality {quality:F3}");
        Assert.True(sharp > off * 1.05, $"sharpen {sharp} off {off}");
        Assert.True(quality > off * 0.85, $"quality {quality} off {off}");
    }

    [Theory, InlineData("sharpen"), InlineData("quality"), InlineData("performance")]
    public void The_garden_finished_by_NVIDIA_Image_Scaling_is_a_stable_picture_of_the_window_s_size(string name)
    {
        if (NoGpu) return;
        using var s = new D3D12Session(GpuDevices.Resolve("")!);
        var g = new GardenGpu(s, GardenScene.Embedded); var nis = NisMode.Parse(name); var (w, h) = nis.Render(W, H);
        Assert.Equal(nis.Scales, w < W);
        using var r = new GardenRaster(s, g, w, h, [], 3, false, nis, W, H);
        Assert.Equal((W, H), (r.OutWidth, r.OutHeight));
        Assert.Equal(W * H, r.Capture(1.234f).Length);
        Check(r, $"garden-nis-{name}");
    }

    [Theory, InlineData(1u), InlineData(3u)]
    public void The_garden_with_ray_tracing_on_is_a_stable_picture(uint load)
    {
        if (NoGpu || !GpuFeatures.SupportsInlineRayTracing(GpuDevices.Resolve("")!)) return;
        using var s = new D3D12Session(GpuDevices.Resolve("")!);
        var g = new GardenGpu(s, GardenScene.Embedded);
        using var r = new GardenRaster(s, g, W, H, [], load, rayTraced: true);
        Check(r, $"garden-rays-load{load}");
    }

    /// <summary>Not a check but the tool that makes Scene\garden.light: with MAZESTA_BAKE_LIGHT naming a file, the light bounced round
    /// the embedded garden is worked out with rays and written there (see <see cref="GardenLightBaker"/>). Build again afterwards.</summary>
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
        if (!a.AsSpan().SequenceEqual(b) && Environment.GetEnvironmentVariable("MAZESTA_RENDER_DIR") is { Length: > 0 } to)
        {   // the two frames that should have been one, to look at
            Png.Write(Path.Combine(to, $"{name}-first.png"), a, r.OutWidth, r.OutHeight); Png.Write(Path.Combine(to, $"{name}-second.png"), b, r.OutWidth, r.OutHeight);
        }
        Assert.Equal(a, b);
        // frames shown one after another build on each other (maps drawn every few frames): a check frame drawn after
        // them must still be the first one's bits
        for (int k = 0; k < 4; k++) r.Capture(2f + k / 30f, live: true);
        Assert.Equal(a, r.Capture(1.234f));
        Assert.True(a.Distinct().Count() > 2000, "the frame is nearly one colour");
        string? dir = Environment.GetEnvironmentVariable("MAZESTA_RENDER_DIR");
        if (dir is { Length: > 0 } && Environment.GetEnvironmentVariable("MAZESTA_RENDER_VIEW") is { Length: > 0 } view)
        {
            var v = view.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            GardenCamera.Fixed = (new(v[0], v[1], v[2]), new(v[3], v[4], v[5]));
        }
        try { Look(r, name, dir); } finally { GardenCamera.Fixed = null; }
    }

    private static void Look(GardenRenderer r, string name, string? dir)
    {
        if (dir is { Length: > 0 })
            foreach (float t in Times)
            {
                Png.Write(Path.Combine(dir, FormattableString.Invariant($"{name}-t{t:00.##}.png")), r.Capture(t), r.OutWidth, r.OutHeight);
                if (Environment.GetEnvironmentVariable("MAZESTA_RENDER_LIVE") is not { Length: > 0 }) continue;
                // also as the last of ten frames shown a thirtieth of a second apart: what the test's window shows
                uint[] shown = []; for (int k = 9; k >= 0; k--) shown = r.Capture(t - k / 30f, live: true);
                Png.Write(Path.Combine(dir, FormattableString.Invariant($"{name}-t{t:00.##}-shown.png")), shown, r.OutWidth, r.OutHeight);
            }
        // MAZESTA_RENDER_TIME: also how long a frame takes, round the whole walk, each drawn as one of the frames being shown (off screen and read back: slower than the test's own window)
        if (dir is { Length: > 0 } && Environment.GetEnvironmentVariable("MAZESTA_RENDER_TIME") is { Length: > 0 })
        {
            const int n = 96; var sw = System.Diagnostics.Stopwatch.StartNew(); var parts = new double[12];
            for (int k = 0; k < n; k++) { var one = System.Diagnostics.Stopwatch.StartNew(); r.Capture(k * GardenCamera.Loop / n, live: true); parts[k * 12 / n] += one.Elapsed.TotalMilliseconds * 12 / n; }
            File.AppendAllText(Path.Combine(dir, "timing.txt"), $"{name} {r.Width}x{r.Height}: {sw.Elapsed.TotalMilliseconds / n:F2} ms a frame, {n} frames round the walk (its twelfths: {string.Join(", ", parts.Select(p => p.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)))})" + Environment.NewLine);
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
