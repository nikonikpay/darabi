using NLayer;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>The hall's music: the owner's own piece (<c>garden-music.mp3</c>, embedded as it is), decoded once into stereo samples at the mix's rate, its silent ends cut off and its end faded into its beginning so the loop has no gap.</summary>
internal static class GardenMusic
{
    private const float Quiet = 0.004f, Overlap = 1.5f;   // below this (of full scale) is silence; seconds the end and the beginning are blended over

    public static short[] Load()
    {
        using var stream = typeof(GardenMusic).Assembly.GetManifestResourceStream("Mazesta.Diagnostics.Gpu.Scene.garden-music.mp3") ?? throw new InvalidOperationException("garden-music.mp3 is not embedded");
        return Decode(stream);
    }

    internal static short[] Decode(Stream mp3)
    {
        using var file = new MpegFile(mp3); int rate = file.SampleRate, channels = file.Channels; var all = new List<float>(); var chunk = new float[8192]; int got;
        while ((got = file.ReadSamples(chunk, 0, chunk.Length)) > 0) for (int i = 0; i < got; i++) all.Add(chunk[i]);
        int frames = all.Count / channels; if (frames < 2) throw new InvalidDataException("no sample data");
        float Of(int frame, int side) => all[frame * channels + Math.Min(side, channels - 1)];
        int first = 0, last = frames - 1;
        while (first < last && Math.Abs(Of(first, 0)) < Quiet && Math.Abs(Of(first, 1)) < Quiet) first++;
        while (last > first && Math.Abs(Of(last, 0)) < Quiet && Math.Abs(Of(last, 1)) < Quiet) last--;
        int length = (int)((long)(last - first + 1) * GardenSoundMix.Rate / rate), blend = Math.Min((int)(Overlap * GardenSoundMix.Rate), length / 4); var pcm = new float[length * 2];
        for (int i = 0; i < length; i++)
        {
            double p = first + (double)i * rate / GardenSoundMix.Rate; int j = (int)p; double f = p - j; int k = Math.Min(j + 1, frames - 1); j = Math.Min(j, frames - 1);
            for (int s = 0; s < 2; s++) pcm[i * 2 + s] = (float)(Of(j, s) + (Of(k, s) - Of(j, s)) * f);
        }
        // the loop is the piece without its last `blend` frames, its head being the old head faded into those frames
        int loop = length - blend; var result = new short[loop * 2];
        for (int i = 0; i < loop; i++)
            for (int s = 0; s < 2; s++)
            {
                float v = pcm[i * 2 + s];
                if (i < blend) { float a = (float)i / blend; v = v * MathF.Sin(a * MathF.PI / 2) + pcm[(loop + i) * 2 + s] * MathF.Cos(a * MathF.PI / 2); }
                result[i * 2 + s] = (short)(Math.Clamp(v, -1f, 1f) * 32767);
            }
        return result;
    }
}
