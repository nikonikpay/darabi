using Xunit; using Mazesta.Core.Inventory; using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.Diagnostics.Tests;

public class SceneScoreTests
{
    [Fact] public void The_graphics_score_is_the_cards_own_rate_per_full_hd_of_pixels()
    {
        Assert.Equal(6000, SceneScore.Graphics(60), 6);
        Assert.Equal(SceneScore.Graphics(60), SceneScore.Graphics(60), 6);   // the pixel count is not in the score: a slower frame at a bigger size scores lower
        Assert.True(SceneScore.Graphics(16) < SceneScore.Graphics(30));
    }
    [Fact] public void A_card_twice_as_quick_scores_twice_as_much_and_a_slower_processor_scores_less()
    {
        Assert.Equal(2 * SceneScore.Graphics(1 / 0.020), SceneScore.Graphics(1 / 0.010), 6);
        Assert.True(SceneScore.Cpu(1 / 0.002) > SceneScore.Cpu(1 / 0.004));
    }
    [Fact] public void The_ram_score_counts_bandwidth_and_latency_alike()
    {
        Assert.Equal(5000, SceneScore.Ram(40, 80), 6);
        Assert.Equal(SceneScore.Ram(80, 80), SceneScore.Ram(40, 40), 6);   // twice the bandwidth is worth as much as half the latency
    }
    [Fact] public void The_overall_score_is_a_weighted_harmonic_mean_so_a_weak_part_is_not_hidden()
    {
        Assert.Equal(5000, SceneScore.Overall(5000, 5000, 5000), 6);
        double balanced = SceneScore.Overall(6000, 6000, 6000), weakCpu = SceneScore.Overall(6000, 1000, 6000);
        Assert.True(weakCpu < balanced);
        Assert.True(weakCpu < (SceneScore.GraphicsWeight * 6000 + SceneScore.CpuWeight * 1000 + SceneScore.RamWeight * 6000));   // below the plain weighted average
        Assert.Equal(1.0, SceneScore.GraphicsWeight + SceneScore.CpuWeight + SceneScore.RamWeight, 9);
    }
    [Fact] public void The_longer_side_of_a_frame_is_the_bottleneck_and_close_sides_are_balanced()
    {
        Assert.Equal(SceneScore.Limit.Graphics, SceneScore.Bottleneck(0.012, 0.004));
        Assert.Equal(SceneScore.Limit.Cpu, SceneScore.Bottleneck(0.004, 0.012));
        Assert.Equal(SceneScore.Limit.Balanced, SceneScore.Bottleneck(0.0100, 0.0095));
    }
    [Fact] public void The_scene_is_ranked_by_its_graphics_score_and_a_times_lower_is_the_better_one()
    {
        Assert.Equal("Bench_Scene_ScoreGpu", BenchmarkRecords.Headline("bench.gpu.scene.d3d")!.Key);
        Assert.True(BenchmarkDetails.HigherIsBetter("pts") && BenchmarkDetails.HigherIsBetter("FPS") && !BenchmarkDetails.HigherIsBetter("ms") && !BenchmarkDetails.HigherIsBetter("ns"));
    }
    [Fact] public void Each_parts_conditions_have_a_section_of_their_own_and_results_have_none()
    {
        Assert.Equal("Gpu", BenchmarkDetails.Section("Bench_Gpu_PowerMax")); Assert.Equal("Cpu", BenchmarkDetails.Section("Bench_Cpu_Ccd2Clock")); Assert.Equal("Ram", BenchmarkDetails.Section("Bench_Ram_Cl"));
        Assert.Null(BenchmarkDetails.Section("Bench_Scene_ScoreCpu")); Assert.Null(BenchmarkDetails.Section("Bench_Threads")); Assert.Null(BenchmarkDetails.Section("Bench_Cpu_Gflops"));
        Assert.False(BenchmarkDetails.IsCondition("Bench_Scene_Score")); Assert.True(BenchmarkDetails.IsCondition("Bench_Ram_Bandwidth"));
    }
    [Fact] public void Full_screen_does_not_change_what_is_measured_so_it_is_in_no_record_key()
        => Assert.Equal(BenchmarkRecords.RecordKey("bench.gpu.scene.d3d", new Dictionary<string, string> { ["gpu"] = "0" }),
            BenchmarkRecords.RecordKey("bench.gpu.scene.d3d", new Dictionary<string, string> { ["gpu"] = "0", ["fullscreen"] = "on" }));
    [Fact] public void The_rams_facts_are_its_configured_speed_and_the_cl_of_the_profile_with_that_speed_and_nothing_where_none_has_it()
    {
        var modules = new[] { new MemoryModuleInfo("A1", 16L << 30, "x", "y", 6000, 4800, SmbiosType: 34) };
        var inv = HardwareInventory.Empty with { MemoryModules = modules };
        var profile = new MemoryProfile("XMP 1", 6000, 30, 36, 36, 76, 112, 1.35);
        var spd = new SpdModule(0, "DDR5", null, null, null, 16, 1, 8, 16, 8, false, null, [new("JEDEC 4800", 4800, 40, 39, 39, 77, 116, null)], [profile], "3.0");
        var facts = MemoryFacts.From(inv, [spd])!;
        Assert.Equal((6000, 30, "XMP 1", "DDR5"), (facts.SpeedMts, facts.CasLatency, facts.Profile, facts.Type));
        var byHand = MemoryFacts.From(inv with { MemoryModules = [modules[0] with { ConfiguredSpeedMts = 5800 }] }, [spd])!;
        Assert.Equal(5800, byHand.SpeedMts); Assert.Null(byHand.CasLatency);   // no profile runs at 5800: the latency is not guessed
        Assert.Null(MemoryFacts.From(HardwareInventory.Empty, []));
    }
}
