namespace Mazesta.Core.Hardware;

public readonly record struct SensorId(string Value)
{
    public static SensorId Create(HardwareId hardware, string providerSensorPath) => new($"{hardware.Value}#{providerSensorPath.Trim('/')}");
    public HardwareId Hardware => new(Value[..Value.IndexOf('#')]);
    public override string ToString() => Value;
}
