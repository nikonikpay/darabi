using Mazesta.Core.Tuning;
namespace Mazesta.Persistence;

public enum GpuProfileKind { Manual, Undervolt, Overclock }

/// <summary>A saved set of GPU settings. <see cref="GpuId"/> ties it to the card it was made on: the app is portable and travels between customers'
/// machines, and a profile found stable on one card says nothing about another - even of the same model. The measurements are the evidence an
/// automatic profile was kept on; a manual profile has none.</summary>
public sealed record GpuProfile(string Name, GpuProfileKind Kind, string GpuId, string GpuName, GpuTuningSettings Settings, DateTimeOffset CreatedAt,
    LoadMeasurement? Baseline = null, LoadMeasurement? Tuned = null);

/// <summary>Written just before the automatic search applies a setting and cleared once the run is over. Found on the next start it means that run
/// never came back - the machine froze, rebooted or the app was killed - so the setting it names is not trusted and the card is put back to stock.</summary>
public sealed record GpuTuningJournal(string GpuId, GpuTuningSettings Settings, DateTimeOffset StartedAt);

public sealed class GpuProfileDocument : IVersionedDocument
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<GpuProfile> Profiles { get; set; } = [];
    public GpuTuningJournal? Journal { get; set; }
}
