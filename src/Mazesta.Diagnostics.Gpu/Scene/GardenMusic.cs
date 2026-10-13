namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>The hall's music: the owner's own piece (<c>garden-music.wav</c>, 16-bit mono, 32 kHz, embedded), made into stereo samples at the mix's rate.</summary>
internal static class GardenMusic
{
    public static short[] Load()
    {
        using var stream = typeof(GardenMusic).Assembly.GetManifestResourceStream("Mazesta.Diagnostics.Gpu.Scene.garden-music.wav") ?? throw new InvalidOperationException("garden-music.wav is not embedded");
        using var memory = new MemoryStream(); stream.CopyTo(memory); return Parse(memory.ToArray());
    }

    /// <summary>The samples of a 16-bit mono PCM wave file, stretched (linearly) to <see cref="GardenSoundMix.Rate"/> and doubled to left and right.</summary>
    internal static short[] Parse(byte[] file)
    {
        int rate = 0, at = 12;
        while (at + 8 <= file.Length)
        {
            uint id = BitConverter.ToUInt32(file, at); int size = BitConverter.ToInt32(file, at + 4), body = at + 8;
            if (id == 0x20746D66) { if (BitConverter.ToUInt16(file, body) != 1 || BitConverter.ToUInt16(file, body + 2) != 1 || BitConverter.ToUInt16(file, body + 14) != 16) throw new InvalidDataException("not 16-bit mono PCM"); rate = BitConverter.ToInt32(file, body + 4); }   // "fmt "
            else if (id == 0x61746164 && rate > 0)   // "data"
            {
                var src = new short[size / 2]; Buffer.BlockCopy(file, body, src, 0, src.Length * 2);
                int frames = (int)((long)src.Length * GardenSoundMix.Rate / rate); var pcm = new short[frames * 2];
                for (int i = 0; i < frames; i++)
                {
                    double p = (double)i * rate / GardenSoundMix.Rate; int j = (int)p; double f = p - j; short a = src[Math.Min(j, src.Length - 1)], b = src[Math.Min(j + 1, src.Length - 1)];
                    pcm[i * 2] = pcm[i * 2 + 1] = (short)(a + (b - a) * f);
                }
                return pcm;
            }
            at = body + size + (size & 1);
        }
        throw new InvalidDataException("no sample data");
    }
}
