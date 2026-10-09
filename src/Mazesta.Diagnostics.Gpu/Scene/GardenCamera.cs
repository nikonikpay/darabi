using System.Numerics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// Where the visual test's camera is at a moment: a slow walk round the courtyard, the same loop every run (a pure function of time, so the
/// check frames see one picture). It starts at the gate looking up the pool to the fountain and the logo, goes along the left outer walk
/// (the walk lies between the beds at x -9.9 and -8.2; a bench stands on it twice, its back to the outer bed, and is passed in front) to
/// the foot of the stairs, up onto the terrace and in at the hall's open door; round its rooms - along the orsi and the light they
/// lay on the floor, the tea corner seen across the room, the paintings on the back wall and the reading table between them, the
/// right sitting corner, the orsi again from within - out onto the terrace looking back down the garden, down the right walk and home.
/// Every key is six seconds from the next. The garden's day (<see cref="GardenDay"/>) is timed by this walk: morning on the way up
/// the garden, the afternoon sun coming in at the windows while it is in the hall, sunset as it leaves, night on the way back.
/// Positions are in the scene's Direct3D axes (y up, z toward the hall); the paving is at y 0, the terrace at 1.07, the hall's floor at
/// 1.11; the doorway is at z -3.3 to -1.9, 1.6 m wide between its open leaves.
/// </summary>
public static class GardenCamera
{
    public const float Loop = 162f;
    private static readonly (Vector3 Eye, Vector3 Target)[] Keys =
    [
        (new(0.0f, 1.75f, -31.3f), new(0.0f, 1.9f, -19.0f)),
        (new(-6.0f, 1.75f, -31.1f), new(0.0f, 2.0f, -19.0f)),
        (new(-8.67f, 1.75f, -29.0f), new(-2.0f, 2.0f, -19.0f)),   // past the first bench: it stands back against the outer bed, the walk before it
        (new(-8.67f, 1.75f, -24.0f), new(0.0f, 2.1f, -16.0f)),
        (new(-8.67f, 1.75f, -17.0f), new(0.0f, 2.2f, -11.0f)),    // and the second
        (new(-9.05f, 1.75f, -9.4f), new(-1.0f, 2.6f, -4.0f)),     // round the end of the beds, the hall ahead
        (new(-5.0f, 1.9f, -8.6f), new(0.0f, 2.7f, -1.0f)),
        (new(0.0f, 2.6f, -7.8f), new(0.0f, 2.7f, 0.0f)),          // on the stairs, facing the door
        (new(0.0f, 2.75f, -3.7f), new(-1.5f, 2.6f, 3.5f)),        // at the threshold
        (new(0.0f, 2.75f, -1.0f), new(-5.5f, 3.3f, 1.8f)),        // just inside: the left rooms, the chandelier over them, the sun on the floor
        (new(-3.0f, 2.75f, -0.6f), new(-9.2f, 2.3f, 3.2f)),       // along the orsi: the tea corner across the room, by the left wall
        (new(-2.6f, 2.75f, 3.0f), new(-7.5f, 1.3f, -1.5f)),       // from the middle of the room, back at the orsi and the light they lay on the floor
        (new(-2.0f, 2.75f, 3.3f), new(-4.6f, 3.9f, 6.75f)),       // the left painting
        (new(0.0f, 2.75f, 1.9f), new(0.0f, 3.4f, 6.75f)),         // the reading table, a painting either side
        (new(2.6f, 2.75f, 2.9f), new(4.1f, 4.0f, 6.75f)),         // before the right painting
        (new(2.9f, 2.75f, 1.6f), new(9.0f, 2.0f, 0.6f)),          // the right sitting corner, the low sun reaching across it
        (new(0.9f, 2.75f, -0.3f), new(6.5f, 2.2f, -2.9f)),         // and the right orsi from within, red with the last of it
        (new(0.0f, 2.75f, -1.3f), new(2.0f, 2.2f, -10.0f)),       // back to the door, looking out: the sun is going down
        (new(0.0f, 2.75f, -3.7f), new(0.0f, 1.6f, -19.0f)),
        (new(0.0f, 2.7f, -7.4f), new(0.0f, 1.0f, -26.0f)),        // the top of the stairs, the garden below, its lamps lit
        (new(4.6f, 1.9f, -8.4f), new(3.0f, 1.2f, -22.0f)),
        (new(9.05f, 1.75f, -9.6f), new(3.5f, 1.2f, -24.0f)),      // down the right walk by night, past its benches
        (new(8.67f, 1.75f, -15.5f), new(1.0f, 1.6f, -19.0f)),
        (new(8.67f, 1.75f, -20.0f), new(0.0f, 1.9f, -19.0f)),
        (new(8.67f, 1.75f, -24.5f), new(0.0f, 2.2f, -17.0f)),
        (new(8.67f, 1.75f, -29.0f), new(0.0f, 2.0f, -19.0f)),
        (new(6.0f, 1.75f, -31.1f), new(0.0f, 2.0f, -19.0f)),      // day breaks
    ];

    /// <summary>For the render checks: one view to draw every moment from, instead of the walk's (null: the walk).</summary>
    internal static (Vector3 Eye, Vector3 Target)? Fixed { get; set; }

    /// <summary>The moment the camera and the day are held at (null: they follow the time). The tuning scene pins them so every frame is drawn from one place
    /// in one light while the water, the fountain, the wind and the rest go on moving. A plain static, not per thread: the ray-traced scene is placed on every core.</summary>
    internal static float? Pinned { get; set; }

    public static (Vector3 Eye, Vector3 Target) At(float time)
    {
        if (Fixed is { } view) return view;
        time = Pinned ?? time;
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
