using NLayer;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>The hall's music: the owner's own piece (<c>garden-music.mp3</c>, embedded as it is), decoded once into stereo samples at the mix's rate. It is not edited; the mix fades its start and its end.</summary>
internal static class GardenMusic
{
    public static short[] Load(string file = "garden-music.mp3")
    {
        using var stream = typeof(GardenMusic).Assembly.GetManifestResourceStream("Mazesta.Diagnostics.Gpu.Scene." + file) ?? throw new InvalidOperationException(file + " is not embedded");
        return Decode(stream);
    }

    internal static short[] Decode(Stream mp3)
    {
        using var file = new MpegFile(mp3); int rate = file.SampleRate, channels = file.Channels; var all = new List<float>(); var chunk = new float[8192]; int got;
        while ((got = file.ReadSamples(chunk, 0, chunk.Length)) > 0) for (int i = 0; i < got; i++) all.Add(chunk[i]);
        int frames = all.Count / channels; if (frames < 2) throw new InvalidDataException("no sample data");
        float Of(int frame, int side) => all[frame * channels + Math.Min(side, channels - 1)];
        int length = (int)((long)frames * GardenSoundMix.Rate / rate); var result = new short[length * 2];
        for (int i = 0; i < length; i++)
        {
            double p = (double)i * rate / GardenSoundMix.Rate; int j = Math.Min((int)p, frames - 1), k = Math.Min(j + 1, frames - 1); double f = p - j;
            for (int s = 0; s < 2; s++) result[i * 2 + s] = (short)(Math.Clamp(Of(j, s) + (Of(k, s) - Of(j, s)) * f, -1, 1) * 32767);
        }
        return result;
    }
}
