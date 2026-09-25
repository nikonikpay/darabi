using Xunit;
namespace Mazesta.Diagnostics.Tests;

/// <summary>T5: the values an executor reads for its options.</summary>
public class TestOptionsTests
{
    private static readonly TestDefinition Def = new(new TestId("t"), "Test_Cpu_Matrix", 10,
        [new TestOption("size", "Test_Option_FileMb", TestOptionKind.Integer, "64"), new TestOption("target", "Test_Option_PingTarget", TestOptionKind.Text, "1.1.1.1")]);

    [Fact] public void A_chosen_value_wins_and_anything_not_chosen_or_left_empty_is_the_declared_default()
    {
        var o = new TestOptions(Def, new Dictionary<string, string> { ["size"] = "128", ["target"] = "" });
        Assert.Equal(128, o.GetInt("size")); Assert.Equal("1.1.1.1", o.Get("target"));
        Assert.Equal(64, TestOptions.None(Def).GetInt("size"));
    }
    [Fact] public void An_undeclared_option_is_a_programming_error_not_a_silent_default()
        => Assert.Throws<KeyNotFoundException>(() => TestOptions.None(Def).Get("drive"));
    [Fact] public void A_value_that_is_not_a_whole_number_is_refused_by_name()
        => Assert.Contains("'size'", Assert.Throws<FormatException>(() => new TestOptions(Def, new Dictionary<string, string> { ["size"] = "12.5" }).GetInt("size")).Message);
}
