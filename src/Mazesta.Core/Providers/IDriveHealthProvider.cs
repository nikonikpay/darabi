namespace Mazesta.Core.Providers;

/// <summary>One physical drive's health as Windows reads it from SMART: its verdict (Healthy, Warning, Unhealthy, Unknown) and its reliability
/// counters. Anything the drive or Windows does not report is null, never 0 - a new SSD's wear of 0 % is a reading, an HDD's missing one is not.</summary>
public sealed record DriveHealth(string Name, string? Serial, string? Status, int? WearPercent, double? TemperatureC, double? TemperatureMaxC,
    long? ReadErrorsUncorrected, long? WriteErrorsUncorrected, long? PowerOnHours, string? BusType = null, string? Letters = null, long? SizeBytes = null, string? Media = null)
{
    /// <summary>The drive as a person tells it apart from the others, "Samsung SSD 980 PRO 1TB (NVMe SSD, 1 TB, C: D:)": a model name alone says little
    /// when two drives are alike, and nothing to someone who does not know their drives' models. Only what Windows reported is in it.</summary>
    public string Label
    {
        get
        {
            string? kind = BusType == "NVMe" ? "NVMe SSD" : Media == "SSD" ? (BusType is { } b ? b + " SSD" : "SSD") : Media == "HDD" ? "HDD" : BusType;
            string? size = SizeBytes is > 0 and var n ? (n >= 1_000_000_000_000 ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{n / 1e12:0.#} TB") : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{n / 1e9:0} GB")) : null;
            var facts = new[] { kind, size, Letters }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            return facts.Count == 0 ? Name : $"{Name} ({string.Join(", ", facts)})";
        }
    }
}

public interface IDriveHealthProvider { IReadOnlyList<DriveHealth> Read(); }
