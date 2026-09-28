using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization;
namespace Mazesta.Desktop.ViewModels;

/// <summary>The sensor provider's state in words, for the status band and the banner.</summary>
public static class ProviderText
{
    /// <summary>The official PawnIO page, offered in the banner when the driver is missing.</summary>
    public const string PawnIoUrl = "https://pawnio.eu/";
    /// <summary>Mirrors LibreHardwareMonitorProvider.ReasonPawnIoMissing; this layer knows the key, not the provider type.</summary>
    public const string PawnIoMissing = "Provider.PawnIoMissing";

    public static string Describe(ProviderStatus s) => s.State switch
    {
        ProviderState.Ready => Loc.Format("Status_Provider_Ready", s.SensorCount),
        ProviderState.Degraded => Loc.Format("Status_Provider_Degraded", Loc.Get(s.ReasonKey ?? "")),
        ProviderState.Failed => Loc.Format("Status_Provider_Failed", Loc.Get(s.ReasonKey ?? "") + (s.Detail is null ? "" : $" ({s.Detail})")),
        _ => Loc.Get("Status_Provider_Starting")
    };
}
