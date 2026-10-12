using Mazesta.Core.Hardware; using System.Diagnostics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Benchmarks; using Vortice.DXGI;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>
/// The card's video engines (NVENC/NVDEC, AMD's VCN, Intel's Quick Sync), through Windows' Media Foundation - the path video editors and recorders
/// on Windows take. For each codec the card has a hardware encoder for (H.264, HEVC, AV1), a 3840x2160 stream of moving synthetic frames is
/// encoded at 40 Mbit/s for a share of the duration and the frames a second are measured; the file that makes is then decoded again and again,
/// by the card's decoder through Direct3D 11, for the same time. A codec the card cannot encode is not measured and said so (so AV1 decoding
/// on a card that decodes but does not encode AV1 is not tested: the stream to decode is made here). Frames are fed from the processor, so a
/// very fast encoder is measured with that feed in the way; the numbers compare cards, they are not a maker's peak figure.
/// </summary>
public sealed class GpuVideoBenchmark : IBenchmark, ITestAvailability
{
    public static readonly TestDefinition Spec = new(new TestId("bench.gpu.video"), "Bench_Gpu_Video", 60, [GpuDevices.Option]);
    public TestDefinition Definition => Spec;
    public HardwareKind Component => HardwareKind.Gpu;
    public Unavailability? CheckAvailability(TestOptions options) => GpuFeatures.GpuAvailability(options);
    private const int Width = 3840, Height = 2160, Fps = 60, Bitrate = 40_000_000, Variants = 8;

    private sealed record Codec(string Name, Guid Subtype, string EncodeKey, string DecodeKey);
    private static readonly Codec[] Codecs =
    [
        new("H.264", Mf.H264, "Bench_Gpu_Video_EncH264", "Bench_Gpu_Video_DecH264"),
        new("HEVC (H.265)", Mf.Hevc, "Bench_Gpu_Video_EncHevc", "Bench_Gpu_Video_DecHevc"),
        new("AV1", Mf.Av1, "Bench_Gpu_Video_EncAv1", "Bench_Gpu_Video_DecAv1"),
    ];

    public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => GpuBenchmark.RunAsync(Spec, request, s => Run(s, request, ct), (Width, Height));

    private static (List<BenchmarkMetric>, string) Run(D3D12Session s, TestExecutionRequest request, CancellationToken ct)
    {
        var available = Codecs.Select(c => (Codec: c, Encoder: Mf.HardwareEncoder(c.Subtype))).ToList();
        var usable = available.Where(a => a.Encoder is not null).ToList();
        if (usable.Count == 0) throw new GpuUnsupportedException($"{s.AdapterName} has no hardware video encoder that Windows offers (H.264, HEVC or AV1).");

        nint device = 0; var metrics = new List<BenchmarkMetric>(); var notes = new List<string>(); var files = new List<string>();
        Mf.Check(Mf.MFStartup(Mf.Version, 0), "MFStartup");
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory4>();
            long luid = s.Device.AdapterLuid;
            using var adapter = factory.EnumAdapterByLuid<IDXGIAdapter1>(System.Runtime.CompilerServices.Unsafe.As<long, Vortice.Luid>(ref luid));
            var manager = Mf.DeviceManager(adapter.NativePointer, out device);
            var frames = Frames();
            double phase = Math.Max(2, request.DurationSeconds / (2.0 * usable.Count)); int step = 0, steps = usable.Count * 2;
            foreach (var (codec, encoder) in available)
            {
                if (encoder is null) { notes.Add($"{codec.Name}: no hardware encoder"); continue; }
                string file = Path.Combine(Path.GetTempPath(), $"mazesta-video-{Guid.NewGuid():N}.mp4"); files.Add(file);
                try
                {
                    var (fps, count) = Encode(codec, file, frames, manager, phase, p => request.Report((step + p) / steps), ct); step++;
                    metrics.Add(new(codec.EncodeKey, fps, "FPS")); notes.Add($"{codec.Name} encoder: {encoder} ({count} frames)");
                    double dec = Decode(file, manager, phase, p => request.Report((step + p) / steps), ct); step++;
                    metrics.Add(new(codec.DecodeKey, dec, "FPS"));
                }
                catch (Exception e) when (e is COMException or InvalidOperationException or InvalidCastException or IOException)
                {
                    step = Math.Max(step, 2 * (available.TakeWhile(a => a.Codec != codec).Count(a => a.Encoder is not null) + 1)); notes.Add($"{codec.Name}: not measured ({e.Message})");
                }
            }
        }
        finally
        {
            if (device != 0) Marshal.Release(device);
            Mf.MFShutdown();
            foreach (var f in files) try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        if (metrics.Count == 0) throw new GpuUnsupportedException("No video codec could be measured on this GPU: " + string.Join("; ", notes));
        if (metrics.All(m => m.Key != "Bench_Gpu_Video_DecAv1")) notes.Add("AV1 decoding is not measured: the stream to decode is made by this card's own encoder");
        return (metrics, $"Media Foundation hardware encode and decode (DXVA, Direct3D 11) of 4K {Fps} fps moving test frames at {Bitrate / 1_000_000} Mbit/s; {string.Join("; ", notes)}");
    }

