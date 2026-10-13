using System.Numerics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>Where the listener stands, as the mix needs it: how much of the hall's music, of the fountain's water and of the day's birds reaches the ear (each 0 to 1), and whether the ear is indoors.</summary>
internal sealed record SoundLevels(float Music, float Water, float Day, float Indoor)
{
    private const float HallFront = -3.15f, HallHalfWidth = 9.4f, MusicReach = 11f, WaterReach = 6f;

    /// <summary>The levels at <paramref name="eye"/> at the walk's <paramref name="time"/>: the hall (behind its front wall, z over -3.15) is where the music is and the garden's sounds are shut out; in the garden the music is
    /// faint from the foot of the stairs, rises to the door and is gone 11 m from the hall (the pool's edge): the fountain's water is heard only within 6 m of the pool, and the birds sing while the sun is up (crickets take over after).</summary>
    public static SoundLevels At(Vector3 eye, Vector3? fountain, float time)
    {
        bool inside = MathF.Abs(eye.X) < HallHalfWidth && eye.Z > HallFront;
        float outside = inside ? 0 : MathF.Max(0, HallFront - eye.Z) + MathF.Max(0, MathF.Abs(eye.X) - HallHalfWidth);   // metres from the hall
        float music = inside ? 1 : 0.45f * MathF.Pow(MathF.Max(0, 1 - outside / MusicReach), 2f);
        float water = 0;
        if (fountain is { } f) { float d = MathF.Sqrt((eye.X - f.X) * (eye.X - f.X) + (eye.Z - f.Z) * (eye.Z - f.Z)); water = MathF.Pow(MathF.Max(0, 1 - d / WaterReach), 2f) * (inside ? 0.1f : 1); }
        float sun = GardenDay.SunAt(time), day = Math.Clamp(MathF.Min(sun, 1 - sun) * 8, 0, 1);
        return new(music, water, day, inside ? 1 : 0);
    }
}

/// <summary>
/// The garden's sound, made as it is played (the hall's music is the owner's own piece): the owner's recording of birds and leaves by day, crickets and the quiet by night, the fountain's water that grows as the listener nears it, and, in the hall only, the owner's piece (faded in at its start and out at its end). Stereo, 48 kHz.
/// The levels are set from another thread (<see cref="Set"/>) and glide, so crossing the door is a fade, not a click.
/// </summary>
internal sealed class GardenSoundMix(int seed = 7)
{
    public const int Rate = 48000;

    private volatile SoundLevels _target = new(0, 0, 1, 0);
    private float _music, _water, _day = 1, _indoor;   // the levels as they glide (fields, not a record: a new one per sample is 48 000 objects a second for the collector)
    private uint _rng = (uint)seed * 2654435761u | 1;
    private float _lp, _hp;
    private long _sample;

    public void Set(SoundLevels levels) => _target = levels;

    /// <summary>The owner's own piece for the hall (<see cref="GardenMusic"/>), as stereo samples at <see cref="Rate"/>.</summary>
    private readonly short[] _clip = GardenMusic.Load(); private int _clipFrame; private const float FadeSeconds = 6;

