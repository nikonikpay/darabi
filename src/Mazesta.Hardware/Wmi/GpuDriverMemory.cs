using Microsoft.Win32;
namespace Mazesta.Hardware.Wmi;

/// <summary>
/// The dedicated memory each display driver reports for its adapter (HardwareInformation.qwMemorySize, 64-bit), keyed by the driver's
/// MatchingDeviceId ("pci\ven_10de&amp;dev_2204"). Win32_VideoController.AdapterRAM cannot hold 4 GB or more; this can.
/// </summary>
internal static class GpuDriverMemory
{
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static Func<string?, long?> Read()
    {
        var sizes = new List<(string Match, long Bytes)>();
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(DisplayClass);
            foreach (var name in cls?.GetSubKeyNames() ?? [])
            {
                using var key = cls!.OpenSubKey(name);
                if (key?.GetValue("MatchingDeviceId") is string match && key.GetValue("HardwareInformation.qwMemorySize") is long bytes && bytes > 0) sizes.Add((match, bytes));
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return pnp => Match(sizes, pnp);
    }

    /// <summary>The longest MatchingDeviceId the PNP id starts with (a subsystem-specific match beats the bare vendor/device one).</summary>
    internal static long? Match(IReadOnlyList<(string Match, long Bytes)> sizes, string? pnp)
    {
        if (pnp is null) return null;
        var best = sizes.Where(s => pnp.StartsWith(s.Match, StringComparison.OrdinalIgnoreCase)).OrderByDescending(s => s.Match.Length).FirstOrDefault();
        return best.Match is null ? null : best.Bytes;
    }
}