    /// <summary>Eight different NV12 frames of moving gradient and fine noise, cycled so that the encoder always has something that changed to code.</summary>
    private static byte[][] Frames()
    {
        var frames = new byte[Variants][]; var random = new Random(7);
        for (int f = 0; f < Variants; f++)
        {
            var frame = new byte[Width * Height * 3 / 2];
            for (int y = 0; y < Height; y++)
            {
                int row = y * Width; int shade = y * 255 / Height;
                for (int x = 0; x < Width; x++) frame[row + x] = (byte)((x * 255 / Width + shade + f * 24 + (random.Next() & 15)) & 255);
            }
            for (int i = Width * Height; i < frame.Length; i += 2) { frame[i] = (byte)(96 + f * 8 + (i >> 12) % 64); frame[i + 1] = (byte)(160 - f * 8 - (i >> 13) % 48); }
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

    private static (double Fps, int Frames) Encode(Codec codec, string file, byte[][] frames, IMFDXGIDeviceManager manager, double seconds, Action<double> progress, CancellationToken ct)
    {
        Mf.Check(Mf.MFCreateAttributes(out var attributes, 3), "MFCreateAttributes");
        attributes.SetUINT32(Mf.EnableHardwareTransforms, 1); attributes.SetUnknown(Mf.SinkD3dManager, manager);
        Mf.Check(Mf.MFCreateSinkWriterFromURL(file, 0, attributes, out var writer), "MFCreateSinkWriterFromURL");
        writer.AddStream(Mf.VideoType(codec.Subtype, Width, Height, Fps, Bitrate), out uint stream);
        writer.SetInputMediaType(stream, Mf.VideoType(Mf.Nv12, Width, Height, Fps), null);
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

    private static double Decode(string file, IMFDXGIDeviceManager manager, double seconds, Action<double> progress, CancellationToken ct)
    {
        long frames = 0; var clock = Stopwatch.StartNew();
        do
        {
            Mf.Check(Mf.MFCreateAttributes(out var attributes, 3), "MFCreateAttributes");
            attributes.SetUINT32(Mf.EnableHardwareTransforms, 1); attributes.SetUnknown(Mf.SourceD3dManager, manager);
            Mf.Check(Mf.MFCreateSourceReaderFromURL(file, attributes, out var reader), "MFCreateSourceReaderFromURL");
            Mf.Check(Mf.MFCreateMediaType(out var wanted), "MFCreateMediaType");
            wanted.SetGUID(Mf.MajorType, Mf.Video); wanted.SetGUID(Mf.Subtype, Mf.Nv12);
            reader.SetCurrentMediaType(Mf.FirstVideoStream, 0, wanted);
            long pass = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                reader.ReadSample(Mf.FirstVideoStream, 0, out _, out uint flags, out _, out nint sample);
                if (sample != 0) { pass++; Marshal.Release(sample); }
                if ((flags & Mf.EndOfStream) != 0) break;
            }
            if (pass == 0) throw new InvalidOperationException("the decoder produced no frames");
            frames += pass; progress(Math.Min(1, clock.Elapsed.TotalSeconds / seconds));
        } while (clock.Elapsed.TotalSeconds < seconds);
        return frames / clock.Elapsed.TotalSeconds;
    }
}
