using System.Numerics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>Where the listener stands, as the mix needs it: how much of the hall's music, of the fountain's water and of the day's birds reaches the ear (each 0 to 1), and whether the ear is indoors.</summary>
internal sealed record SoundLevels(float Music, float Water, float Day, float Indoor)
{
    private const float HallFront = -3.15f, HallHalfWidth = 9.4f, MusicReach = 12f, WaterReach = 6f;

    /// <summary>The levels at <paramref name="eye"/> at the walk's <paramref name="time"/>: the hall (behind its front wall, z over -3.15) is where the music is and the garden's sounds are shut out; in the garden the music
    /// is faint from the foot of the stairs and swells to the door (12 m out), the fountain's trickle is heard only within 6 m of the pool, and the birds sing while the sun is up (crickets take over after).</summary>
    public static SoundLevels At(Vector3 eye, Vector3? fountain, float time)
    {
        bool inside = MathF.Abs(eye.X) < HallHalfWidth && eye.Z > HallFront;
        float outside = inside ? 0 : MathF.Max(0, HallFront - eye.Z) + MathF.Max(0, MathF.Abs(eye.X) - HallHalfWidth);   // metres from the hall
        float music = inside ? 1 : 0.9f * MathF.Pow(MathF.Max(0, 1 - outside / MusicReach), 2.2f);
        float water = 0;
        if (fountain is { } f) { float d = MathF.Sqrt((eye.X - f.X) * (eye.X - f.X) + (eye.Z - f.Z) * (eye.Z - f.Z)); water = MathF.Pow(MathF.Max(0, 1 - d / WaterReach), 2f) * (inside ? 0.1f : 1); }
        float sun = GardenDay.SunAt(time), day = Math.Clamp(MathF.Min(sun, 1 - sun) * 8, 0, 1);
        return new(music, water, day, inside ? 1 : 0);
    }
}

/// <summary>
/// The garden's sound, made as it is played (no recording is shipped, so none is anyone's to license): a breeze, birds by day and crickets by night, the fountain's water that grows as the listener nears it, and, in the hall, a plucked
/// string instrument playing slowly in the Shur mode (the scale of Iranian classical music, with its quarter-tone second) over a drone. The string is a Karplus-Strong model, which is what a plucked string does. Stereo, 48 kHz.
/// The levels are set from another thread (<see cref="Set"/>) and glide, so crossing the door is a fade, not a click.
/// </summary>
internal sealed class GardenSoundMix(int seed = 7)
{
    public const int Rate = 48000;
    private const float Tonic = 293.66f;                                   // D4
    private static readonly float[] Shur = [0, 150, 300, 500, 700, 800, 1000];   // cents above the tonic: D, E half-flat, F, G, A, B flat, C

    private volatile SoundLevels _target = new(0, 0, 1, 0);
    private SoundLevels _now = new(0, 0, 1, 0);
    private uint _rng = (uint)seed * 2654435761u | 1;
    private float _brown, _hp, _lp, _gust;
    private long _sample;

    public void Set(SoundLevels levels) => _target = levels;


