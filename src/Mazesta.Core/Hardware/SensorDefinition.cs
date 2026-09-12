namespace Mazesta.Core.Hardware;

public sealed record SensorDefinition(SensorId Id, HardwareId Hardware, string Name, SensorKind Kind, Unit Unit, SensorRole Role, int Ordinal);
