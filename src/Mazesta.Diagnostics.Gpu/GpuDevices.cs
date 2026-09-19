using ComputeSharp;
namespace Mazesta.Diagnostics.Gpu;

/// <summary>The DirectX 12 adapters the GPU tests can run on. Software adapters (WARP) are never offered: a
/// stress test that quietly ran on the CPU would report a healthy GPU it never touched.</summary>
public static class GpuDevices
{
    public const string OptionKey = "gpu";
    private static readonly Lazy<IReadOnlyList<GraphicsDevice>> Adapters = new(() => GraphicsDevice.EnumerateDevices().Where(d => d.IsHardwareAccelerated).ToList());

    public static TestOption Option { get; } = new(OptionKey, "Test_Option_Gpu", TestOptionKind.Choice, "", Choices);

    public static IReadOnlyList<OptionChoice> Choices()
        => Adapters.Value.OrderByDescending(d => d.DedicatedMemorySize).Select(d => new OptionChoice(KeyOf(d), $"{d.Name} ({d.DedicatedMemorySize >> 30} GB)")).ToList();

    /// <summary>The chosen adapter; with no choice, the one with the most dedicated memory (the discrete GPU on a machine that also has an iGPU). Null when there is none.</summary>
    public static GraphicsDevice? Resolve(string key)
        => key.Length == 0 ? Adapters.Value.OrderByDescending(d => d.DedicatedMemorySize).FirstOrDefault() : Adapters.Value.FirstOrDefault(d => KeyOf(d) == key);

    private static string KeyOf(GraphicsDevice d) => $"{d.Name}|{d.Luid}";
}
