using Mazesta.Core.Providers;
namespace Mazesta.Core.Health;

/// <summary>When a drive needs attention: Windows' own health verdict (from the drive's SMART / NVMe critical warnings) is not healthy, or an NVMe
/// drive counted media errors. One rule for the SMART test and the tray's periodic health check, so they never disagree. A SATA drive's
/// "uncorrected read errors" are not judged: Windows takes them from a vendor-specific SMART attribute that some drives fill with other counts
/// (a healthy Plextor M7V reads 46), so they are reported as evidence only; NVMe's error count is defined by the standard.</summary>
public static class DriveAttention
{
    public static bool Needs(DriveHealth d) => d.Status is "Warning" or "Unhealthy" || (d.BusType == "NVMe" && (d.ReadErrorsUncorrected > 0 || d.WriteErrorsUncorrected > 0));

    /// <summary>Life left, from the drive's own wear counter (100 − used); null for a drive without one (an HDD), never guessed.</summary>
    public static int? HealthPercent(int? wearPercent) => wearPercent is { } w ? Math.Clamp(100 - w, 0, 100) : null;
}
