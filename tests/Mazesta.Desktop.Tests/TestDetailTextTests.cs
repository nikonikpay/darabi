using Xunit; using Mazesta.Desktop.Localization; using Mazesta.Desktop.Services;
namespace Mazesta.Desktop.Tests;

public class TestDetailTextTests
{
    // The line of the owner's own run (RTX 4070, steady GPU load), as the executor wrote it.
    private const string Gpu = "GPU Steady compute stress on NVIDIA GeForce RTX 4070; dispatches=21360; 6813 Gop/s integer; verified 2,734,080 of 44,795,166,720 thread results through chained dispatches (a sample, not every thread); measured GPU load avg 93.5% (n=11); GPU core avg 68.3°C max 72.0°C (n=11); GPU hot spot avg 87.2°C max 94.6°C (n=11); GPU power avg 176.3 W max 196.2 W (n=11)";

    [Fact] public void A_known_detail_becomes_one_worded_line_per_part_with_the_same_numbers()
    {
        var lines = TestDetailText.Lines(Gpu);
        Assert.Equal(8, lines.Count); Assert.All(lines, l => Assert.False(l.Latin));
        Assert.Equal(Loc.Format("Detail_GpuStress", Loc.Get("Detail_Profile_Steady"), "NVIDIA GeForce RTX 4070"), lines[0].Text);
        Assert.Equal(Loc.Format("Detail_Stat", Loc.Get("Detail_L_GpuLoad"), "93.5", "%", "11"), lines[4].Text);
        Assert.Equal(Loc.Format("Detail_Stat_Max", Loc.Get("Detail_L_GpuHotSpot"), "87.2", "94.6", "°C", "11"), lines[6].Text);
        Assert.Contains("44,795,166,720", lines[3].Text);
    }

    // The processor's matrix load, as its executor writes it (the sentence the owner saw untranslated), on a hybrid processor.
    [Fact] public void The_processor_tests_are_worded_too_and_a_bracket_s_semicolon_does_not_cut_a_part()
    {
        var lines = TestDetailText.Lines("matrix load 64x64, 4 fixed input sets, every product checked in full against a precomputed checksum; threads=24; iterations=18250; ran on 24 of 24 logical processors (P-cores 16/16 threads; E-cores 8/8 threads); 26.1 GFLOPS (FP64, scalar); measured CPU load avg 99.1% (n=30); CPU temperature avg 81.0°C max 88.0°C (n=30)");
        Assert.Equal(7, lines.Count); Assert.All(lines, l => Assert.False(l.Latin));
        Assert.Equal(Loc.Format("Detail_Cpu_Matrix", "64", "4"), lines[0].Text);
        Assert.Contains(Loc.Format("Detail_Cpu_PCores", "16", "16"), lines[3].Text); Assert.Contains(Loc.Format("Detail_Cpu_ECores", "8", "8"), lines[3].Text);
        Assert.Equal(Loc.Format("Detail_Rate_Gflops", "26.1", " (FP64, scalar)"), lines[4].Text);
    }

    [Fact] public void A_sentence_with_its_own_semicolon_is_one_line_and_somebody_else_s_message_follows_on_its_own()
    {
        var one = Assert.Single(TestDetailText.Lines("Only 512 MiB of free RAM is available above the OS reserve; at least 1024 MiB is needed."));
        Assert.False(one.Latin); Assert.Equal(Loc.Format("Detail_W_LittleRam", "512", "1024"), one.Text);
        var io = TestDetailText.Lines("I/O error while exercising the drive: The device is not ready.");
        Assert.Equal([false, true], io.Select(l => l.Latin)); Assert.Equal("The device is not ready.", io[1].Text);
    }

