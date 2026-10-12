using Mazesta.Core.Hardware; using System.Diagnostics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Benchmarks; using Vortice.DXGI;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>What a video run measures: decoding, encoding, or both (the benchmark).</summary>
public enum VideoWork { Both, Decode, Encode }

/// <summary>
/// The card's video engines (NVENC/NVDEC, AMD's VCN, Intel's Quick Sync), through Windows' Media Foundation - the path video players, editors and
/// recorders on Windows take. <b>Decode</b>: short clips shipped with the program (<see cref="VideoFormat.Clips"/>: H.264, HEVC, VP9 and AV1 at
/// Full HD, 4K and 8K, 8 and 10 bits, 4:2:0 and 4:2:2) are played again and again by the card's decoder through Direct3D 11, every pass checked to
/// give back all of its frames. A form the card has no decoder for is said so; one Windows decodes on the processor instead is not counted as the
/// card's. <b>Encode</b>: moving synthetic frames are encoded (<see cref="VideoFormat.Encodes"/>) at a fixed bit rate, and the file made is decoded
/// back and checked to hold every frame. Frames are fed from the processor, so a very fast encoder is measured with that feed in the way; the
/// numbers compare cards, they are not a maker's peak figure.
/// </summary>
public sealed class GpuVideoBenchmark : IBenchmark, ITestAvailability
{
    public static readonly TestDefinition Spec = new(new TestId("bench.gpu.video"), "Bench_Gpu_Video", 120, [GpuDevices.Option]);
    public TestDefinition Definition => Spec;
    public HardwareKind Component => HardwareKind.Gpu;
    public Unavailability? CheckAvailability(TestOptions options) => GpuFeatures.GpuAvailability(options);
    private const int Fps = 60, Variants = 8;

    public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => RunAsync(Spec, VideoWork.Both, request, ct);

    /// <summary>The run for a benchmark or a test: the formats each is measured in, and for a test whether everything checked out.</summary>
    internal static Task<BenchmarkResult> RunAsync(TestDefinition spec, VideoWork work, TestExecutionRequest request, CancellationToken ct)
        => GpuBenchmark.RunAsync(spec, request, s => Run(s, work, request, ct), (3840, 2160));

    private static (List<BenchmarkMetric>, string) Run(D3D12Session s, VideoWork work, TestExecutionRequest request, CancellationToken ct)
    {
        List<(VideoFormat Format, string? Encoder)> encodes = work == VideoWork.Decode ? [] : [.. VideoFormat.Encodes.Select(f => (f, Mf.HardwareEncoder(f.Subtype)))];
        IReadOnlyList<(VideoFormat Format, string Resource)> clips = work == VideoWork.Encode ? [] : VideoFormat.Clips;
        if (work == VideoWork.Encode && encodes.All(e => e.Encoder is null)) throw new GpuUnsupportedException($"{s.AdapterName} has no hardware video encoder that Windows offers (H.264, HEVC or AV1).");

        nint device = 0; var metrics = new List<BenchmarkMetric>(); var notes = new List<string>(); var files = new List<string>(); int wrong = 0;
        Mf.Check(Mf.MFStartup(Mf.Version, 0), "MFStartup");
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory4>();
            long luid = s.Device.AdapterLuid;
            using var adapter = factory.EnumAdapterByLuid<IDXGIAdapter1>(System.Runtime.CompilerServices.Unsafe.As<long, Vortice.Luid>(ref luid));
            var manager = Mf.DeviceManager(adapter.NativePointer, out device);
            int steps = encodes.Count(e => e.Encoder is not null) + clips.Count, step = 0;
            double phase = Math.Max(1.5, request.DurationSeconds / (double)Math.Max(1, steps));
            Action<double> Progress() { int at = step++; return p => request.Report((at + p) / steps); }

            foreach (var (f, encoder) in encodes)
            {
                if (encoder is null) { notes.Add($"{f.Label} encode: no hardware encoder"); continue; }
                string file = Path.Combine(Path.GetTempPath(), $"mazesta-video-{Guid.NewGuid():N}.mp4"); files.Add(file);
                try
                {
                    var (fps, count) = Encode(f, file, manager, phase, Progress(), ct);
                    metrics.Add(new(f.MetricKey(encode: true), fps, "FPS"));
                    // The file made is played back: every frame the encoder was given must be in it.
                    var back = Decode(file, manager, 0, f, _ => { }, ct);
                    if (back.PassFrames != count) { wrong++; notes.Add($"{f.Label} encode: {count} frames encoded but {back.PassFrames} came back"); }
                    else notes.Add($"{f.Label} encode: {fps:F0} fps, {count} frames verified by playing them back ({encoder})");
                }
                catch (Exception e) when (e is COMException or InvalidOperationException or InvalidCastException or IOException)
                { notes.Add($"{f.Label} encode: not measured ({e.Message})"); }
            }

            foreach (var (f, resource) in clips)
            {
                string file = Path.Combine(Path.GetTempPath(), $"mazesta-clip-{Guid.NewGuid():N}.mp4"); files.Add(file);
                var progress = Progress();
                try
                {
                    using (var from = typeof(GpuVideoBenchmark).Assembly.GetManifestResourceStream(resource)!) using (var to = File.Create(file)) from.CopyTo(to);
                    var r = Decode(file, manager, phase, f, progress, ct);
                    if (!r.Hardware) { notes.Add($"{f.Label} decode: decoded by the processor, not the card (the card has no decoder for it)"); continue; }
                    metrics.Add(new(f.MetricKey(encode: false), r.Fps, "FPS"));
                    if (r.Short > 0) { wrong++; notes.Add($"{f.Label} decode: {r.Short} pass(es) gave fewer than {VideoFormat.ClipFrames} frames"); }
                    else notes.Add($"{f.Label} decode: {r.Fps:F0} fps, every pass all {VideoFormat.ClipFrames} frames");
                }
                catch (Exception e) when (e is COMException or InvalidOperationException or InvalidCastException or IOException)
                { notes.Add($"{f.Label} decode: not supported here ({e.Message})"); }
            }
        }
        finally
        {
            if (device != 0) Marshal.Release(device);
            Mf.MFShutdown();
            foreach (var f in files) try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        if (metrics.Count == 0) throw new GpuUnsupportedException("No video form could be measured on this GPU: " + string.Join("; ", notes));
        if (wrong > 0) throw new GpuWrongResultException($"the video engine lost frames in {wrong} form(s): " + string.Join("; ", notes));
        return (metrics, string.Join("; ", ["Media Foundation hardware video (DXVA, Direct3D 11)", .. notes]));
    }

