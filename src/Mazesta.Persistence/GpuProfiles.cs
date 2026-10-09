using Mazesta.Core.Tuning;
namespace Mazesta.Persistence;

public enum GpuProfileKind { Manual, Undervolt, Overclock, OverclockPlus }

/// <summary>A saved set of GPU settings. <see cref="GpuId"/> ties it to the card it was made on: the app is portable and travels between customers'
/// machines, and a profile found stable on one card says nothing about another - even of the same model. The measurements are the evidence an
/// automatic profile was kept on; a manual profile has none. <see cref="LoadVersion"/> is the scoring load the measurements came from (null: saved
/// before it was recorded, i.e. version 1), so a later search never compares its score with one from a different load.</summary>
public sealed record GpuProfile(string Name, GpuProfileKind Kind, string GpuId, string GpuName, GpuTuningSettings Settings, DateTimeOffset CreatedAt,
    LoadMeasurement? Baseline = null, LoadMeasurement? Tuned = null, int? LoadVersion = null);

/// <summary>Written just before the automatic search applies a setting and cleared once the run is over. Found on the next start it means that run
/// never came back - the machine froze, rebooted or the app was killed - so the setting it names is not trusted and the card is put back to stock.</summary>
public sealed record GpuTuningJournal(string GpuId, GpuTuningSettings Settings, DateTimeOffset StartedAt);

/// <summary>A card's stock voltage/frequency curve as the curve scan measured it, kept so the editor does not need a new scan every visit.</summary>
public sealed record GpuCurve(string GpuId, DateTimeOffset MeasuredAt, IReadOnlyList<VfPoint> Points);

public sealed class GpuProfileDocument : IVersionedDocument
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<GpuProfile> Profiles { get; set; } = [];
    public GpuTuningJournal? Journal { get; set; }
    /// <summary>The newest measured curve of each card (absent in documents saved before curves existed).</summary>
    public List<GpuCurve> Curves { get; set; } = [];
}
