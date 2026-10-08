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

    /// <summary>With MAZESTA_RENDER_TIME set: the frame rate of the loop the test window runs (draw, show, repeat; no checks between), which is what the card's busy share comes from.</summary>
    [Theory, InlineData(false), InlineData(true)]
    public void The_shown_loop_frame_rate_is_written_when_asked(bool rays)
    {
        if (Environment.GetEnvironmentVariable("MAZESTA_RENDER_TIME") is not { Length: > 0 } || Environment.GetEnvironmentVariable("MAZESTA_RENDER_DIR") is not { Length: > 0 } dir || NoGpu || (rays && !GpuFeatures.SupportsInlineRayTracing(GpuDevices.Resolve("")!))) return;
        using var s = new D3D12Session(GpuDevices.Resolve("")!); using var w = new TestWindow("frame rate", 1920, 1080, false);
        using var v = new SceneView(s, w, 1920, 1080, rays, 3, null, null);
        var sw = System.Diagnostics.Stopwatch.StartNew(); long n = 0; double from = 0;
        while (sw.Elapsed.TotalSeconds < 8) { w.Pump(); v.Present((float)sw.Elapsed.TotalSeconds); if (sw.Elapsed.TotalSeconds < 2) { n = 0; from = sw.Elapsed.TotalSeconds; } else n++; }
        File.AppendAllText(Path.Combine(dir, "timing.txt"), FormattableString.Invariant($"shown loop rays={rays}: {n / (sw.Elapsed.TotalSeconds - from):F1} FPS, {(sw.Elapsed.TotalSeconds - from) * 1000 / n:F2} ms a frame") + Environment.NewLine);
    }

    /// <summary>A frame sent to the card in pieces (as the test window does, so the card works while the rest is recorded) is the same bits as the same frame in one list.</summary>
    [Theory, InlineData(1u, false), InlineData(3u, false), InlineData(1u, true), InlineData(3u, true)]
    public void A_frame_recorded_in_pieces_is_the_same_picture(uint load, bool rays)
    {
        if (NoGpu || (rays && !GpuFeatures.SupportsInlineRayTracing(GpuDevices.Resolve("")!))) return;
        using var s = new D3D12Session(GpuDevices.Resolve("")!);
        var g = new GardenGpu(s, GardenScene.Embedded);
        using var r = new GardenRaster(s, g, W, H, [], load, rayTraced: rays);
        foreach (float t in new[] { 1.234f, 40f, 100f })
        {
            var whole = r.Capture(t); var pieces = r.Capture(t, chunked: true);
            Assert.True(whole.AsSpan().SequenceEqual(pieces), $"the frame at {t} s differs when recorded in pieces");
        }
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
            const int n = 96; var sw = System.Diagnostics.Stopwatch.StartNew(); var parts = new double[12]; double record = 0;
            for (int k = 0; k < n; k++) { var one = System.Diagnostics.Stopwatch.StartNew(); r.Capture(k * GardenCamera.Loop / n, live: true); record += r.LastRecordSeconds * 1000 / n; parts[k * 12 / n] += one.Elapsed.TotalMilliseconds * 12 / n; }
            File.AppendAllText(Path.Combine(dir, "timing.txt"), $"{name} {r.Width}x{r.Height}: {sw.Elapsed.TotalMilliseconds / n:F2} ms a frame ({record:F2} ms of it the processor recording), {n} frames round the walk (its twelfths: {string.Join(", ", parts.Select(p => p.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)))})" + Environment.NewLine);
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
