namespace Mazesta.Diagnostics.Cpu;

/// <summary>The processor's tests as the stages of one (see <see cref="StageCycleExecutor"/>): instruction width × data set × load shape, after OCCT's CPU presets.</summary>
public static class CpuCycle
{
    /// <summary>A stage is "test:option=value,option=value"; the first preset is the default.</summary>
    public static readonly IReadOnlyDictionary<string, string> Presets = new Dictionary<string, string>
    {
        // Every instruction width, then the work that loads the caches and memory (FFT, Linpack) and the integer/hash/matrix mix.
        ["full"] = "cpu.vector:width=sse cpu.vector:width=avx2 cpu.vector:width=avx512 cpu.fft cpu.linpack:memory=single cpu.stress:pattern=steady",
        // The same arithmetic on each width in turn: a CPU (or undervolt) that holds on SSE and fails on AVX2 / AVX-512 shows here.
        ["instructions"] = "cpu.vector:width=sse cpu.vector:width=avx2 cpu.vector:width=avx512",
        // Load that swings and a single core that boosts and rests: where curve offsets and Vmin faults show, not at a steady full load.
        ["transient"] = "cpu.stress:pattern=variable cpu.singlecore:load=variable cpu.vector:width=avx2 cpu.stress:pattern=variable,high=100,low=20,highSeconds=3,lowSeconds=2",
    };

    public static readonly TestDefinition Definition = new(new TestId("cpu.cycle"), "Test_Cpu_Cycle", 1200, StageCycleExecutor.Options(Presets, "Test_CpuPreset_", "Test_Option_CpuStages"));

    /// <summary>The cycle over this program's own CPU tests (fresh instances, so a stage never shares state with the same test in the queue).</summary>
    public static StageCycleExecutor Standard(Memory.IMemoryProbe probe) => new(Definition, Presets, new ITestExecutor[]
        { new CpuStressExecutor(), new CpuCoreCycleExecutor(), new LinpackExecutor(probe), new CpuVectorStressExecutor(), new CpuFftExecutor(probe) }
        .ToDictionary(t => t.Definition.Id.Value));
}