    /// <summary>Eight different frames of moving gradient and fine noise (NV12, or P010 for 10 bits), cycled so that the encoder always has something
    /// that changed to code.</summary>
    private static byte[][] Frames(int width, int height, int bits)
    {
        var frames = new byte[Variants][]; var random = new Random(7); int bytes = bits > 8 ? 2 : 1;
        for (int f = 0; f < Variants; f++)
        {
            var frame = new byte[width * height * 3 / 2 * bytes];
            void Put(int i, int v) { if (bytes == 1) frame[i] = (byte)v; else { frame[2 * i] = 0; frame[2 * i + 1] = (byte)v; } }   // P010: the value in the high bits
            for (int y = 0; y < height; y++)
            {
                int row = y * width; int shade = y * 255 / height;
                for (int x = 0; x < width; x++) Put(row + x, (x * 255 / width + shade + f * 24 + (random.Next() & 15)) & 255);
            }
            for (int i = width * height; i < width * height * 3 / 2; i += 2) { Put(i, 96 + f * 8 + (i >> 12) % 64); Put(i + 1, 160 - f * 8 - (i >> 13) % 48); }
            frames[f] = frame;
        }
        return frames;
    }

    private static unsafe IMFSample Sample(byte[] frame, long time)
    {
        Mf.Check(Mf.MFCreateMemoryBuffer((uint)frame.Length, out var buffer), "MFCreateMemoryBuffer");
        buffer.Lock(out nint data, out _, out _);
        frame.AsSpan().CopyTo(new Span<byte>((void*)data, frame.Length));
        buffer.Unlock(); buffer.SetCurrentLength((uint)frame.Length);
        Mf.Check(Mf.MFCreateSample(out var sample), "MFCreateSample");
        sample.AddBuffer(buffer); sample.SetSampleTime(time); sample.SetSampleDuration(10_000_000L / Fps);
        return sample;
    }

    /// <summary>Bit rate by picture size: 40 Mbit/s at 4K, as the benchmark always had, and the same per pixel at Full HD.</summary>
    private static int Bitrate(VideoFormat f) => (int)(40_000_000L * f.Width * f.Height / (3840 * 2160));

