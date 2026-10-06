using System.Numerics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The garden's day, which both visual tests go through once in every walk of the garden (<see cref="GardenCamera.Loop"/>): the sun
/// is just up as the walk sets out from the gate, stands highest as it reaches the hall, and sinks while it is inside - so the light
/// of the orsi's stained panes creeps across the floor and reaches deeper into the rooms - and sets as it comes out onto the terrace
/// again. At dusk the lamps are lit one after another; the walk back down the garden is by night, under the moon; day breaks as it ends.
/// It is a winter's day, the kind the hall was built for: the sun low from morning to evening, shining in at its windows.
/// A pure function of the walk's time - the same moment is the same light - which both renderers and the light volume read.
/// <para><see cref="Key"/>: toward the key light (the sun, or the moon); <see cref="KeyColor"/>: its irradiance; <see cref="Night"/>:
/// 0 by day, 1 at night, between in the twilights; <see cref="Lamps"/>: how far the lamps are lit; <see cref="Noon"/>: 0 with the sun
/// low and golden, 1 with it high; <see cref="Sun"/>: where the sun is on its arc, 0 at sunrise, 1 at sunset.</para>
/// </summary>
public readonly record struct GardenDay(Vector3 Key, Vector3 KeyColor, bool Moon, Vector3 Zenith, Vector3 Horizon, Vector3 Ground, float Night, float Lamps, float Noon, float Exposure, float Sun)
{
    /// <summary>When, in the walk's seconds, the sun comes up (four seconds before the walk starts over) and goes down (as the walk
    /// turns to leave the hall), and how long each twilight lasts.</summary>
    public const float Sunrise = -4f, Sunset = 104f, Twilight = 14f;
    /// <summary>The sun's arc: how high it stands at noon, and how far round from the garden's axis it rises and sets. The scene's
    /// own sun (16 degrees up, 40 round) is a point of it, late in the afternoon.</summary>
    public const float Highest = 40f * MathF.PI / 180, Sweep = 54f * MathF.PI / 180;
    /// <summary>How much of the sky's own brightness lights the garden by day (Garden.hlsli has the same number): the picture's
    /// exposure is set for sunlit stone, and the sky's light is a fraction of the sun's. At night the sky is what there is.</summary>
    public const float SkyShare = 0.34f;
    /// <summary>The sky's light on the garden, as one colour: what the light volume's part for the sky is multiplied by.</summary>
    public Vector3 SkyLight => Vector3.Lerp(Horizon, Zenith, 0.55f) * (SkyShare + (1 - SkyShare) * Night);

    /// <summary>Where the sun is on its arc at <paramref name="time"/>: 0 at sunrise, 1 at sunset; past 1 it has set that long ago,
    /// under 0 it has that long to rise (the night's first half counts on from the sunset, its second down to the sunrise).</summary>
    public static float SunAt(float time)
    {
        float loop = GardenCamera.Loop, length = Sunset - Sunrise, d = ((time - Sunrise) % loop + loop) % loop;
        return (d > (length + loop) / 2 ? d - loop : d) / length;
    }

    /// <summary>Toward the sun at point <paramref name="s"/> of its arc. It crosses the sky on the garden's own side of the hall
    /// (-z: the hall's windows face it), from one side of the axis to the other; <paramref name="side"/> (1 or -1) is the x it sets on.</summary>
    public static Vector3 SunDirection(float s, float side)
    {
        float az = Sweep * (2 * s - 1) * side, el = Highest * MathF.Sin(MathF.PI * s);
        return new(MathF.Sin(az) * MathF.Cos(el), MathF.Sin(el), -MathF.Cos(az) * MathF.Cos(el));
    }

    /// <summary>Toward the moon at <paramref name="time"/>: it swings slowly across the sky about where the scene has it, there and back
    /// in one walk of the garden, so its shadows creep over the paving while it is up.</summary>
    public static Vector3 MoonAt(Vector3 placed, float time)
    {
        float a = 0.42f * MathF.Sin(time / GardenCamera.Loop * 2 * MathF.PI), c = MathF.Cos(a), s = MathF.Sin(a);
        return Vector3.Normalize(new(c * placed.X + s * placed.Z, placed.Y * (1 + 0.25f * MathF.Cos(time / GardenCamera.Loop * 2 * MathF.PI)), -s * placed.X + c * placed.Z));
    }

    /// <summary>The light at <paramref name="time"/>, for a scene whose own sun shines from <paramref name="sun"/> with
    /// <paramref name="sunColor"/> (what it has a hand above the horizon) and whose moon stands at <paramref name="moon"/>.</summary>
    public static GardenDay At(float time, Vector3 sun, Vector3 sunColor, Vector3 moon, Vector3 moonColor)
    {
        float s = SunAt(time), length = Sunset - Sunrise;
        float night = s > 1 ? Smooth((s - 1) * length / Twilight) : s < 0 ? Smooth(-s * length / Twilight) : 0;
        var dir = SunDirection(s, sun.X < 0 ? -1 : 1); float degrees = MathF.Asin(dir.Y) * 180 / MathF.PI;
        // the sun's light: red and weak on the horizon, the scene's own gold a hand above it, whiter and stronger toward noon
        var low = new Vector3(1.0f, 0.5f, 0.24f) * 0.7f; var high = new Vector3(0.96f, 1.04f, 1.16f) * 1.12f;
        var light = sunColor * Vector3.Lerp(Vector3.Lerp(low, Vector3.One, Smooth(degrees / 14)), high, Smooth((degrees - 16) / 20)) * Smooth((degrees + 1) / 5);
        float noon = Smooth((degrees - 10) / 22);
        // three skies as simple gradients: the low golden sun's, the high sun's, the night's
        // (by day the exposure is set for sunlit stone, and the sky's own light is a fraction of the sun's: what it does not reach stays in real shade)
        var zenith = Vector3.Lerp(Vector3.Lerp(new(0.13f, 0.22f, 0.46f), new(0.10f, 0.25f, 0.60f), noon), new(0.012f, 0.022f, 0.070f), night);
        var horizon = Vector3.Lerp(Vector3.Lerp(new(0.62f, 0.46f, 0.32f), new(0.50f, 0.60f, 0.74f), noon), new(0.10f, 0.085f, 0.17f), night);
        var ground = Vector3.Lerp(Vector3.Lerp(new(0.20f, 0.16f, 0.12f), new(0.21f, 0.19f, 0.16f), noon), new(0.020f, 0.018f, 0.025f), night);
        // the key light is the sun until it is down, then nothing for a moment, then the moon
        bool isMoon = night >= 0.5f;
        var key = isMoon ? MoonAt(moon, time) : dir.Y > 0.03f ? dir : Vector3.Normalize(dir with { Y = 0.03f });
        return new(key, isMoon ? moonColor * Smooth(night * 2 - 1) : light, isMoon, zenith, horizon, ground, night, Smooth((night - 0.15f) / 0.6f), noon, 1.9f + (1.45f - 1.9f) * night, Math.Clamp(s, 0, 1));
    }

    private static float Smooth(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
}
