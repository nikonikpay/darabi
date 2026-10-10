using System.Globalization; using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Memory;

/// <summary>
/// The steps of the RAM sequence test, after OCCT 17's memory blocks and TestMem5's configurations: a preset is a list of steps run in
/// order over all the RAM the test holds, with loops. Steps either write a pattern (fill), change the data in a way whose outcome is known
/// in advance (arithmetic, invert, mirror), only make traffic (flip twice, read), wait (idle), or check. A check compares every word with what
/// the steps since the last fill must have left there - worked out from the fill's generator and the transforms, never read back from the
/// machine under test - so many sweeps at full speed can run between two checks without losing a fault: a word that went wrong in any of
/// them is still wrong at the next check (the transforms are all one-to-one).
/// What the transforms are known to leave: an arithmetic pass x·A + B with an odd A and an invert ~x = −x − 1 are both affine maps mod 2^64,
/// so any run of them is one affine map (a, b); a mirror reverses the order of a block's chunks, a permutation that commutes with them.
/// Names follow the reference tools in spirit; the algorithms are this program's own.
/// </summary>
public static class MemorySequence
{
    public enum Op { Fill, Arith, Invert, Flip, Mirror, Read, Idle, Check, Loop }
    public enum Pattern { Simple, Random, Walk1, Walk0, Checker, Zeros, Ones }

    /// <param name="Count">Passes (sweeps) of the step, milliseconds for <see cref="Op.Idle"/>, rounds for <see cref="Op.Loop"/>.</param>
    /// <param name="Chunk">For a mirror: the size of the pieces whose order is reversed, in 64-bit words.</param>
    public sealed record Step(Op Op, int Count = 1, Pattern Fill = Pattern.Simple, ulong A = 0, ulong B = 0, int Chunk = 1, IReadOnlyList<Step>? Body = null)
    {
        public override string ToString() => Op switch
        {
            Op.Fill => $"fill {Fill.ToString().ToLowerInvariant()} {Count}", Op.Arith => $"arith {Count} 0x{A:X} 0x{B:X}", Op.Mirror => $"mirror {Chunk} {Count}",
            Op.Idle => $"idle {Count}", Op.Check => "check", Op.Loop => $"loop {Count}", _ => $"{Op.ToString().ToLowerInvariant()} {Count}",
        };
    }

    // ——— presets: the sequence each runs, in the same words a technician types for "custom" ———

    public const string Custom = "custom";
    public static readonly IReadOnlyDictionary<string, string> Presets = new Dictionary<string, string>
    {
        // Every kind of step once per round: the shortest run that has touched every pattern and transform.
        ["quick"] = "fill simple; check; fill random; mirror 128 3; check; fill checker; invert 1; check; fill walk1; check; fill walk0; check; arith 4 0 0; check",
        // After TestMem5's "Extreme1" (anta777) as OCCT 17 ships it: many full sweeps between checks, from SSE-sized to page-sized pieces.
        ["extreme"] = "loop 8 { fill simple 4; check } loop 8 { fill random 16; check } loop 8 { fill random; mirror 128 32; check } "
                    + "loop 8 { fill random; mirror 128 256; check } loop 8 { fill random; mirror 2 64; mirror 4 64; check } loop 8 { arith 16 0 0; check } loop 8 { arith 32 0 0; invert 31; check }",
        // Heat: the most writes per second the RAM takes, little time spent checking - how DDR5 modules reach the temperature where marginal ones fail.
        ["heat"] = "loop 4 { fill random 4; mirror 128 32; flip 16; mirror 4 32; arith 16 0 0; check }",
        // Spike: full-speed sweeps on every thread broken by pauses, so the memory controller and its supply go from idle to flat out over and over.
        ["spike"] = "loop 12 { fill random; idle 1500; mirror 128 8; idle 300; flip 4; idle 50; read 2; idle 900; invert 3; check; idle 2500 }",
        // Margin probe, after the community "AM5 Margin Probe" preset: pattern changes, inversion-heavy passes and short idle breaks, checked often.
        ["margin"] = "loop 2 { arith 4 0x9E3779B9 0x7F4A7C15; check; mirror 128 6; idle 18; check; invert 31; check } "
                   + "loop 4 { flip 1; mirror 2 1; check; mirror 4 1; mirror 1 1; check; idle 12; check } "
                   + "read 1; check; fill walk0; check; fill walk1; check; fill checker; idle 35; check; "
                   + "loop 4 { arith 85 0xAAAAAAAA 0x55555555; flip 170; invert 255; check; mirror 128 2; mirror 2 1; mirror 4 1; check; idle 40 }",
    };

    // ——— parsing ———

