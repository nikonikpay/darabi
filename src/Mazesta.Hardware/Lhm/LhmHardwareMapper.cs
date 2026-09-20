using LibreHardwareMonitor.Hardware; using Mazesta.Core.Hardware;
namespace Mazesta.Hardware.Lhm;
internal sealed record MappedSensor(SensorDefinition Definition, ISensor Source);
internal sealed record MappedNode(HardwareNode Node, IHardware Source, IReadOnlyList<MappedSensor> Sensors);
internal sealed class LhmHardwareMapper(Func<IHardware, string?> storageSerialResolver)
{
    public IReadOnlyList<MappedNode> Map(IEnumerable<IHardware> roots)
    {
        var result = new List<MappedNode>();
        foreach (var root in roots) Visit(root, null, result);
        return result;
    }
    private void Visit(IHardware hw, HardwareId? parentId, List<MappedNode> into)
    {
        if (hw.HardwareType == HardwareType.Network && NetworkAdapterFilter.IsVirtualBinding(hw.Name)) return;
        var kind = KindOf(hw.HardwareType);
        string path = hw.Identifier.ToString();
        string? serial = kind == HardwareKind.Storage ? Normalize(storageSerialResolver(hw)) : null;
        var id = serial is not null ? HardwareId.ForStorage(serial) : HardwareId.FromProviderPath(kind, path);
        var sensors = new List<MappedSensor>();
        int ordinal = 0; var seen = new HashSet<SensorId>();
        foreach (var s in hw.Sensors.Where(s => !s.IsDefaultHidden).OrderBy(s => s.SensorType).ThenBy(s => s.Index))
        {
            var sensorKind = SensorKindOf(s.SensorType);
            string sensorPath = s.Identifier.ToString()[path.Length..];   // "/temperature/1"
            string name = SensorNameCatalog.Resolve(hw, s);
            var sensorId = SensorId.Create(id, sensorPath);
            if (!seen.Add(sensorId)) continue;   // a driver can report the same identifier twice; ids key every dictionary downstream, so the first one wins
            var def = new SensorDefinition(sensorId, id, name, sensorKind, Units.ForKind(sensorKind),
                SensorRoleMap.Resolve(hw.HardwareType, s.SensorType, name, path), ordinal++);
            sensors.Add(new MappedSensor(def, s));
        }
        var node = new HardwareNode(id, kind, VendorOf(hw.HardwareType, path), hw.Name, parentId, serial is not null || kind != HardwareKind.Storage, sensors.Select(m => m.Definition).ToList());
        into.Add(new MappedNode(node, hw, sensors));
        foreach (var sub in hw.SubHardware) Visit(sub, id, into);
    }
    private static string? Normalize(string? serial) => string.IsNullOrWhiteSpace(serial) ? null : serial.Trim();
    public static HardwareKind KindOf(HardwareType t) => t switch
    {
        HardwareType.Cpu => HardwareKind.Cpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => HardwareKind.Gpu,
        HardwareType.Memory => HardwareKind.Memory,
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => HardwareKind.Motherboard,
        HardwareType.Storage => HardwareKind.Storage, HardwareType.Network => HardwareKind.Network,
        HardwareType.Psu => HardwareKind.Psu, HardwareType.Cooler => HardwareKind.Cooler, _ => HardwareKind.Other
    };
    public static HardwareVendor VendorOf(HardwareType t, string identifier) => t switch
    {
        HardwareType.GpuNvidia => HardwareVendor.Nvidia, HardwareType.GpuAmd => HardwareVendor.Amd, HardwareType.GpuIntel => HardwareVendor.Intel,
        HardwareType.Cpu when identifier.StartsWith("/intelcpu", StringComparison.Ordinal) => HardwareVendor.Intel,
        HardwareType.Cpu when identifier.StartsWith("/amdcpu", StringComparison.Ordinal) => HardwareVendor.Amd,
        _ => HardwareVendor.Unknown
    };
    public static SensorKind SensorKindOf(SensorType t) => Enum.TryParse<SensorKind>(t.ToString(), out var k) ? k : t == SensorType.TimeSpan ? SensorKind.Timespan : SensorKind.Unknown;
}
