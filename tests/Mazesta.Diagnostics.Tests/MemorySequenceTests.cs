using Xunit; using Mazesta.Diagnostics.Memory; using Mazesta.Diagnostics.Tests.Fakes; using static Mazesta.Diagnostics.Memory.MemorySequence;
namespace Mazesta.Diagnostics.Tests;

public class MemorySequenceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private sealed class FixedProbe(long total, long available) : IMemoryProbe { public MemoryStatus Read() => new(total, available); }

    [Fact] public void Every_preset_reads_and_checks() { foreach (var p in Presets.Values) Assert.NotEmpty(Parse(p)); }

    [Fact] public void Loops_nest_and_numbers_may_be_hex()
    {
        var s = Parse("loop 2 { fill checker 0x3; loop 4 { mirror 128 2 } check } arith 4 0x9E3779B9 0x7F4A7C15; check");
        Assert.Equal(Op.Loop, s[0].Op); Assert.Equal(3, s[0].Body![0].Count); Assert.Equal(Op.Loop, s[0].Body![1].Op); Assert.Equal(128, s[0].Body![1].Body![0].Chunk);
        Assert.Equal(0x9E3779B9UL, s[1].A); Assert.Equal(0x7F4A7C15UL, s[1].B);
    }

    [Theory]
    [InlineData("fill simple")] [InlineData("loop 2 { check")] [InlineData("check }")] [InlineData("mirror 3; check")] [InlineData("idle; check")] [InlineData("dance; check")] [InlineData("fill plaid; check")]
    public void A_sequence_that_cannot_be_read_or_never_checks_is_refused(string text) => Assert.Throws<FormatException>(() => Parse(text));

    [Fact] public void The_expectation_follows_every_transform_and_a_flipped_bit_is_one_wrong_word()
    {
        var w = new ulong[8192]; var e = new Expected(); var scratch = new ulong[1024];
        Fill(w, Pattern.Random, 7, 2); e.Filled(Pattern.Random, 7);
        var (a, b) = ArithConstants(new Step(Op.Arith));
        for (int i = 0; i < 3; i++) { Arith(w, a, b); e.Arith(a, b); }
        Invert(w); e.Inverted();
        Mirror(w, 128, scratch); e.Mirrored(128); Mirror(w, 4, scratch); e.Mirrored(4); Mirror(w, 1, scratch); e.Mirrored(1);
        Invert(w); Invert(w);   // a flip leaves the data as it was
        Assert.Equal((0L, 0L), Check(w, e, 2));
        Assert.True(Check(w, e, 3).Words > 8000);   // another block's data is not this block's
        w[5000] ^= 1UL << 17;
        Assert.Equal((1L, 1L), Check(w, e, 2));
    }

    [Fact] public void Two_equal_mirrors_cancel() { var e = new Expected(); e.Filled(Pattern.Simple, 1); var before = e.At(0, 10, 1024); e.Mirrored(4); e.Mirrored(4); Assert.Equal(before, e.At(0, 10, 1024)); }

    [Fact] public void Checkerboard_neighbours_hold_opposite_bits() => Assert.Equal(ulong.MaxValue, Generate(Pattern.Checker, 0, 4) ^ Generate(Pattern.Checker, 0, 5));

    [Fact] public async Task A_short_run_of_a_preset_passes_with_its_rounds_in_the_account()
    {
        var options = new TestOptions(MemorySequenceExecutor.Definition, new Dictionary<string, string> { [MemorySequenceExecutor.PresetOption] = "quick", [MemoryPatternExecutor.SizeOption] = "64" });
        var result = await new MemorySequenceExecutor(new FixedProbe(64L << 30, 60L << 30)).RunAsync(new(2, new FakeClock(T0), null, null, options), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, result.Outcome); Assert.Equal(0, result.ErrorCount); Assert.Contains("preset 'quick'", result.Detail); Assert.Contains("tested=64 MiB", result.Detail);
    }

    [Fact] public async Task A_custom_sequence_that_cannot_be_read_is_unsupported_not_failed()
    {
        var options = new TestOptions(MemorySequenceExecutor.Definition, new Dictionary<string, string> { [MemorySequenceExecutor.PresetOption] = Custom, [MemorySequenceExecutor.SequenceOption] = "fill random; mirror 128 4" });
        var result = await new MemorySequenceExecutor(new FixedProbe(64L << 30, 60L << 30)).RunAsync(new(2, new FakeClock(T0), null, null, options), CancellationToken.None);
        Assert.Equal(TestOutcome.Unsupported, result.Outcome); Assert.Contains("never checks", result.Detail);
    }
}
