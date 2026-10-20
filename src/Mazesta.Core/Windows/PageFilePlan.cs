namespace Mazesta.Core.Windows;

public enum PageFileMode { SystemManaged, Custom, None }

/// <summary>
/// A virtual-memory setting to apply: Windows manages it on every drive, a size on one drive (0/0 there means "Windows picks the size on this
/// drive"), or none. Checked against the limits Windows' own dialog enforces before anything is written; it takes effect after a restart.
/// </summary>
public sealed record PageFilePlan(PageFileMode Mode, string? Drive = null, long InitialMb = 0, long MaximumMb = 0)
{
    public const long MinimumMb = 16;

    /// <summary>Why the plan cannot be applied (a Strings key), or null. The maximum is not capped: Windows' dialog allows three times the memory, but some programs need more and the registry takes it;
    /// the initial size has to fit on the drive.</summary>
    public string? Problem(long ramMb, long? driveFreeMb)
    {
        if (Mode != PageFileMode.Custom) return null;
        if (string.IsNullOrWhiteSpace(Drive)) return "Tools_Vm_Error_Drive";
        if (InitialMb == 0 && MaximumMb == 0) return null;
        if (InitialMb < MinimumMb) return "Tools_Vm_Error_Initial";
        if (MaximumMb < InitialMb) return "Tools_Vm_Error_Order";
        if (driveFreeMb is { } free && InitialMb > free) return "Tools_Vm_Error_Space";
        return null;
    }
}
