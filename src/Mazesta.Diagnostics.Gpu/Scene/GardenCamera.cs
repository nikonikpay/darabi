using System.Numerics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// Where the visual test's camera is at a moment: a slow walk round the garden, the same loop every run (a pure function of time, so the
/// check frames see one picture). It starts at the Blender scene's own camera by the fountain, goes up the left walk to the logo over
/// the pool, round behind it by the cascade, to the top of the hall's stairs looking down the garden, back down the right walk and home.
/// Positions are in the scene's Direct3D axes (y up, z toward the hall); the walks at x = ±5.9 run between the lantern posts and the beds.
/// </summary>
public static class GardenCamera
{
    public const float Loop = 64f;
    private static readonly (Vector3 Eye, Vector3 Target)[] Keys =
    [
        (new(-2.6f, 2.1f, -21.8f), new(0.8f, 2.6f, 4.0f)),
        (new(-5.9f, 1.9f, -13.5f), new(0.0f, 3.0f, -3.0f)),
        (new(-5.9f, 2.1f, -2.5f), new(0.0f, 3.1f, -3.0f)),
        (new(-3.2f, 3.2f, 5.2f), new(0.0f, 2.8f, -4.0f)),
        (new(0.0f, 3.4f, 8.6f), new(0.0f, 2.4f, -12.0f)),
        (new(5.9f, 2.1f, 1.0f), new(0.0f, 3.1f, -3.0f)),
        (new(5.9f, 1.9f, -12.0f), new(0.0f, 2.6f, 10.0f)),
        (new(2.4f, 2.2f, -20.6f), new(-0.5f, 2.6f, 2.0f)),
    ];

    public static (Vector3 Eye, Vector3 Target) At(float time)
    {
        float t = (time % Loop + Loop) % Loop / Loop * Keys.Length;
        int i = (int)t; float f = t - i;
        f = f * f * (3 - 2 * f) * 0.35f + f * 0.65f;   // a touch of ease at each key, never a stop
        (Vector3, Vector3) K(int k) => Keys[(k % Keys.Length + Keys.Length) % Keys.Length];
        var (e0, t0) = K(i - 1); var (e1, t1) = K(i); var (e2, t2) = K(i + 1); var (e3, t3) = K(i + 2);
        return (CatmullRom(e0, e1, e2, e3, f), CatmullRom(t0, t1, t2, t3, f));
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
    }

    /// <summary>The keys, for the tests: every one stands inside the courtyard.</summary>
    internal static IReadOnlyList<(Vector3 Eye, Vector3 Target)> KeyFrames => Keys;
}
