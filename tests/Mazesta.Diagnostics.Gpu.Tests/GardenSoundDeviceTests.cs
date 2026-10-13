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
}