    [Fact] public void The_power_test_names_each_half_and_words_what_follows()
    {
        var lines = TestDetailText.Lines("CPU [Passed]: matrix load 64x64, 4 fixed input sets, every product checked in full against a precomputed checksum; threads=8; GPU [Failed]: GPU Steady compute stress on RTX 4070; dispatches=10");
        Assert.All(lines, l => Assert.False(l.Latin)); Assert.Equal(6, lines.Count);
        Assert.Equal(Loc.Format("Detail_Half", Loc.Get("Detail_Half_Cpu"), Loc.Get("Test_Outcome_Passed")), lines[0].Text);
        Assert.Equal(Loc.Format("Detail_Half", Loc.Get("Detail_Half_Gpu"), Loc.Get("Test_Outcome_Failed")), lines[3].Text);
    }

    [Fact] public void A_part_that_is_not_recognised_is_kept_as_written_on_its_own_line()
    {
        var lines = TestDetailText.Lines("radix-2 complex FFT, N=1024; threads=24; something new avg 3.0 kPa (n=2)");
        Assert.Equal([true, false, true], lines.Select(l => l.Latin)); Assert.Equal("radix-2 complex FFT, N=1024", lines[0].Text); Assert.Equal("something new avg 3.0 kPa (n=2)", lines[2].Text);
        Assert.Empty(TestDetailText.Lines(null)); Assert.Empty(TestDetailText.Lines("  "));
    }
    // The garden scene test as the owner's RTX 3090 run wrote it: every part is worded, and the heat, clock, power and load stand apart as figures, the hot spot among them.
    private const string Scene = "Direct3D 12 + ray tracing Persian garden drawn at 1920x1080 (window 2560x1440) on NVIDIA GeForce RTX 3090; 9,497,180 triangles in 20,524 objects, centre model 'Mazesta logo'; load level 3: 9.50 M triangles · 20,524 objects · 59 lamps · ray-traced shadows (2 a light) and reflections · MSAA 4×; frames=658; 32.8 FPS average; 26.6 FPS lowest half-second; check frames=7; measured GPU load avg 82.9% (n=44); GPU power avg 216.1 W max 244.6 W (n=44); GPU temperature avg 63.9°C max 67.0°C (n=44); GPU hot spot avg 76.8°C max 80.2°C (n=44); GPU clock avg 1850.5 MHz max 1875.0 MHz (n=44); GPU memory clock avg 9752.0 MHz max 9752.0 MHz (n=44); CPU clock peak 4500 MHz; CPU clock avg 339 MHz; CPU load avg 9 %; CPU power peak 87 W; RAM speed 2133 MT/s; RAM CAS latency (profile at that speed) 15 CL; Mazesta's own load: processor 0.4 cores busy on average (peak 3.9; 1 % of the whole processor), RAM 1872 MB on average (peak 1901 MB)";

    [Fact] public void A_scene_result_has_no_untranslated_line_and_its_key_figures_come_first_with_the_highest_values()
    {
        var view = TestDetailText.View(Scene);
        Assert.All(view.Lines, l => Assert.False(l.Latin, l.Text));
        Assert.Equal(["67.0 °C", "80.2 °C", "1875.0 MHz", "244.6 W", "82.9 %", "32.8 FPS", "26.6 FPS", "9752.0 MHz"], view.Figures.Select(f => f.Value));
        Assert.Equal(Loc.Format("Detail_Fig_Max", Loc.Get("Detail_L_GpuHotSpot")), view.Figures[1].Name);
        Assert.Equal(Loc.Format("Detail_Fig_Note", "76.8", "°C", "44"), view.Figures[1].Note);
        Assert.Contains(view.Lines, l => l.Text == Loc.Format("Detail_Cpu_ClockPeak", "4500"));
        Assert.DoesNotContain(view.Lines, l => l.Text.Contains("M triangles"));
    }

    [Fact] public void A_detail_without_stats_has_no_figures_and_the_same_lines_as_before()
    {
        var view = TestDetailText.View("RAM pattern test; tested=1,024 MiB");
        Assert.Empty(view.Figures); Assert.Equal(TestDetailText.Lines("RAM pattern test; tested=1,024 MiB").Select(l => l.Text), view.Lines.Select(l => l.Text));
        Assert.Empty(TestDetailText.View(null).Figures);
    }
}
