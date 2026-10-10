using Mazesta.Core.Inventory; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu;

/// <summary>The graphics card's tests as the stages of one (see <see cref="StageCycleExecutor"/>): steady, swinging and pulsed compute, the
/// memory, and the 3-D scene, with the card chosen once for all of them.</summary>
public static class GpuCycle
{
    public static readonly IReadOnlyDictionary<string, string> Presets = new Dictionary<string, string>
    {
        // Every kind of load once per round: compute steady and swinging, power steps, the memory, a real 3-D frame.
        ["full"] = "gpu.steady gpu.variable gpu.pulse gpu.vram gpu.scene.d3d",
        // Changes of load at three beats: where a weak power supply, a 12V-2x6 contact or a marginal clock shows (and coil whine is loudest).
        ["transient"] = "gpu.variable gpu.pulse:pulseMs=100,gapMs=400 gpu.pulse:pulseMs=1000,gapMs=1000 gpu.pulse:pulseMs=250,gapMs=250",
        // The card's memory between compute loads that heat it: a VRAM overclock or a hot memory chip.
        ["memory"] = "gpu.vram gpu.steady gpu.vram gpu.pulse",
    };

    public static readonly TestDefinition Definition = new(new TestId("gpu.cycle"), "Test_Gpu_Cycle", 1200, StageCycleExecutor.Options(Presets, "Test_GpuPreset_", "Test_Option_GpuStages", GpuDevices.Option));

    public static StageCycleExecutor Standard(MemoryFactsSource? facts) => new(Definition, Presets, new ITestExecutor[]
        { new GpuStressExecutor(GpuStressProfile.Steady), new GpuStressExecutor(GpuStressProfile.Variable), new GpuStressExecutor(GpuStressProfile.Pulse), new GpuVramExecutor(), new GpuSceneExecutor(facts) }
        .ToDictionary(t => t.Definition.Id.Value));
}
