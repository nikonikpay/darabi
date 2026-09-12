using LibreHardwareMonitor.Hardware;
namespace Mazesta.Hardware.Tests.Fakes;
public sealed class FakeSensor(IHardware hardware, string name, SensorType type, int index, float? value, bool hidden = false) : ISensor
{
    public IControl Control => null!;
    public IHardware Hardware { get; } = hardware;
    public Identifier Identifier { get; } = new(hardware.Identifier, type.ToString().ToLowerInvariant(), index.ToString());
    public int Index { get; } = index;
    public bool IsDefaultHidden { get; } = hidden;
    public float? Max => Value; public float? Min => Value;
    public string Name { get; set; } = name;
    public IReadOnlyList<IParameter> Parameters => [];
    public SensorType SensorType { get; } = type;
    public float? Value { get; set; } = value;
    public IEnumerable<SensorValue> Values => [];
    public TimeSpan ValuesTimeWindow { get; set; }
    public void ResetMin() { } public void ResetMax() { } public void ClearValues() { }
    public void Accept(IVisitor visitor) { } public void Traverse(IVisitor visitor) { }
}
