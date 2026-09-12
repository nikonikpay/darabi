namespace Mazesta.Core.Hardware;

public sealed record HardwareNode(HardwareId Id, HardwareKind Kind, HardwareVendor Vendor, string Name, HardwareId? ParentId, bool IdIsStable, IReadOnlyList<SensorDefinition> Sensors);
