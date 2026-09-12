namespace Mazesta.Core.Hardware;

public readonly record struct HardwareId(string Value)
{
    public static HardwareId FromProviderPath(HardwareKind kind, string providerPath)
    {
        var token = providerPath.Trim('/').Replace('/', '-');
        return new HardwareId($"{KindToken(kind)}/{token}");
    }
    public static HardwareId ForStorage(string serial)
        => new($"storage/{string.Join('_', serial.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))}");
    public static string KindToken(HardwareKind kind) => kind.ToString().ToLowerInvariant();
    public override string ToString() => Value;
}