    /// <summary>Reads a sequence: steps separated by ';', spaces or new lines, <c>loop N { … }</c> for repeats. Numbers are decimal or 0x hex.
    /// Throws <see cref="FormatException"/> naming the word it could not read.</summary>
    public static IReadOnlyList<Step> Parse(string text)
    {
        var words = text.Replace("{", " { ").Replace("}", " } ").Split([' ', ';', '\n', '\r', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
        int at = 0;
        var steps = Block(words, ref at, top: true);
        if (steps.Count == 0) throw new FormatException("The sequence has no steps.");
        if (!steps.Any(HasCheck)) throw new FormatException("The sequence never checks: add a 'check' step, or nothing it does can show a fault.");
        return steps;
    }

    private static bool HasCheck(Step s) => s.Op == Op.Check || (s.Body?.Any(HasCheck) ?? false);

    private static List<Step> Block(string[] w, ref int at, bool top)
    {
        var steps = new List<Step>();
        while (at < w.Length)
        {
            string word = w[at++].ToLowerInvariant();
            if (word == "}") { if (top) throw new FormatException("A '}' closes no loop."); return steps; }
            steps.Add(word switch
            {
                "loop" => Loop(w, ref at),
                "fill" => new(Op.Fill, Fill: Enum.TryParse<Pattern>(Next(w, ref at, "fill"), true, out var p) ? p : throw new FormatException($"Unknown fill pattern '{w[at - 1]}' (simple, random, walk1, walk0, checker, zeros, ones)."), Count: Optional(w, ref at, 1)),
                "arith" => new(Op.Arith, Number(w, ref at, "arith"), A: Optional(w, ref at, 0UL), B: Optional(w, ref at, 0UL)),
                "invert" => new(Op.Invert, Optional(w, ref at, 1)),
                "flip" => new(Op.Flip, Optional(w, ref at, 1)),
                "read" => new(Op.Read, Optional(w, ref at, 1)),
                "idle" => new(Op.Idle, Number(w, ref at, "idle")),
                "mirror" => Mirror(Number(w, ref at, "mirror"), Optional(w, ref at, 1)),
                "check" => new(Op.Check),
                _ => throw new FormatException($"Unknown step '{word}' (fill, arith, invert, flip, mirror, read, idle, check, loop)."),
            });
        }
        if (!top) throw new FormatException("A loop has no closing '}'.");
        return steps;
    }

    private static Step Loop(string[] w, ref int at)
    {
        int n = Number(w, ref at, "loop");
        if (at >= w.Length || w[at++] != "{") throw new FormatException("'loop N' must be followed by '{'.");
        return new(Op.Loop, n, Body: Block(w, ref at, top: false));
    }

    private static Step Mirror(int chunk, int count)
        => chunk is >= 1 and <= 1024 && (chunk & (chunk - 1)) == 0 ? new(Op.Mirror, count, Chunk: chunk) : throw new FormatException($"A mirror's piece must be 1, 2, 4 … 1024 words, not {chunk}.");

    private static string Next(string[] w, ref int at, string step) => at < w.Length ? w[at++] : throw new FormatException($"'{step}' needs a value.");

    private static int Number(string[] w, ref int at, string step)
    {
        string s = Next(w, ref at, step);
        return TryNumber(s, out ulong v) && v is >= 1 and <= 1_000_000 ? (int)v : throw new FormatException($"'{step}' needs a number from 1 to 1000000, not '{s}'.");
    }

    private static int Optional(string[] w, ref int at, int fallback)
    {
        if (at >= w.Length || !TryNumber(w[at], out ulong v)) return fallback;
        at++; return v is >= 1 and <= 1_000_000 ? (int)v : throw new FormatException($"A count must be from 1 to 1000000, not '{w[at - 1]}'.");
    }

    private static ulong Optional(string[] w, ref int at, ulong fallback) { if (at >= w.Length || !TryNumber(w[at], out ulong v)) return fallback; at++; return v; }

    private static bool TryNumber(string s, out ulong v)
        => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? ulong.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) : ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v);

    // ——— what the RAM must hold ———

    /// <summary>What every word must hold now: the last fill's value at a position the mirrors since moved there, through one affine map.</summary>
    public sealed class Expected
    {
        public Pattern Fill { get; private set; } = Pattern.Random;
        public ulong Seed { get; private set; }
        public ulong A { get; private set; } = 1;
        public ulong B { get; private set; }
        private readonly List<int> _mirrors = [];   // chunk sizes, oldest first; two equal neighbours cancel and are dropped

        public void Filled(Pattern p, ulong seed) { Fill = p; Seed = seed; A = 1; B = 0; _mirrors.Clear(); }
        public void Arith(ulong a, ulong b) { A *= a; B = B * a + b; }
        public void Inverted() { A = 0 - A; B = 0 - B - 1; }
        public void Mirrored(int chunk) { if (_mirrors.Count > 0 && _mirrors[^1] == chunk) _mirrors.RemoveAt(_mirrors.Count - 1); else _mirrors.Add(chunk); }

