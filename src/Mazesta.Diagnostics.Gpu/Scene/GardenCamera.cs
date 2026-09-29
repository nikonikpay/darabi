using System.Numerics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// Where the visual test's camera is at a moment: a slow walk round the courtyard, the same loop every run (a pure function of time, so the
/// check frames see one picture). It starts at the gate looking up the pool to the logo, goes along the left outer walk (x = -9.05, between
/// the inner and outer beds) to the foot of the stairs, up onto the terrace looking back down the garden, down the right walk and home.
/// Positions are in the scene's Direct3D axes (y up, z toward the hall); the paving is at y 0.05, the terrace at 1.07.
/// </summary>
public static class GardenCamera
{
    public const float Loop = 64f;
    private static readonly (Vector3 Eye, Vector3 Target)[] Keys =
    [
        (new(0.0f, 1.75f, -31.3f), new(0.0f, 1.9f, -19.0f)),
        (new(-9.05f, 1.75f, -31.0f), new(0.0f, 2.0f, -19.0f)),
        (new(-9.05f, 1.75f, -19.5f), new(0.0f, 2.2f, -12.0f)),
        (new(-9.05f, 1.75f, -9.3f), new(0.0f, 1.5f, -19.0f)),
        (new(0.0f, 2.7f, -7.2f), new(0.0f, 1.0f, -26.0f)),
        (new(9.05f, 1.75f, -9.3f), new(0.0f, 1.5f, -19.0f)),
        (new(9.05f, 1.75f, -22.4f), new(0.0f, 2.0f, -15.0f)),
        (new(6.0f, 1.75f, -31.0f), new(0.0f, 2.0f, -19.0f)),
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
