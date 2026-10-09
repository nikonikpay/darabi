using Mazesta.Core.Tuning; using Xunit;
namespace Mazesta.Core.Tests;

public sealed class GpuAutoRulesTests
{
    private static readonly GpuRules Rules = new("Game", [new("Lumion.exe", "Lumion", "Render"), new("3dsmax.exe", "3ds Max", "Modelling")]);
    private static HashSet<string> Running(params string[] n) => [.. n];

    [Fact] public void A_listed_program_in_front_wins_over_a_game_and_over_one_behind()
        => Assert.Equal("Render", GpuAutoRules.Decide(Rules, Running("lumion.exe", "3dsmax.exe"), "lumion.exe", true));

    [Fact] public void A_full_screen_game_gets_the_game_profile_even_with_a_program_open_behind()
        => Assert.Equal("Game", GpuAutoRules.Decide(Rules, Running("3dsmax.exe", "game.exe"), "game.exe", true));

    [Fact] public void A_listed_program_running_behind_counts_when_nothing_else_is_in_use()
        => Assert.Equal("Modelling", GpuAutoRules.Decide(Rules, Running("3dsmax.exe", "chrome.exe"), "chrome.exe", false));

    [Fact] public void Nothing_in_use_means_no_profile_so_the_card_goes_back_to_its_own()
        => Assert.Null(GpuAutoRules.Decide(Rules, Running("chrome.exe"), "chrome.exe", false));

    [Fact] public void Without_a_game_profile_a_game_changes_nothing()
        => Assert.Null(GpuAutoRules.Decide(new(null, Rules.Apps), Running("game.exe"), "game.exe", true));

    [Theory]
    [InlineData(@"C:\Program Files\Lumion\Lumion.exe", "Lumion.exe")]
    [InlineData(@"""C:\Apps\D5 Render\D5Render.exe"",0", "D5Render.exe")]
    [InlineData(@"C:\Apps\x\uninstall.ico", null)]
    [InlineData(@"", null)]
    [InlineData(null, null)]
    public void An_executable_name_comes_out_of_a_path_or_an_icon_value(string? input, string? expected) => Assert.Equal(expected, GpuAutoRules.ExeName(input));
}