    private static (double Fps, int Frames) Encode(VideoFormat f, string file, IMFDXGIDeviceManager manager, double seconds, Action<double> progress, CancellationToken ct)
    {
        var frames = Frames(f.Width, f.Height, f.Bits);
        Mf.Check(Mf.MFCreateAttributes(out var attributes, 3), "MFCreateAttributes");
        attributes.SetUINT32(Mf.EnableHardwareTransforms, 1); attributes.SetUnknown(Mf.SinkD3dManager, manager);
        Mf.Check(Mf.MFCreateSinkWriterFromURL(file, 0, attributes, out var writer), "MFCreateSinkWriterFromURL");
        var output = Mf.VideoType(f.Subtype, f.Width, f.Height, Fps, Bitrate(f));
        if (f.Bits > 8 && f.Codec == "hevc") output.SetUINT32(Mf.Mpeg2Profile, 2);   // eAVEncH265VProfile_Main_420_10
        writer.AddStream(output, out uint stream);
        writer.SetInputMediaType(stream, Mf.VideoType(f.Bits > 8 ? Mf.P010 : Mf.Nv12, f.Width, f.Height, Fps), null);
        writer.BeginWriting();
        writer.WriteSample(stream, Sample(frames[0], 0));   // the first frame starts the encoder: its set-up is not part of the rate
        var clock = Stopwatch.StartNew(); int count = 1;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            ct.ThrowIfCancellationRequested();
            var sample = Sample(frames[count % Variants], count * (10_000_000L / Fps)); writer.WriteSample(stream, sample); Mf.Release(sample); count++;
            if ((count & 15) == 0) progress(clock.Elapsed.TotalSeconds / seconds);
        }
        writer.FinalizeWriter(); clock.Stop();
        return ((count - 1) / clock.Elapsed.TotalSeconds, count);
    }

    /// <summary>A decode run: frames a second, the frames of one whole pass, how many passes came back short, and whether the frames were on the card
    /// (a hardware decoder hands back Direct3D surfaces; Windows' software decoders hand back memory).</summary>
    private sealed record Decoded(double Fps, long PassFrames, int Short, bool Hardware);

    /// <summary>Plays the file from the start, over and over by seeking back, for <paramref name="seconds"/> (once when zero).</summary>
    private static unsafe Decoded Decode(string file, IMFDXGIDeviceManager manager, double seconds, VideoFormat f, Action<double> progress, CancellationToken ct)
    {
        Mf.Check(Mf.MFCreateAttributes(out var attributes, 3), "MFCreateAttributes");
        attributes.SetUINT32(Mf.EnableHardwareTransforms, 1); attributes.SetUnknown(Mf.SourceD3dManager, manager);
        Mf.Check(Mf.MFCreateSourceReaderFromURL(file, attributes, out var reader), "MFCreateSourceReaderFromURL");
        // The decoder's own output for the form: 10 bits come as P010 (4:2:0) or Y210 (4:2:2), 8-bit 4:2:2 as YUY2; NV12 last, as some drivers convert.
        Guid[] outputs = (f.Bits > 8, f.Chroma == 422) switch { (true, true) => [Mf.Y210, Mf.P010], (true, false) => [Mf.P010, Mf.Nv12], (false, true) => [Mf.Yuy2, Mf.Nv12], _ => [Mf.Nv12] };
        Exception? refused = null; bool set = false;
        foreach (var o in outputs)
        {
            Mf.Check(Mf.MFCreateMediaType(out var wanted), "MFCreateMediaType");
            wanted.SetGUID(Mf.MajorType, Mf.Video); wanted.SetGUID(Mf.Subtype, o);
            try { reader.SetCurrentMediaType(Mf.FirstVideoStream, 0, wanted); set = true; break; } catch (COMException e) { refused = e; }
        }
        if (!set) throw new InvalidOperationException($"Windows found no decoder for it on this card ({refused?.Message})");
        long frames = 0, pass = 0, whole = 0; int passes = 0, shortPasses = 0; bool? hardware = null; var clock = Stopwatch.StartNew();
        var start = stackalloc long[3];   // a PROPVARIANT of VT_I8 = 0: the start of the file, in 100 ns units
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            reader.ReadSample(Mf.FirstVideoStream, 0, out _, out uint flags, out _, out nint sample);
            if (sample != 0) { hardware ??= Mf.OnCard(sample); pass++; Marshal.Release(sample); }
            if ((flags & Mf.EndOfStream) == 0) continue;
            if (pass == 0) throw new InvalidOperationException("the decoder produced no frames");
            passes++; frames += pass; if (passes == 1) whole = pass; else if (pass < whole) shortPasses++;
            progress(seconds > 0 ? Math.Min(1, clock.Elapsed.TotalSeconds / seconds) : 1);
            if (clock.Elapsed.TotalSeconds >= seconds) break;
            pass = 0; start[0] = 20; start[1] = 0; start[2] = 0;
            reader.SetCurrentPosition(Guid.Empty, (nint)start);
        }
        if (passes > 0 && whole < VideoFormat.ClipFrames && seconds > 0) shortPasses++;
        Mf.Release(reader);
        return new(frames / clock.Elapsed.TotalSeconds, whole, shortPasses, hardware ?? false);
    }
}
