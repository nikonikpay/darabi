namespace Mazesta.Diagnostics;

/// <summary>One test of a profile and how long it runs.</summary>
public sealed record ProfileTest(string TestId, int Seconds);

/// <summary>A ready-made selection of tests with their lengths for one purpose. A profile only picks tests and times: the minimum coverage is
/// each test's own business (single-core cycling is Inconclusive when a core went untested, the RAM test says how many patterns ran), so a
/// profile is never "complete" because its time ran out.</summary>
public sealed record TestProfile(string Id, string NameKey, string NoteKey, IReadOnlyList<ProfileTest> Tests);

public static class TestProfiles
{
    public static IReadOnlyList<TestProfile> All { get; } =
    [
        new("quick", "Profile_Quick", "Profile_Quick_Note",
            [new("cpu.matrix", 60), new("memory.pattern", 120), new("gpu.steady", 60), new("storage.sequential", 30), new("network.latency", 20), new("storage.smart", 5)]),
        new("standard", "Profile_Standard", "Profile_Standard_Note",
            [new("cpu.matrix", 300), new("cpu.vector", 300), new("cpu.linpack", 300), new("cpu.singlecore", 600), new("memory.pattern", 900),
             new("gpu.steady", 300), new("gpu.vram", 300), new("gpu.scene.d3d", 120), new("storage.sequential", 60), new("storage.random4k", 60),
             new("network.latency", 30), new("power.combined", 300), new("storage.smart", 5)]),
        new("deep", "Profile_Deep", "Profile_Deep_Note",
            [new("cpu.matrix", 900), new("cpu.vector", 900), new("cpu.linpack", 1200), new("cpu.integer", 600), new("cpu.fft", 900), new("cpu.hash", 600), new("cpu.singlecore", 1800),
             new("memory.pattern", 3600), new("memory.bitfade", 1200), new("gpu.steady", 900), new("gpu.variable", 600), new("gpu.pulse", 300), new("gpu.vram", 900), new("gpu.scene.d3d", 300),
             new("storage.sequential", 300), new("storage.random4k", 300), new("network.latency", 60), new("power.combined", 900),
             new("windows.dism", 300), new("windows.sfc", 900), new("storage.smart", 5)]),
        new("transient", "Profile_Transient", "Profile_Transient_Note",
            [new("cpu.singlecore", 1200), new("cpu.vector", 300), new("gpu.variable", 600), new("gpu.pulse", 600), new("power.combined", 600)]),
        new("osstorage", "Profile_OsStorage", "Profile_OsStorage_Note",
            [new("storage.sequential", 300), new("storage.random4k", 300), new("storage.smart", 5), new("windows.dism", 300), new("windows.sfc", 900)]),
    ];
}
