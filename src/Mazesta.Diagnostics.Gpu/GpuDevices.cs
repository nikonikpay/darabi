using ComputeSharp; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Monitoring;
namespace Mazesta.Diagnostics.Gpu;

/// <summary>The DirectX 12 adapters the GPU tests can run on. Software adapters (WARP) are never offered: a
/// stress test that quietly ran on the CPU would report a healthy GPU it never touched.</summary>
public static class GpuDevices
{
    public const string OptionKey = "gpu";
    private static readonly object Gate = new(); private static IReadOnlyList<GraphicsDevice>? s_adapters; private static bool s_stale;
    /// <summary>The adapters, opened once. A device that was lost (driver reset, hang) can never be used again in this process, so a run that ends that way,
    /// or the device's own lost event, marks the list stale and the next call opens fresh ones: otherwise every later run would fail the same way until the app was restarted.</summary>
    private static IReadOnlyList<GraphicsDevice> Adapters
    {
        get
        {
            lock (Gate)
            {
                if (s_adapters is null || s_stale)
                {
                    var list = GraphicsDevice.EnumerateDevices().Where(d => d.IsHardwareAccelerated).ToList();
                    foreach (var d in list) d.DeviceLost += (_, _) => Invalidate();
                    s_adapters = list; s_stale = list.Count == 0;   // an empty answer (the driver is still coming back from a reset) is not kept: the next call asks again
                }
                return s_adapters;
            }
        }
    }
    /// <summary>Says the adapters in use are no longer to be trusted (one was lost): the next call that needs them opens them again.</summary>
    internal static void Invalidate() { lock (Gate) s_stale = true; }

    public static TestOption Option { get; } = new(OptionKey, "Test_Option_Gpu", TestOptionKind.Choice, "", Choices);

    public static IReadOnlyList<OptionChoice> Choices()
        => Adapters.OrderByDescending(d => d.DedicatedMemorySize).Select(d => new OptionChoice(KeyOf(d), $"{d.Name} ({d.DedicatedMemorySize >> 30} GB)")).ToList();

    /// <summary>The cards that could take part in a test: the hardware adapters with memory of their own, so neither the processor's integrated graphics nor a software adapter. One of them runs a test.</summary>
    public static int Discrete => Adapters.Count(d => d.DedicatedMemorySize >= 512L << 20);

    public const string NoGpu = "No DirectX 12 hardware GPU is available.";

    /// <summary>The adapter a test or benchmark request chose through <see cref="Option"/>.</summary>
    public static GraphicsDevice? Resolve(TestExecutionRequest request, TestDefinition definition) => Resolve((request.Options ?? TestOptions.None(definition)).Get(OptionKey));

    /// <summary>The chosen adapter; with no choice, the one with the most dedicated memory (the discrete GPU on a machine that also has an iGPU). Null when there is none.</summary>
    public static GraphicsDevice? Resolve(string key)
    {
        var all = Adapters;
        if (key.Length == 0) return all.OrderByDescending(d => d.DedicatedMemorySize).FirstOrDefault();
        // A card's LUID changes when the driver resets it, so a choice made before the reset names the same card by its name only.
        string name = key.Split('|')[0];
        return all.FirstOrDefault(d => KeyOf(d) == key) ?? all.Where(d => d.Name == name).OrderByDescending(d => d.DedicatedMemorySize).FirstOrDefault();
    }

    private static string KeyOf(GraphicsDevice d) => $"{d.Name}|{d.Luid}";

    /// <summary>
    /// Which of the monitor's GPU nodes (and the nodes under it) is the adapter a run used, for reading that card's own sensors. The only GPU
    /// the monitor sees, or the one whose name matches the adapter's; with several GPUs and no match nothing is chosen, so a run on one card
    /// is never credited with another card's temperature or free memory. The sensor monitor and DirectX do not share a device identity (LUID),
    /// so the name is what ties them; two identical cards stay ambiguous and neither is read.
    /// </summary>
    public static Func<HardwareNode, bool> SensorNode(PollingEngine? engine, string adapter)
    {
        var gpus = engine?.Hardware.Where(n => n.Kind == HardwareKind.Gpu && n.ParentId is null).ToList() ?? [];
        if (gpus.Count == 1) { var only = gpus[0].Id; return n => n.Id == only || n.ParentId == only; }
        string want = BenchmarkPeers.PartName(adapter);
        // Two cards of one name can not be told apart by it: neither is chosen, rather than the first standing in for the one that ran.
        var same = gpus.Where(n => string.Equals(BenchmarkPeers.PartName(n.Name), want, StringComparison.OrdinalIgnoreCase)).ToList();
        HardwareId? match = same.Count == 1 ? same[0].Id : null;
        return n => match is { } id && (n.Id == id || n.ParentId == id);
    }
}
