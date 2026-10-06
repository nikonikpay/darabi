namespace Mazesta.Diagnostics;

/// <summary>What a test that broke most likely points at. The page words each kind (<c>Advice_{kind}_*</c>); a kind is only ever a likelihood, never a verdict on the part.</summary>
public enum FailureKind { GpuDriver, Gpu, Memory, Storage, Network, Cpu, Windows, Unknown }

public static class FailureAdvice
{
    /// <summary>Picks the likely cause from the test's id (its part) and the error text the test or the exception left. A Windows or driver message beats the part, since
    /// the same "device removed" arrives from whichever test was drawing when the driver reset.</summary>
    public static FailureKind Classify(string testId, string? detail)
    {
        string d = detail ?? "";
        bool Has(params string[] words) => words.Any(w => d.Contains(w, StringComparison.OrdinalIgnoreCase));
        string part = testId.Split('.') is { Length: > 1 } p ? (p[0] == "bench" ? p[1] : p[0]) : testId;
        if (Has("device instance has been suspended", "GetDeviceRemovedReason", "DXGI_ERROR_DEVICE", "device removed", "device lost", "No DirectX 12 hardware GPU", "driver was reset", "0x887A0005", "0x887A0006", "0x887A0007", "0x887A0020", "TDR"))
            return FailureKind.GpuDriver;
        if (part is "gpu" or "power" && Has("DirectX", "D3D", "DXGI", "Direct3D", "shader", "VRAM", "adapter")) return FailureKind.Gpu;
        return part switch
        {
            "gpu" => FailureKind.Gpu, "memory" => FailureKind.Memory, "storage" => FailureKind.Storage, "network" => FailureKind.Network, "cpu" => FailureKind.Cpu, "windows" => FailureKind.Windows,
            _ => Has("out of memory", "could not allocate") ? FailureKind.Memory : FailureKind.Unknown,
        };
    }
}
