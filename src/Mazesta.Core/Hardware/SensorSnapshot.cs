namespace Mazesta.Core.Hardware;

public sealed record SensorSnapshot(long Sequence, DateTimeOffset Timestamp, IReadOnlyList<SensorReading> Readings, IReadOnlyDictionary<HardwareId, NodeStatus> NodeStatus);
