using System.Reflection;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>A video stream's form as the decode and encode tests name it: codec (h264, hevc, vp9, av1), picture height, bit depth and chroma
/// subsampling (420 or 422).</summary>
internal sealed record VideoFormat(string Codec, int Height, int Bits, int Chroma)
{
    public int Width => Height switch { 1080 => 1920, 2160 => 3840, 4320 => 7680, _ => Height * 16 / 9 };
    public string Id => $"{Codec}_{Height}p_{Bits}b_{Chroma}";
    public string CodecName => Codec switch { "h264" => "H.264", "hevc" => "HEVC", "vp9" => "VP9", _ => "AV1" };
    public string Label => $"{CodecName} {(Height switch { 2160 => "4K", 4320 => "8K", _ => $"{Height}p" })} {Bits}-bit 4:{(Chroma == 422 ? "2:2" : "2:0")}";
    public Guid Subtype => Codec switch { "h264" => Mf.H264, "hevc" => Mf.Hevc, "vp9" => Mf.Vp9, _ => Mf.Av1 };

    /// <summary>The 4K 8-bit 4:2:0 streams keep the keys the benchmark had before it measured more forms, so their comparisons go on.</summary>
    public string MetricKey(bool encode)
        => (Height, Bits, Chroma) == (2160, 8, 420) && Codec != "vp9" ? $"Bench_Gpu_Video_{(encode ? "Enc" : "Dec")}{(Codec switch { "h264" => "H264", "hevc" => "Hevc", _ => "Av1" })}"
            : $"Bench_Gpu_Video_{(encode ? "Enc" : "Dec")}_{Id}";

    private static readonly string[] Order = ["h264", "hevc", "vp9", "av1"];   // (before Clips: static fields start in the order written)

    /// <summary>The clips the decode test plays, embedded in this assembly (made by tools/make-video-clips.sh): a card decodes what it was built
    /// for whether or not it can encode it, so the streams cannot come from its own encoder.</summary>
    public static IReadOnlyList<(VideoFormat Format, string Resource)> Clips { get; } = [.. Assembly.GetExecutingAssembly().GetManifestResourceNames()
        .Where(n => n.StartsWith(ClipPrefix, StringComparison.Ordinal) && n.EndsWith(".mp4", StringComparison.Ordinal))
        .Select(n => (Format: Parse(n[ClipPrefix.Length..^4]), Resource: n)).Where(c => c.Format is not null).Select(c => (c.Format!, c.Resource))
        .OrderBy(c => Array.IndexOf(Order, c.Item1.Codec)).ThenBy(c => c.Item1.Height).ThenBy(c => c.Item1.Chroma).ThenBy(c => c.Item1.Bits)];
    public const int ClipFrames = 30;
    private const string ClipPrefix = "Mazesta.Diagnostics.Gpu.Video.";

    /// <summary>"av1-2160p-10b-420" → the form; null for a name of another shape.</summary>
    internal static VideoFormat? Parse(string name)
    {
        var p = name.Split('-');
        return p.Length == 4 && Order.Contains(p[0]) && p[1].EndsWith('p') && int.TryParse(p[1][..^1], out int h) && p[2].EndsWith('b') && int.TryParse(p[2][..^1], out int b)
            && int.TryParse(p[3], out int c) && c is 420 or 422 ? new(p[0], h, b, c) : null;
    }

    /// <summary>What the encode test makes: every codec cards encode, at Full HD and 4K in 8 bits, and 4K in 10 bits where the codec has a 10-bit
    /// profile cards encode (HEVC Main 10, AV1 Main). 4:2:2 encoding is left out: only the newest cards have it, through no Windows path yet.</summary>
    public static IReadOnlyList<VideoFormat> Encodes { get; } =
        [new("h264", 1080, 8, 420), new("h264", 2160, 8, 420), new("hevc", 1080, 8, 420), new("hevc", 2160, 8, 420), new("hevc", 2160, 10, 420),
         new("av1", 1080, 8, 420), new("av1", 2160, 8, 420), new("av1", 2160, 10, 420)];
}
