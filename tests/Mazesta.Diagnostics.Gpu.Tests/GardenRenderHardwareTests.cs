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
        int frames = int.Parse(Environment.GetEnvironmentVariable("MAZESTA_FRAMES") ?? "1");
        using var s = new D3D12Session(GpuDevices.Resolve("")!, frames); int W2 = int.Parse(Environment.GetEnvironmentVariable("MAZESTA_W") ?? "1920"), H2 = W2 * 9 / 16; using var w = new TestWindow("frame rate", W2, H2, Environment.GetEnvironmentVariable("MAZESTA_FULL") is { Length: > 0 });
        using var v = new SceneView(s, w, W2, H2, rays, 3, null, null, Environment.GetEnvironmentVariable("MAZESTA_WEATHER") == "on" ? WeatherLevel.Standard : null);
        var sw = System.Diagnostics.Stopwatch.StartNew(); long n = 0; double from = 0, gpu0 = 0, cpu = 0; long gpuN0 = 0;
        while (sw.Elapsed.TotalSeconds < (Environment.GetEnvironmentVariable("MAZESTA_FULL") is null ? 8 : 20))
        {
            w.Pump(); v.Present((float)sw.Elapsed.TotalSeconds + float.Parse(Environment.GetEnvironmentVariable("MAZESTA_T0") ?? "0"));
            if (sw.Elapsed.TotalSeconds < 2) { n = 0; from = sw.Elapsed.TotalSeconds; gpu0 = s.GpuSeconds; gpuN0 = s.GpuFrames; cpu = 0; } else { n++; cpu += s.LastRecordSeconds; }
        }
        double wall = sw.Elapsed.TotalSeconds - from; v.Drain(); double gpu = (s.GpuSeconds - gpu0) / Math.Max(1, s.GpuFrames - gpuN0);
        File.AppendAllText(Path.Combine(dir, "timing.txt"), FormattableString.Invariant($"shown loop rays={rays} frames={frames}: {n / wall:F1} FPS, {wall * 1000 / n:F2} ms a frame; card {gpu * 1000:F2} ms ({gpu * n / wall * 100:F1} % busy), processor {cpu * 1000 / n:F2} ms") + Environment.NewLine);
    }

    /// <summary>The frames shown one after another, sent to the card in pieces (as the test window does, so the card works while the rest is recorded), are the same picture (to the few pixels two runs of shown frames differ by) as the
    /// same frames in one list - at moments far apart, so a piece that draws nothing (and leaves the picture of the frame before) cannot pass.</summary>
    [Theory, InlineData(1u, false), InlineData(3u, false), InlineData(1u, true), InlineData(3u, true)]
    public void Frames_recorded_in_pieces_are_the_same_pictures(uint load, bool rays)
    {
        if (NoGpu || (rays && !GpuFeatures.SupportsInlineRayTracing(GpuDevices.Resolve("")!))) return;
        using var s = new D3D12Session(GpuDevices.Resolve("")!); var g = new GardenGpu(s, GardenScene.Embedded);
        float[] times = [1f, 1.03f, 9f, 9.03f, 40f]; var whole = new List<uint[]>(); var pieces = new List<uint[]>();
        using (var r = new GardenRaster(s, g, W, H, [], load, rayTraced: rays)) { r.Capture(0.5f); foreach (float t in times) whole.Add(r.Capture(t, live: true)); }
        using (var r = new GardenRaster(s, g, W, H, [], load, rayTraced: rays)) { r.Capture(0.5f); foreach (float t in times) pieces.Add(r.Capture(t, live: true, chunked: true)); }
        for (int k = 0; k < times.Length; k++)
        {
            int d = 0, y0 = H, y1 = 0; for (int i = 0; i < whole[k].Length; i++) if (whole[k][i] != pieces[k][i]) { d++; y0 = Math.Min(y0, i / W); y1 = Math.Max(y1, i / W); }
            Assert.True(d < W * H / 50, $"the frame at {times[k]} s differs when recorded in pieces: {d} pixels, rows {y0}-{y1} of {H}");   // (two runs of the same shown frames differ by up to a percent themselves: the frames build on each other; a piece that draws nothing leaves all of them)
        }
        Assert.False(whole[0].AsSpan().SequenceEqual(whole[2]), "the picture does not change with time");
    }

    /// <summary>With two frames in flight (the processor writing one frame's constants, movers, lamps and instances while the card still reads the one before) the frames shown one after another are the same
    /// pictures as with one: a buffer the two frames shared would show as a wrong or flickering picture here. The frames far apart in time, so one that draws nothing cannot pass.</summary>
    [Theory, InlineData(1u, false), InlineData(3u, false), InlineData(1u, true), InlineData(3u, true)]
    public void Two_frames_in_flight_are_the_same_pictures_as_one(uint load, bool rays)
    {
        if (NoGpu || (rays && !GpuFeatures.SupportsInlineRayTracing(GpuDevices.Resolve("")!))) return;
        float[] times = [.. Enumerable.Range(0, 18).Select(k => 1f + k * 7.5f)]; var shown = new List<uint[]>[2];   // (far apart: the lamps, the movers, the fountain and the light differ from one frame to the next, so a buffer the two share shows)
        for (int mode = 0; mode < 2; mode++)
        {
            using var s = new D3D12Session(GpuDevices.Resolve("")!, mode + 1); var g = new GardenGpu(s, GardenScene.Embedded, frames: s.Frames);
            using var r = new GardenRaster(s, g, W, H, [], load, rayTraced: rays); var check = r.Capture(1.234f); shown[mode] = [];
            // every frame is copied out inside its own commands and left running: with two frames in flight the next is being recorded (its constants, movers, lamps and instances written) while this one is drawn
            var pending = new List<Func<uint[]>>();
            foreach (float t in times) { var keep = r.DrawLiveAndKeep(t); if (mode == 0) shown[mode].Add(keep()); else pending.Add(keep); }
            if (mode == 1) { foreach (var keep in pending) shown[mode].Add(keep.Invoke()); }
            var later = r.Capture(1.234f); int off = 0; for (int i = 0; i < check.Length; i++) if (check[i] != later[i]) off++;
            Assert.True(Near(check, later, W, H), $"with {s.Frames} in flight the check frame after {times.Length} shown frames differs from the one before them in {off} pixels");
        }
        for (int k = 0; k < shown[0].Count; k++)
        {
            int d = 0; for (int i = 0; i < shown[0][k].Length; i++) if (shown[0][k][i] != shown[1][k][i]) d++;
            Assert.True(d < W * H / 50, $"picture {k} differs with two frames in flight: {d} of {W * H} pixels");
        }
    }

    /// <summary>A check frame is the same bits whichever frame slot it is drawn in (a slot's own copy of a buffer that holds something the frame needs but was never written would show here).</summary>
    [Theory, InlineData(1u, false), InlineData(3u, false), InlineData(3u, true)]
    public void A_check_frame_is_the_same_bits_in_every_slot(uint load, bool rays)
    {
        if (NoGpu || (rays && !GpuFeatures.SupportsInlineRayTracing(GpuDevices.Resolve("")!))) return;
        using var s = new D3D12Session(GpuDevices.Resolve("")!, 2); var g = new GardenGpu(s, GardenScene.Embedded, frames: 2);
        using var r = new GardenRaster(s, g, W, H, [], load, rayTraced: rays);
        var first = r.Capture(1.234f);
        for (int k = 0; k < 4; k++)
        {
            s.NextSlot(); var again = r.Capture(1.234f);
            int d = 0; for (int i = 0; i < first.Length; i++) if (first[i] != again[i]) d++;
            Assert.True(d == 0, $"the check frame drawn in slot {s.Slot} (try {k}) differs from the first one in {d} of {first.Length} pixels");
        }
    }

    /// <summary>Boxed down to 16 x 16 mean colours, as the benchmark's check frame is: no block more than 2 of 255 levels from its twin.</summary>
    private static bool Near(uint[] a, uint[] b, int w, int h)
    {
        var sum = new double[16 * 16 * 3 * 2]; for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int bk = (y * 16 / h * 16 + x * 16 / w) * 3; for (int c = 0; c < 3; c++) { sum[bk + c] += a[y * w + x] >> c * 8 & 255; sum[768 + bk + c] += b[y * w + x] >> c * 8 & 255; } }
        return Enumerable.Range(0, 768).All(k => Math.Abs(sum[k] - sum[768 + k]) / (w * h / 256.0) <= 2);
    }

    /// <summary>The check frame drawn after frames were shown one after another, with either number of frames in flight, is the picture drawn before them (what the benchmark and the visual test rely on).</summary>
    [Theory, InlineData(1, false), InlineData(2, false), InlineData(2, true)]
    public void The_check_frame_after_shown_frames_is_the_one_before_them(int frames, bool overlay)
    {
        if (NoGpu) return;
        int wide = 960, high = 540;
        using var s = new D3D12Session(GpuDevices.Resolve("")!, frames); using var w = new TestWindow("check", wide, high, false);
        using var v = new SceneView(s, w, wide, high, false, 3, null, null, null); v.Overlay.Visible = overlay;
        var before = v.Renderer.Capture(1.234f); var clock = System.Diagnostics.Stopwatch.StartNew(); long shown = 0;
        while (clock.Elapsed.TotalSeconds < 2) { w.Pump(); shown++; v.Present((float)clock.Elapsed.TotalSeconds); }
        v.Drain();
        Assert.True(Near(before, v.Renderer.Capture(1.234f), wide, high), $"after {shown} shown frames the check frame is another picture than the one before them");
    }

    /// <summary>After many frames run one behind the other (in flight, two at a time) a check frame is still the bits of the first one.</summary>
    [Theory, InlineData(1, 1u), InlineData(2, 1u), InlineData(2, 3u)]
    public void Many_frames_in_flight_leave_the_check_frame_alone(int frames, uint load)
    {
        if (NoGpu) return;
        using var s = new D3D12Session(GpuDevices.Resolve("")!, frames); var g = new GardenGpu(s, GardenScene.Embedded, frames: frames);
        // three render targets in turn, as a swap chain's buffers are
        var targets = Enumerable.Range(0, 3).Select(_ => s.Device.CreateCommittedResource(Vortice.Direct3D12.HeapType.Default, Vortice.Direct3D12.ResourceDescription.Texture2D(Vortice.DXGI.Format.R8G8B8A8_UNorm, (uint)W, (uint)H, 1, 1, flags: Vortice.Direct3D12.ResourceFlags.AllowRenderTarget),
            Vortice.Direct3D12.ResourceStates.Common, new Vortice.Direct3D12.ClearValue(Vortice.DXGI.Format.R8G8B8A8_UNorm, new Vortice.Mathematics.Color4(0, 0, 0, 1)))).ToArray();
        using var r = new GardenRaster(s, g, W, H, targets, load); var check = r.Capture(1.234f);
        for (int k = 0; k < 300; k++) { float t = k / 120f; int to = k % 3; s.Run(l => r.Draw(l, t, to), wait: false, timed: true); s.NextSlot(); }
        s.Finish(); var later = r.Capture(1.234f); int off = 0; for (int i = 0; i < check.Length; i++) if (check[i] != later[i]) off++;
        Assert.True(Near(check, later, W, H), $"with {frames} in flight the check frame after 300 frames differs from the one before them in {off} pixels");
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
