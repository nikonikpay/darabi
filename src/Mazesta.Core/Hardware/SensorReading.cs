namespace Mazesta.Core.Hardware;

public readonly record struct SensorReading(SensorId Id, double? Value, DateTimeOffset Timestamp, DataQuality Quality, string Source);
