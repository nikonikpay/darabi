using System.Numerics; using Xunit;
using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

[Trait("Category", "Hardware")]
public class GardenSoundDeviceTests
{
    [Fact] public void The_sound_device_plays_for_a_moment_and_closes_without_a_fault()
    {
        using var sound = new GardenSound(new Vector3(0, 1, -19)); sound.Listen(new(0, 2.7f, 1), 10); Thread.Sleep(1500); sound.Listen(new(0, 1.7f, -18), 12); Thread.Sleep(500);
    }

    /// <summary>With the owner's recording in Data (copy it beside the test dll), the hall's music is heard faintly from the stairs, louder at the door and loudest inside, and leaving fades it out (levels are RMS of the mix).</summary>
    [Fact] public void The_recording_swells_towards_the_door_and_fades_away_outside()
    {
        if (GardenMusic.Load() is null) return;
        double At(float z) { var mix = new GardenSoundMix(); mix.Set(SoundLevels.At(new(0, 1.7f, z), new Vector3(0, 1, -19), 10)); var pcm = new short[GardenSoundMix.Rate * 2]; double sum = 0; mix.Render(pcm, GardenSoundMix.Rate); mix.Render(pcm, GardenSoundMix.Rate); foreach (short v in pcm) sum += (double)v * v; return Math.Sqrt(sum / pcm.Length) / short.MaxValue; }
        double inside = At(1), door = At(-4), stairs = At(-9), far = At(-30);
        Console.WriteLine($"rms inside {inside:F4}  door {door:F4}  stairs {stairs:F4}  far {far:F4}");
        Assert.True(inside > door && door > stairs && stairs > far);
    }
}
