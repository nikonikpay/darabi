namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The music the hall plays, if the owner has put one in: <c>garden-music.wav</c> in the app's Data folder (16-bit stereo, 48 kHz, cut to loop). It is the owner's own file, not part of the app or its source, so nothing is
/// shipped that is not ours to ship; with none there the hall plays the string <see cref="GardenSoundMix"/> makes itself.
/// </summary>
internal static class GardenMusic
{
    public const string FileName = "garden-music.wav";

    public static short[]? Load()
    {
        foreach (string dir in new[] { Path.Combine(AppContext.BaseDirectory, "Data"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mazesta Test", "Data") })
        {
            try { if (File.Exists(Path.Combine(dir, FileName)) && Parse(File.ReadAllBytes(Path.Combine(dir, FileName))) is { } pcm) return pcm; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    /// <summary>The samples of a 16-bit stereo 48 kHz PCM wave file; null for any other file (it is then not played, never played wrongly).</summary>
    internal static short[]? Parse(byte[] file)
    {
        if (file.Length < 44 || BitConverter.ToUInt32(file, 0) != 0x46464952 || BitConverter.ToUInt32(file, 8) != 0x45564157) return null;   // "RIFF" ... "WAVE"
        bool ok = false;
        for (int at = 12; at + 8 <= file.Length;)
        {
            uint id = BitConverter.ToUInt32(file, at); int size = BitConverter.ToInt32(file, at + 4), body = at + 8;
            if (size < 0 || body + size > file.Length) size = Math.Max(0, file.Length - body);
            if (id == 0x20746D66) ok = size >= 16 && BitConverter.ToUInt16(file, body) == 1 && BitConverter.ToUInt16(file, body + 2) == 2 && BitConverter.ToUInt32(file, body + 4) == GardenSoundMix.Rate && BitConverter.ToUInt16(file, body + 14) == 16;   // "fmt "
            else if (id == 0x61746164)   // "data"
            {
                if (!ok || size < 4) return null;
                var pcm = new short[size / 4 * 2]; Buffer.BlockCopy(file, body, pcm, 0, pcm.Length * 2); return pcm;
            }
            at = body + size + (size & 1);
        }
        return null;
    }
}
