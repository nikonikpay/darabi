using System.Collections.Concurrent; using ComputeSharp; using ComputeSharp.Interop; using Vortice.Direct3D12;
namespace Mazesta.Diagnostics.Gpu;

/// <summary>What a GPU can do, asked of its own Direct3D 12 device (the one ComputeSharp opened) and remembered per adapter: the pages ask
/// again whenever an option changes, and the answer is a fact of the card and driver, not of the moment.</summary>
public static class GpuFeatures
{
    private static readonly ConcurrentDictionary<string, RaytracingTier> Tiers = new();

    public static unsafe RaytracingTier RayTracingTier(GraphicsDevice device) => Tiers.GetOrAdd($"{device.Name}|{device.Luid}", _ =>
    {
        Guid iid = typeof(ID3D12Device5).GUID; void* pointer;
        InteropServices.GetID3D12Device(device, &iid, &pointer);
        using var d3d = new ID3D12Device5((nint)pointer);
        return d3d.CheckFeatureSupport<FeatureDataD3D12Options5>(Feature.Options5).RaytracingTier;
    });

    public static bool SupportsInlineRayTracing(GraphicsDevice device) => RayTracingTier(device) >= RaytracingTier.Tier1_1;

    /// <summary>The availability answer shared by every ray-tracing test and benchmark: no GPU at all, or a GPU without DXR 1.1.</summary>
    public static Unavailability? RayTracingAvailability(TestOptions options)
    {
        if (GpuDevices.Resolve(options.Get(GpuDevices.OptionKey)) is not { } device) return new("Test_Unavailable_NoGpu", GpuDevices.NoGpu);
        return SupportsInlineRayTracing(device) ? null
            : new("Test_Unavailable_NoRayTracing", $"{device.Name} reports ray-tracing tier {RayTracingTier(device)}; DXR 1.1 (inline ray tracing) is required.");
    }

    public static Unavailability? GpuAvailability(TestOptions options)
        => GpuDevices.Resolve(options.Get(GpuDevices.OptionKey)) is null ? new("Test_Unavailable_NoGpu", GpuDevices.NoGpu) : null;
}