        public ulong At(long blockIndex, long i, long words)
        {
            for (int m = _mirrors.Count - 1; m >= 0; m--) i = MirrorIndex(i, _mirrors[m], words);
            return A * Generate(Fill, Seed, blockIndex * words + i) + B;
        }
    }

    /// <summary>Where the word at <paramref name="i"/> came from after the order of the block's <paramref name="chunk"/>-word pieces was reversed.</summary>
    public static long MirrorIndex(long i, int chunk, long words) => (words / chunk - 1 - i / chunk) * chunk + i % chunk;

    /// <summary>The value a fill writes at global word <paramref name="g"/>. Simple holds its own address (an addressing fault shows), checker
    /// alternates 01… and 10… between neighbouring words and shifts every 8 KiB (one DRAM page), walking bits move one place per word.</summary>
    public static ulong Generate(Pattern p, ulong seed, long g) => p switch
    {
        Pattern.Simple => (ulong)g ^ ~((ulong)g << 32) ^ seed,
        Pattern.Random => SplitMix((ulong)g ^ seed * 0xD1B54A32D192ED03UL),
        Pattern.Walk1 => 1UL << (int)(((ulong)g + seed) & 63),
        Pattern.Walk0 => ~(1UL << (int)(((ulong)g + seed) & 63)),
        Pattern.Checker => (((ulong)g + ((ulong)g >> 10) + seed) & 1) == 0 ? 0x5555555555555555UL : 0xAAAAAAAAAAAAAAAAUL,
        Pattern.Zeros => 0UL,
        _ => ulong.MaxValue,
    };

    private static ulong SplitMix(ulong x) { x += 0x9E3779B97F4A7C15UL; x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL; x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL; return x ^ (x >> 31); }

    /// <summary>The multiplier and addend of an arithmetic step: 0 means the default, and the multiplier is made odd, since an even one would
    /// lose the top bit of every word and with it the ability to tell a wrong word from a right one.</summary>
    public static (ulong A, ulong B) ArithConstants(Step s) => ((s.A == 0 ? 0x9E3779B97F4A7C15UL : s.A) | 1, s.B == 0 ? 0x7F4A7C15UL : s.B);

    // ——— the passes over one block ———

    public static void Fill(Span<ulong> w, Pattern p, ulong seed, long blockIndex)
    {
        long g0 = blockIndex * w.Length;
        switch (p)
        {
            case Pattern.Zeros: w.Clear(); return;
            case Pattern.Ones: w.Fill(ulong.MaxValue); return;
            default: for (int i = 0; i < w.Length; i++) w[i] = Generate(p, seed, g0 + i); return;
        }
    }

    public static void Arith(Span<ulong> w, ulong a, ulong b) { for (int i = 0; i < w.Length; i++) w[i] = w[i] * a + b; }

    public static void Invert(Span<ulong> w)
    {
        var v = MemoryMarshal.Cast<ulong, System.Numerics.Vector<ulong>>(w);
        for (int i = 0; i < v.Length; i++) v[i] = ~v[i];
        for (int i = v.Length * System.Numerics.Vector<ulong>.Count; i < w.Length; i++) w[i] = ~w[i];
    }

    /// <summary>Reverses the order of the block's <paramref name="chunk"/>-word pieces (one word: the whole block back to front). <paramref name="scratch"/> holds one piece.</summary>
    public static void Mirror(Span<ulong> w, int chunk, Span<ulong> scratch)
    {
        if (chunk == 1) { w.Reverse(); return; }
        int pieces = w.Length / chunk;
        for (int k = 0; k < pieces / 2; k++)
        {
            var x = w.Slice(k * chunk, chunk); var y = w.Slice((pieces - 1 - k) * chunk, chunk);
            x.CopyTo(scratch); y.CopyTo(x); scratch[..chunk].CopyTo(y);
        }
    }

    public static ulong Read(ReadOnlySpan<ulong> w) { ulong x = 0; foreach (ulong v in w) x ^= v; return x; }

    /// <summary>The words of a block that differ from what <paramref name="e"/> says they must hold, and the bits that differ in them.</summary>
    public static (long Words, long Bits) Check(ReadOnlySpan<ulong> w, Expected e, long blockIndex)
    {
        long words = 0, bits = 0;
        for (int i = 0; i < w.Length; i++)
        {
            ulong d = w[i] ^ e.At(blockIndex, i, w.Length);
            if (d != 0) { words++; bits += System.Numerics.BitOperations.PopCount(d); }
        }
        return (words, bits);
    }
}
