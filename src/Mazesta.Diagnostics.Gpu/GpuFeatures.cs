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

    /// <summary>For the programs page: whether the card named runs Direct3D 12 in hardware and traces rays in hardware (DXR), as its own device
    /// says. The adapter is the one of that name, else the only one there is; several and none of that name, or a device that can not be asked,
    /// is "unknown" (null), never another card's answer; no hardware adapter at all is "no".</summary>
    public static (bool? Dx12, bool? Dxr) Describe(string? gpuName)
    {
        try
        {
            var all = GraphicsDevice.EnumerateDevices().Where(d => d.IsHardwareAccelerated).ToList();
            if (all.Count == 0) return (false, false);
            // The card named, or the only card there is; with several and none of that name, which one answered is not known, so neither is the answer.
            var d = all.FirstOrDefault(x => gpuName is not null && (x.Name.Contains(gpuName.Trim(), StringComparison.OrdinalIgnoreCase) || gpuName.Contains(x.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                ?? (all.Count == 1 ? all[0] : null);
            return d is null ? (null, null) : (true, RayTracingTier(d) >= RaytracingTier.Tier1_0);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return (null, null); }
    }

    /// <summary>The availability answer shared by every ray-tracing test and benchmark: no GPU at all, or a GPU without DXR 1.1.</summary>
    public static Unavailability? RayTracingAvailability(TestOptions options)
    {
        if (GpuDevices.Resolve(options.Get(GpuDevices.OptionKey)) is not { } device) return new("Test_Unavailable_NoGpu", GpuDevices.NoGpu);
        return SupportsInlineRayTracing(device) ? null
            : new("Test_Unavailable_NoRayTracing", $"{device.Name} reports ray-tracing tier {RayTracingTier(device)}; DXR 1.1 (inline ray tracing) is required.");
    }

    /// <summary>
    /// What Windows lets this process keep resident on the card's own memory right now: DXGI's local-memory budget less what the process already
    /// uses (IDXGIAdapter3::QueryVideoMemoryInfo, for the adapter the device was opened on). Beyond the budget the OS starts moving allocations
    /// out to system RAM, and a VRAM test would then be checking the wrong memory. Null when Windows does not say.
    /// </summary>
    public static unsafe long? ResidentBudgetBytes(GraphicsDevice device)
    {
        try
        {
            Guid iid = typeof(ID3D12Device).GUID; void* pointer;
            InteropServices.GetID3D12Device(device, &iid, &pointer);
            using var d3d = new ID3D12Device((nint)pointer);
            using var factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<Vortice.DXGI.IDXGIFactory4>();
            long luid = d3d.AdapterLuid;   // a LUID is its low and high parts, the same 8 bytes
            using var adapter = factory.EnumAdapterByLuid<Vortice.DXGI.IDXGIAdapter3>(System.Runtime.CompilerServices.Unsafe.As<long, Vortice.Luid>(ref luid));
            var info = adapter.QueryVideoMemoryInfo(0, Vortice.DXGI.MemorySegmentGroup.Local);
            return info.Budget > info.CurrentUsage ? (long)(info.Budget - info.CurrentUsage) : 0;
        }
        catch (Exception e) when (e is SharpGen.Runtime.SharpGenException or InvalidCastException or EntryPointNotFoundException) { return null; }
    }

    public static Unavailability? GpuAvailability(TestOptions options)
        => GpuDevices.Resolve(options.Get(GpuDevices.OptionKey)) is null ? new("Test_Unavailable_NoGpu", GpuDevices.NoGpu) : null;
}
