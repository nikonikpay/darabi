using Mazesta.Core.Tuning; using Mazesta.Hardware.Wmi; using Microsoft.Extensions.Logging;
namespace Mazesta.Hardware.Amd;

/// <summary>
/// Recognises an AMD Radeon and its Adrenalin driver and, when Adrenalin's ADLX opens the card for manual tuning (see <see cref="AmdTuningDevice"/>),
/// lists it as a tunable device. When it cannot (no Adrenalin, a card or driver without manual tuning) the page says why, naming the card.
/// </summary>
public sealed class AmdTuningProvider(IWmiQuery wmi, Func<bool>? adrenalinPresent = null, Func<(IReadOnlyList<IGpuTuningDevice> Devices, string? Detail)>? open = null, ILogger? log = null) : IGpuTuningProvider
{
    private readonly Lazy<(string? Reason, string? Detail, IReadOnlyList<IGpuTuningDevice> Devices, bool Any)> _state = new(() => Describe(Cards(wmi), (adrenalinPresent ?? AdrenalinInstalled)(), open ?? (() => AdlxTuningDevices.Open(log))));
    public IReadOnlyList<IGpuTuningDevice> Devices => _state.Value.Devices;
    public string? UnavailableReasonKey => _state.Value.Reason;
    public string? UnavailableDetail => _state.Value.Detail;
    /// <summary>True when an AMD card is in the PC, whatever its driver.</summary>
    public bool HasAmd => _state.Value.Any;

    public sealed record Card(string Name, string Driver, bool AmdDriver);

    /// <summary>The AMD cards from Win32_VideoController: by PCI vendor 1002, else by the maker the driver names.</summary>
    internal static IReadOnlyList<Card> Cards(IWmiQuery wmi)
    {
        try
        {
            return [.. wmi.Query(@"root\cimv2", "SELECT Name,AdapterCompatibility,DriverVersion,PNPDeviceID FROM Win32_VideoController")
                .Where(r => Text(r, "PNPDeviceID").Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) || Text(r, "AdapterCompatibility").Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase))
                .Select(r => new Card(Text(r, "Name"), Text(r, "DriverVersion"), Text(r, "AdapterCompatibility").Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase)))];
        }
        catch (Exception e) when (e is System.Management.ManagementException or InvalidOperationException or UnauthorizedAccessException) { return []; }
    }

    private static string Text(IReadOnlyDictionary<string, object?> r, string key) => r.TryGetValue(key, out var v) ? v?.ToString()?.Trim() ?? "" : "";

    /// <summary>Adrenalin puts AMD's control libraries in System32; Windows' own display driver does not.</summary>
    internal static bool AdrenalinInstalled()
    {
        string sys = Environment.SystemDirectory;
        return File.Exists(Path.Combine(sys, "amdadlx64.dll")) || File.Exists(Path.Combine(sys, "atiadlxx.dll"));
    }

    internal static (string? Reason, string? Detail, IReadOnlyList<IGpuTuningDevice> Devices, bool Any) Describe(IReadOnlyList<Card> cards, bool adrenalin, Func<(IReadOnlyList<IGpuTuningDevice> Devices, string? Detail)> open)
    {
        if (cards.Count == 0) return (null, null, [], false);
        string names = string.Join(", ", cards.Select(c => c.Driver.Length > 0 ? $"{c.Name}, {c.Driver}" : c.Name));
        if (!adrenalin || !cards.Any(c => c.AmdDriver)) return ("Tuning_Unavailable_AmdNoDriver", names, [], true);
        var (devices, why) = open();
        return devices.Count > 0 ? (null, null, devices, true) : ("Tuning_Unavailable_Amd", why is null ? names : $"{names}; {why}", [], true);
    }
}

/// <summary>Every NVIDIA and AMD card that can be tuned; when there is none, the reason that fits: an AMD card named, or the NVIDIA message when there is neither.</summary>
public sealed class CombinedTuningProvider(IGpuTuningProvider nvidia, AmdTuningProvider amd) : IGpuTuningProvider
{
    public IReadOnlyList<IGpuTuningDevice> Devices => [.. nvidia.Devices, .. amd.Devices];
    public string? UnavailableReasonKey => Devices.Count > 0 || !amd.HasAmd ? nvidia.UnavailableReasonKey : amd.UnavailableReasonKey;
    public string? UnavailableDetail => Devices.Count > 0 || !amd.HasAmd ? nvidia.UnavailableDetail : amd.UnavailableDetail;
}
