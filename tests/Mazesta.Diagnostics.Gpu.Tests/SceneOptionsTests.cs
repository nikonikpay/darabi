using Xunit; using Mazesta.Diagnostics.Gpu.Benchmarks; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class SceneOptionsTests
{
    [Fact] public void Both_scenes_start_full_screen_and_offer_no_full_screen_size_among_the_resolutions()
    {
        foreach (var d in new[] { GpuSceneExecutor.Scene, GpuSceneBenchmark.Scene })
        {
            Assert.Equal("on", d.Options.Single(o => o.Key == GpuSceneExecutor.FullScreenOption).Default);
            Assert.DoesNotContain(d.Options.Single(o => o.Key == GpuSceneExecutor.ResolutionOption).Choices!().Select(c => c.Value), v => v == "fullscreen");
        }
    }
    [Fact] public void A_size_is_written_width_first_and_kept_left_to_right_in_a_right_to_left_page()
    {
        foreach (var c in GpuSceneExecutor.Sizes())
        {
            Assert.StartsWith("\u2066", c.Label); Assert.EndsWith("\u2069", c.Label);
            var (w, h) = GpuSceneExecutor.ParseSize(c.Value);
            Assert.True(w > h); Assert.Contains($"{w} × {h}", c.Label);
        }
    }
    [Fact] public void The_ai_test_and_the_scene_test_are_registered_tests_of_the_gpu()
    {
        Assert.Equal("gpu.ai", GpuAiExecutor.Definition.Id.Value);
        Assert.Equal("gpu.scene.d3d", GpuSceneExecutor.Scene.Id.Value);
    }
}
