namespace Mazesta.Core.Providers;

/// <summary>One physical drive's health as Windows reads it from SMART: its verdict (Healthy, Warning, Unhealthy, Unknown) and its reliability
/// counters. Anything the drive or Windows does not report is null, never 0 - a new SSD's wear of 0 % is a reading, an HDD's missing one is not.</summary>
public sealed record DriveHealth(string Name, string? Serial, string? Status, int? WearPercent, double? TemperatureC, double? TemperatureMaxC,
    long? ReadErrorsUncorrected, long? WriteErrorsUncorrected, long? PowerOnHours, string? BusType = null);

public interface IDriveHealthProvider { IReadOnlyList<DriveHealth> Read(); }
