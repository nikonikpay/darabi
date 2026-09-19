using LibreHardwareMonitor.Hardware;
namespace Mazesta.Hardware.Tests.Fakes;
public sealed class FakeHardware(HardwareType type, string identifier, string name, IHardware? parent = null) : IHardware
{
    private readonly List<ISensor> _sensors = [];
    public HardwareType HardwareType { get; } = type;
    public Identifier Identifier { get; } = new(identifier.Trim('/').Split('/'));
    public string Name { get; set; } = name;
    public IHardware Parent { get; } = parent!;
    public ISensor[] Sensors => _sensors.ToArray();
    public IHardware[] SubHardware { get; set; } = [];
    public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
    public int UpdateCalls; public Exception? ThrowOnUpdate; public Action? OnUpdate;
    public event SensorEventHandler? SensorAdded; public event SensorEventHandler? SensorRemoved;
    public string GetReport() => "";
    public void Update() { UpdateCalls++; OnUpdate?.Invoke(); if (ThrowOnUpdate is not null) throw ThrowOnUpdate; }
    public void Accept(IVisitor visitor) { } public void Traverse(IVisitor visitor) { }
    public FakeSensor Add(string name, SensorType type, int index, float? value, bool hidden = false)
    { var s = new FakeSensor(this, name, type, index, value, hidden); _sensors.Add(s); SensorAdded?.Invoke(s); return s; }
    public void Remove(ISensor s) { _sensors.Remove(s); SensorRemoved?.Invoke(s); }
}
