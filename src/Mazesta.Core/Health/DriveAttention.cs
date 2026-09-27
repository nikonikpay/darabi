using Mazesta.Core.Providers;
namespace Mazesta.Core.Health;

/// <summary>When a drive needs attention: Windows' own health verdict (from the drive's SMART / NVMe critical warnings) is not healthy, or the drive
/// counted uncorrected read or write errors. One rule for the SMART test and the tray's periodic health check, so they never disagree.</summary>
public static class DriveAttention
{
    public static bool Needs(DriveHealth d) => d.Status is "Warning" or "Unhealthy" || d.ReadErrorsUncorrected > 0 || d.WriteErrorsUncorrected > 0;
}
