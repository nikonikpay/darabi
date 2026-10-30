namespace Mazesta.Diagnostics;

/// <summary>One test of a profile, how long it runs, and the options it is set to (key → choice or value; the others keep their defaults).</summary>
public sealed record ProfileTest(string TestId, int Seconds, IReadOnlyDictionary<string, string>? Options = null);

/// <summary>A ready-made selection of tests with their lengths for one purpose. A profile only picks tests and times: the minimum coverage is
/// each test's own business (single-core cycling is Inconclusive when a core went untested, the RAM test says how many patterns ran), so a
/// profile is never "complete" because its time ran out.</summary>
public sealed record TestProfile(string Id, string NameKey, string NoteKey, IReadOnlyList<ProfileTest> Tests);

public static class TestProfiles
{
    private static IReadOnlyDictionary<string, string> Preset(string name) => new Dictionary<string, string> { ["preset"] = name };

    public static IReadOnlyList<TestProfile> All { get; } =
    [
        // The owner's choice: two minutes a test, one for the drive, and the 3D garden for one whole walk (162 s). On 64 GB of RAM two minutes may
        // not reach every pattern, and the RAM test then says Inconclusive rather than passing.
        new("quick", "Profile_Quick", "Profile_Quick_Note",
            [new("cpu.stress", 120), new("memory.pattern", 120), new("gpu.steady", 120), new("gpu.vram", 120), new("gpu.thermal", 120), new("gpu.scene.d3d", 162), new("power.combined", 120),
             new("storage.sequential", 60), new("storage.smart", 5)]),
        new("standard", "Profile_Standard", "Profile_Standard_Note",
            [new("cpu.stress", 300), new("cpu.vector", 300), new("cpu.linpack", 300), new("cpu.singlecore", 600), new("memory.pattern", 900), new("memory.sequence", 600),
             new("gpu.steady", 300), new("gpu.vram", 300), new("gpu.scene.d3d", 120), new("storage.sequential", 60), new("storage.random4k", 60),
             new("network.latency", 30), new("network.speed", 30), new("power.combined", 300), new("storage.smart", 5)]),
        new("transient", "Profile_Transient", "Profile_Transient_Note",
            [new("cpu.singlecore", 1200), new("cpu.vector", 300), new("gpu.variable", 600), new("gpu.pulse", 600), new("power.combined", 600)]),
        // A working day's changes of state instead of full load: for the machine that passes every stress test and still crashes at the desk.
        // The cycles run their "transient" preset (the load that keeps changing) and the RAM its "spike" one (full speed broken by pauses); the hour is not passed.
        new("everyday", "Profile_Everyday", "Profile_Everyday_Note",
            [new("system.daily", 1380), new("storage.daily", 600), new("cpu.cycle", 540, Preset("transient")), new("gpu.cycle", 540, Preset("transient")),
             new("memory.sequence", 300, Preset("spike")), new("storage.smart", 5)]),
        new("osstorage", "Profile_OsStorage", "Profile_OsStorage_Note",
            [new("storage.sequential", 120), new("storage.random4k", 120), new("storage.smart", 5), new("windows.dism", 120), new("windows.sfc", 240)]),
    ];
}