    private float White() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return _rng / 2147483648f - 1; }
    private float Unit() => (White() + 1) / 2;

    // ——— crickets ———
    private float _cricketPhase, _cricketBeat;
    private float Crickets()
    {
        _cricketBeat = (_cricketBeat + 1f / Rate) % 990; float t = _cricketBeat % 6, gate = MathF.Sin(MathF.Tau * 28 * t) > 0.2f ? 1 : 0;
        // a courtyard, not a forest: a short burst of three chirps every six seconds, and every third burst is left out
        float chirp = (int)(_cricketBeat / 6) % 3 != 0 && t < 0.7f && MathF.Sin(MathF.Tau * 4.3f * t) > 0.1f ? 1 : 0;
        _cricketPhase = (_cricketPhase + 4600f / Rate) % 1; return MathF.Sin(MathF.Tau * _cricketPhase) * gate * chirp * 0.02f;
    }

    // ——— the garden by day: the owner's recording of birds and leaves, looped (its end is crossfaded into its start); it is decoded on first use ———
    private short[]? _ambience; private int _ambFrame; private const int AmbCross = Rate * 3;
    private (float L, float R) Ambience()
    {
        _ambience ??= GardenMusic.Load("garden-ambience.mp3"); int n = _ambience.Length / 2;
        float l = _ambience[_ambFrame * 2] / 32768f, r = _ambience[_ambFrame * 2 + 1] / 32768f;
        int tail = _ambFrame - (n - AmbCross);
        if (tail >= 0) { float k = tail / (float)AmbCross, a = MathF.Cos(k * MathF.PI / 2), b = MathF.Sin(k * MathF.PI / 2); l = l * a + _ambience[tail * 2] / 32768f * b; r = r * a + _ambience[tail * 2 + 1] / 32768f * b; }
        if (++_ambFrame >= n) _ambFrame = AmbCross;   // the crossfade has played the first AmbCross frames of the start already
        return (l, r);
    }

    // ——— the water: the fountain's steady pour and the odd drop falling in the basin ———
    private float _dropHz, _dropAge = 9;
    private float Water()
    {
        if (_dropAge > 1 && Unit() < 0.00015f) { _dropHz = 1300 + Unit() * 2600; _dropAge = 0; }
        float drop = _dropAge < 1 ? MathF.Sin(MathF.Tau * _dropHz * _dropAge * 0.05f) * MathF.Exp(-_dropAge * 6) * 0.35f : 0; _dropAge += 1f / (Rate * 0.05f);
        float w = White(); _lp += (w - _lp) * 0.5f; _hp += (_lp - _hp) * 0.08f;   // a band of noise: the pour. It is only ever heard near the pool.
        return (_lp - _hp) * 0.12f + drop;
    }

    // ——— the whole ———

    /// <summary>Fills <paramref name="stereo"/> (left, right, left...; <paramref name="frames"/> of them) with the next of the soundscape.</summary>
    public void Render(Span<short> stereo, int frames)
    {
        var goal = _target;
        for (int i = 0; i < frames; i++, _sample++)
        {
            const float Glide = 1f / (Rate * 0.4f);
            _music += (goal.Music - _music) * Glide * 4; _water += (goal.Water - _water) * Glide * 4; _day += (goal.Day - _day) * Glide * 4; _indoor += (goal.Indoor - _indoor) * Glide * 4;
            float outdoors = 1 - _indoor;   // in the hall there is only the music
            float by = _day * outdoors, ambL = 0, ambR = 0;   // the recording of birds and leaves is only the day's, outdoors
            if (by > 0.002f) { var (l, r) = Ambience(); ambL = l * by * 1.5f; ambR = r * by * 1.5f; }
            float cricket = Crickets() * (1 - _day) * outdoors;
            float water = Water() * _water * 0.5f;
            float left = ambL + water + cricket, right = ambR + water + cricket;
            float music = 0, musicR = 0;
            if (_music > 0.002f)
            {
                int frames_ = _clip.Length / 2, fade = FadeSeconds * Rate < frames_ / 2 ? (int)(FadeSeconds * Rate) : frames_ / 2;
                float gain = MathF.Min(1, MathF.Min(_clipFrame, frames_ - 1 - _clipFrame) / (float)fade); gain *= gain;   // faint at the start, faint again at the end, where the night takes over
                music = _clip[_clipFrame * 2] / 32768f * gain * _music; musicR = _clip[_clipFrame * 2 + 1] / 32768f * gain * _music;
                _clipFrame = (_clipFrame + 1) % frames_;
            }
            else _clipFrame = 0;   // each visit to the hall starts the piece from its beginning
            stereo[i * 2] = Out(left + music); stereo[i * 2 + 1] = Out(right + musicR);
        }
    }

    private static short Out(float x) => (short)(MathF.Tanh(x * 1.4f) * 0.7f * short.MaxValue);   // a soft ceiling: nothing clips whatever the layers add up to
}