    private float White() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return _rng / 2147483648f - 1; }
    private float Unit() => (White() + 1) / 2;

    // ——— the plucked string ———
    private sealed class Pluck { public float[] Line = []; public int At, Length; public float Gain; public bool Live; }
    private readonly Pluck[] _strings = [new(), new(), new(), new(), new(), new(), new(), new()]; private int _nextString;
    private void Strike(float hz, float gain) { Pick(hz, gain * 0.6f); Pick(hz * 1.0017f, gain * 0.6f); }   // a santur's note is two strings struck by one mallet, a hair apart
    private void Pick(float hz, float gain)
    {
        var s = _strings[_nextString++ % _strings.Length]; int n = Math.Max(8, (int)(Rate / hz));
        if (s.Line.Length < n) s.Line = new float[n];
        float prev = 0; for (int i = 0; i < n; i++) { prev = prev * 0.3f + White() * 0.7f; s.Line[i] = prev; }   // a soft burst of noise: the pick
        s.At = 0; s.Length = n; s.Gain = gain; s.Live = true;
    }
    private float Strings()
    {
        float sum = 0;
        foreach (var s in _strings)
        {
            if (!s.Live) continue;
            int next = (s.At + 1) % s.Length; float y = s.Line[s.At];
            s.Line[s.At] = (y + s.Line[next]) * 0.4994f;   // the string loses a little at every round: higher partials fastest
            s.At = next; sum += y * s.Gain; s.Gain *= 0.99998f; if (s.Gain < 0.0008f) s.Live = false;
        }
        return sum;
    }

    // ——— the tombak: a pattern of deep 'dom' and bright 'tak' and 'ka' strokes, a sixteenth every 0.12 s ———
    private const string Zarb = "D.t.k.t.D.k.t.k.";   // one bar of sixteen steps; D = dom, t = tak, k = ka
    private int _zarbStep; private long _zarbNext; private float _domAge = 9, _takAge = 9, _takGain, _takPhase, _domPhase;
    private float Tombak()
    {
        if (_sample >= _zarbNext)
        {
            char c = Zarb[_zarbStep++ % Zarb.Length]; _zarbNext = _sample + Rate * 12 / 100;
            if (c == 'D') { _domAge = 0; _domPhase = 0; } else if (c is 't' or 'k') { _takAge = 0; _takGain = c == 't' ? 1 : 0.5f; }
        }
        float v = 0;
        if (_domAge < 0.5f) { _domPhase += (150 - 70 * MathF.Min(1, _domAge * 8)) / Rate; v += MathF.Sin(MathF.Tau * _domPhase) * MathF.Exp(-_domAge * 9) * 0.5f; _domAge += 1f / Rate; }
        if (_takAge < 0.12f) { _takPhase = (_takPhase + 1900f / Rate) % 1; v += (White() * 0.5f + MathF.Sin(MathF.Tau * _takPhase) * 0.5f) * MathF.Exp(-_takAge * 45) * 0.22f * _takGain; _takAge += 1f / Rate; }
        return v;
    }

    // ——— the player: phrases of an eighth-note pulse that walk the mode and come home to the tonic ———
    private int _degree = 4, _notesLeft = 0, _rizLeft; private long _nextNote, _nextRiz;
    private float Hz(int degree) { int octave = (int)MathF.Floor(degree / (float)Shur.Length), step = ((degree % Shur.Length) + Shur.Length) % Shur.Length; return Tonic * MathF.Pow(2, (Shur[step] / 1200f) + octave); }
    private void Play()
    {
        const int Pulse = Rate * 36 / 100;   // 0.36 s
        if (_rizLeft > 0 && _sample >= _nextRiz) { Strike(Hz(_degree), 0.30f); _rizLeft--; _nextRiz = _sample + Rate * 9 / 100; }
        if (_sample < _nextNote) return;
        if (_notesLeft <= 0) { _notesLeft = 7 + (int)(Unit() * 6); _nextNote = _sample + Rate * (1 + (long)(Unit() * 2)); _degree = 4; return; }   // a breath between phrases
        _notesLeft--;
        if (_notesLeft == 0) { _degree = 0; Strike(Hz(0), 0.45f); _rizLeft = 4; _nextRiz = _sample + Rate * 9 / 100; _nextNote = _sample + Pulse * 4; return; }   // the phrase ends on the tonic, trembled
        float r = Unit(); _degree = Math.Clamp(_degree + (r < 0.35f ? -1 : r < 0.7f ? 1 : r < 0.85f ? -2 : 2), -3, 9);
        Strike(Hz(_degree), 0.22f + Unit() * 0.15f);
        if (Unit() < 0.2f) { _rizLeft = 3; _nextRiz = _sample + Rate * 9 / 100; }
        _nextNote = _sample + Pulse * (Unit() < 0.3f ? 2 : 1);
    }

    // ——— birds and crickets ———
    private float _birdHz, _birdSweep, _birdAge, _birdLength, _birdPhase, _birdPan; private int _birdRepeats; private long _nextBird; private float _cricketPhase, _cricketBeat;
    private float Bird()
    {
        if (_birdAge >= _birdLength)
        {
            if (_birdRepeats > 0) { _birdRepeats--; _birdAge = 0; }
            else if (_sample >= _nextBird) { _birdHz = 2200 + Unit() * 3000; _birdSweep = (Unit() - 0.4f) * 3000; _birdLength = 0.05f + Unit() * 0.12f; _birdRepeats = (int)(Unit() * 4); _birdAge = 0; _birdPan = Unit() * 2 - 1; _nextBird = _sample + (long)(Rate * (0.6 + Unit() * 3.5)); }
            else return 0;
        }
        float t = _birdAge / _birdLength, hz = _birdHz + _birdSweep * t; _birdPhase += hz / Rate; _birdAge += 1f / Rate;
        return MathF.Sin(MathF.Tau * _birdPhase) * MathF.Sin(MathF.PI * t) * 0.10f;
    }
    private float Crickets()
    {
        _cricketBeat = (_cricketBeat + 1f / Rate) % 990; float t = _cricketBeat % 6, gate = MathF.Sin(MathF.Tau * 28 * t) > 0.2f ? 1 : 0;
        // a courtyard, not a forest: a short burst of three chirps every six seconds, and every third burst is left out
        float chirp = (int)(_cricketBeat / 6) % 3 != 0 && t < 0.7f && MathF.Sin(MathF.Tau * 4.3f * t) > 0.1f ? 1 : 0;
        _cricketPhase = (_cricketPhase + 4600f / Rate) % 1; return MathF.Sin(MathF.Tau * _cricketPhase) * gate * chirp * 0.02f;
    }

    // ——— the water: a band of noise that never repeats, rising and falling, and the odd drop ———
    private float _dropHz, _dropAge = 9;
    private float Water()
    {
        float w = White(); _lp += (w - _lp) * 0.5f; _hp += (_lp - _hp) * 0.08f;
        if (_dropAge > 1 && Unit() < 0.00015f) { _dropHz = 1300 + Unit() * 2600; _dropAge = 0; }
        float drop = _dropAge < 1 ? MathF.Sin(MathF.Tau * _dropHz * _dropAge * 0.05f) * MathF.Exp(-_dropAge * 6) * 0.35f : 0; _dropAge += 1f / (Rate * 0.05f);
        return (_lp - _hp) * 0.04f + drop;   // the plink of drops in the basin and a whisper of spray, no rushing
    }

    // ——— the whole ———
    private readonly float[] _room = new float[Rate * 53 / 1000]; private int _roomAt;
    private float _droneLow, _droneFifth;

    /// <summary>Fills <paramref name="stereo"/> (left, right, left...; <paramref name="frames"/> of them) with the next of the soundscape.</summary>
    public void Render(Span<short> stereo, int frames)
    {
        var goal = _target;
        for (int i = 0; i < frames; i++, _sample++)
        {
            const float Glide = 1f / (Rate * 0.4f);
            _now = new(_now.Music + (goal.Music - _now.Music) * Glide * 4, _now.Water + (goal.Water - _now.Water) * Glide * 4, _now.Day + (goal.Day - _now.Day) * Glide * 4, _now.Indoor + (goal.Indoor - _now.Indoor) * Glide * 4);
            float outdoors = 1 - _now.Indoor * 0.85f;
            _brown = _brown * 0.998f + White() * 0.02f; _gust += (White() - _gust) * 0.00002f;
            float wind = _brown * (0.5f + 2.2f * MathF.Abs(_gust)) * 0.9f * outdoors;
            float bird = Bird() * _now.Day * outdoors * 2, cricket = Crickets() * (1 - _now.Day) * outdoors;
            float water = Water() * _now.Water * 0.5f;
            float left = wind + water + bird * (0.5f - _birdPan * 0.4f) + cricket, right = wind + water + bird * (0.5f + _birdPan * 0.4f) + cricket;
            float music = 0, musicR = 0;
            if (_now.Music > 0.002f)
            {
                Play(); _droneLow = (_droneLow + Tonic / 2 / Rate) % 1; _droneFifth = (_droneFifth + Tonic * 0.75f / Rate) % 1;
                float dry = Strings() * 0.35f + Tombak() + (MathF.Sin(MathF.Tau * _droneLow) + 0.5f * MathF.Sin(MathF.Tau * _droneFifth)) * 0.02f, wet = _room[_roomAt]; _room[_roomAt] = dry + wet * 0.55f; _roomAt = (_roomAt + 1) % _room.Length;
                music = musicR = (dry + wet * 0.35f) * _now.Music * 0.55f;
            }
            stereo[i * 2] = Out(left + music); stereo[i * 2 + 1] = Out(right + musicR);
        }
    }

    private static short Out(float x) => (short)(MathF.Tanh(x * 1.4f) * 0.7f * short.MaxValue);   // a soft ceiling: nothing clips whatever the layers add up to
}
