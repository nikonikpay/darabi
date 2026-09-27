using System.Text.RegularExpressions; using LibreHardwareMonitor.Hardware; using Mazesta.Core.Hardware;
namespace Mazesta.Hardware.Lhm;
internal static partial class SensorRoleMap
{
    [GeneratedRegex(@"^(CPU Core|P-Core|E-Core) #\d+$")] private static partial Regex CoreName();
    [GeneratedRegex(@"^(CPU Core|P-Core|E-Core) #\d+( Thread #\d+)?$")] private static partial Regex ThreadLoadName();
    [GeneratedRegex(@"^Core #\d+$")] private static partial Regex AmdCoreClock();
    [GeneratedRegex(@"^Core #\d+ \(Effective\)$")] private static partial Regex AmdEffective();
    [GeneratedRegex(@"^CCD\d+ \(Tdie\)$")] private static partial Regex Ccd();
    [GeneratedRegex(@"^DIMM #\d+$")] private static partial Regex Dimm();
    [GeneratedRegex(@"^D3D Compute")] private static partial Regex D3DCompute();
    [GeneratedRegex(@"^Core #\d+ VID$")] private static partial Regex AmdVid();
    [GeneratedRegex(@"^Core #\d+ \(SMU\)$")] private static partial Regex AmdCorePower();
    [GeneratedRegex(@"^(Core|CPU Core|P-Core|E-Core) #\d+$")] private static partial Regex CoreFactor();
    [GeneratedRegex(@"^t[A-Z]")] private static partial Regex Timing();
    private static readonly HashSet<string> BoardVoltages = new(StringComparer.Ordinal)
    { "Vcore", "Vcore SoC", "+12V", "+5V", "+3.3V", "+3V Standby", "AVCC", "3VCC", "VBat", "DIMM", "CPU Termination", "CPU System Agent", "VTT", "VRM", "CPU Core", "CMOS Battery" };

    public static SensorRole Resolve(HardwareType hw, SensorType st, string name, string hardwareIdentifier)
    {
        switch (hw)
        {
            case HardwareType.Cpu: return Cpu(st, name);
            case HardwareType.GpuNvidia: case HardwareType.GpuAmd: case HardwareType.GpuIntel: return Gpu(hw, st, name);
            case HardwareType.Memory: return hardwareIdentifier.StartsWith("/vram", StringComparison.Ordinal) ? VirtualMemory(st, name) : Memory(st, name);
            case HardwareType.Motherboard: case HardwareType.SuperIO: case HardwareType.EmbeddedController: return Board(st, name);
            case HardwareType.Storage: return Storage(st, name);
            case HardwareType.Network: return Network(st, name);
            default: return SensorRole.None;
        }
    }
    private static SensorRole Cpu(SensorType st, string n) => st switch
    {
        SensorType.Temperature when n == "CPU Package" => SensorRole.CpuPackageTemp,
        SensorType.Temperature when n is "Core (Tctl/Tdie)" or "Core (Tdie)" or "Core (Tctl)" => SensorRole.CpuTctlTdie,
        SensorType.Temperature when Ccd().IsMatch(n) => SensorRole.CpuCcdTemp,
        SensorType.Temperature when CoreName().IsMatch(n) => SensorRole.CpuCoreTemp,
        SensorType.Temperature when n == "CCDs Max (Tdie)" => SensorRole.CpuCcdMaxTemp,
        SensorType.Temperature when n == "CCDs Average (Tdie)" => SensorRole.CpuCcdAverageTemp,
        SensorType.Clock when n == "Bus Speed" => SensorRole.CpuBusClock,
        SensorType.Clock when n == "Cores (Average Effective)" => SensorRole.CpuEffectiveClockAverage,
        SensorType.Clock when n == "Cores (Average)" => SensorRole.CpuCoreClockAverage,
        SensorType.Clock when AmdEffective().IsMatch(n) => SensorRole.CpuEffectiveClock,
        SensorType.Clock when CoreName().IsMatch(n) || AmdCoreClock().IsMatch(n) => SensorRole.CpuCoreClock,
        SensorType.Voltage when n is "CPU Core" or "Core (SVI2 TFN)" => SensorRole.CpuVcore,
        SensorType.Voltage when n is "SoC (SVI2 TFN)" or "SoC" => SensorRole.CpuSocVoltage,
        SensorType.Voltage when AmdVid().IsMatch(n) => SensorRole.CpuCoreVid,
        SensorType.Power when n is "CPU Package" or "Package" => SensorRole.CpuPackagePower,
        SensorType.Power when n == "CPU Cores" => SensorRole.CpuCorePower,
        SensorType.Power when AmdCorePower().IsMatch(n) => SensorRole.CpuPerCorePower,
        SensorType.Factor when CoreFactor().IsMatch(n) => SensorRole.CpuCoreMultiplier,
        SensorType.Load when n == "CPU Total" => SensorRole.CpuTotalLoad,
        SensorType.Load when ThreadLoadName().IsMatch(n) => SensorRole.CpuThreadLoad,
        SensorType.Load when n == "CPU Core Max" => SensorRole.CpuCoreMaxLoad,
        _ => SensorRole.None
    };
    private static SensorRole Gpu(HardwareType hw, SensorType st, string n) => st switch
    {
        SensorType.Temperature when n == "GPU Core" => SensorRole.GpuCoreTemp,
        SensorType.Temperature when n == "GPU Hot Spot" => SensorRole.GpuHotSpotTemp,
        SensorType.Temperature when n == "GPU Memory Junction" || (n == "GPU Memory" && hw != HardwareType.GpuNvidia) => SensorRole.GpuVramTemp,
        SensorType.Clock when n == "GPU Core" => SensorRole.GpuCoreClock,
        SensorType.Clock when n == "GPU Memory" => SensorRole.GpuMemoryClock,
        SensorType.Load when n == "GPU Core" => SensorRole.GpuLoad3D,
        SensorType.Load when n == "D3D 3D" => SensorRole.GpuLoadD3D3D,
        SensorType.Load when D3DCompute().IsMatch(n) => SensorRole.GpuLoadCompute,
        SensorType.Load when n is "GPU Video Engine" or "GPU Media" => SensorRole.GpuLoadVideo,
        SensorType.Load when n == "GPU Memory Controller" => SensorRole.GpuLoadMemoryController,
        SensorType.Load when n == "GPU Bus" => SensorRole.GpuLoadBus,
        SensorType.Load when n is "GPU Power" or "GPU Board Power" => SensorRole.GpuPowerPercent,
        SensorType.Load when n.StartsWith("D3D ", StringComparison.Ordinal) => SensorRole.GpuLoadEngine,
        SensorType.Power when n is "GPU Package" or "GPU Power" => SensorRole.GpuPower,
        SensorType.Voltage when n is "GPU Core Voltage" or "GPU Core" => SensorRole.GpuVoltage,
        SensorType.Fan => SensorRole.GpuFanRpm,
        SensorType.Control when n.StartsWith("GPU Fan", StringComparison.Ordinal) => SensorRole.GpuFanPercent,
        SensorType.SmallData when n == "GPU Memory Total" => SensorRole.GpuVramTotal,
        SensorType.SmallData when n == "GPU Memory Used" => SensorRole.GpuVramUsed,
        SensorType.SmallData when n == "GPU Memory Free" => SensorRole.GpuVramFree,
        SensorType.SmallData when n == "D3D Dedicated Memory Used" => SensorRole.GpuD3DMemoryDedicated,
        SensorType.SmallData when n == "D3D Shared Memory Used" => SensorRole.GpuD3DMemoryShared,
        SensorType.Throughput when n == "GPU PCIe Rx" => SensorRole.GpuPcieRx,
        SensorType.Throughput when n == "GPU PCIe Tx" => SensorRole.GpuPcieTx,
        _ => SensorRole.None
    };
    private static SensorRole Memory(SensorType st, string n) => st switch
    {
        SensorType.Load when n == "Memory" => SensorRole.RamLoad,
        SensorType.Data when n == "Memory Used" => SensorRole.RamUsed,
        SensorType.Data when n == "Memory Available" => SensorRole.RamFree,
        SensorType.Temperature when Dimm().IsMatch(n) => SensorRole.DimmTemp,
        // The other temperature rows of a DIMM are its sensor's resolution and programmed limits (from the SPD), not readings.
        SensorType.Temperature or SensorType.Data when n == "Capacity" || n.StartsWith("Thermal Sensor", StringComparison.Ordinal) || n.StartsWith("Temperature Sensor", StringComparison.Ordinal) => SensorRole.DimmSpec,
        SensorType.Timing when Timing().IsMatch(n) => SensorRole.DimmTiming,
        _ => SensorRole.None
    };
    /// <summary>The "Virtual Memory" node is RAM plus the page file (commit charge): kept apart from RAM so it is never read as RAM.</summary>
    private static SensorRole VirtualMemory(SensorType st, string n) => st switch
    {
        SensorType.Load when n == "Memory" => SensorRole.VirtualMemoryLoad,
        SensorType.Data when n == "Memory Used" => SensorRole.VirtualMemoryUsed,
        SensorType.Data when n == "Memory Available" => SensorRole.VirtualMemoryFree,
        _ => SensorRole.None
    };
    private static SensorRole Board(SensorType st, string n) => st switch
    {
        SensorType.Temperature when n is "Chipset" or "PCH" => SensorRole.ChipsetTemp,
        SensorType.Temperature => SensorRole.BoardTemp,
        SensorType.Fan when n.StartsWith("CPU Fan", StringComparison.Ordinal) => SensorRole.CpuFan,
        SensorType.Fan => SensorRole.BoardFan,
        SensorType.Voltage when BoardVoltages.Contains(n) => SensorRole.BoardVoltage,
        SensorType.Control => SensorRole.BoardFanControl,
        _ => SensorRole.None
    };
    private static SensorRole Storage(SensorType st, string n) => st switch
    {
        SensorType.Temperature when n is "Temperature" or "Composite Temperature" => SensorRole.StorageTemp,
        SensorType.Level when n == "Life" => SensorRole.StorageRemainingLife,
        SensorType.Factor when n == "Power On Hours" => SensorRole.StoragePowerOnHours,
        SensorType.Load when n == "Used Space" => SensorRole.StorageUsedSpace,
        SensorType.Throughput when n == "Read Rate" => SensorRole.StorageReadRate,
        SensorType.Throughput when n == "Write Rate" => SensorRole.StorageWriteRate,
        SensorType.Temperature when n is "Warning Temperature" or "Critical Temperature" => SensorRole.StorageTempLimit,
        SensorType.Load when n == "Read Activity" => SensorRole.StorageReadActivity,
        SensorType.Load when n == "Write Activity" => SensorRole.StorageWriteActivity,
        SensorType.Load when n == "Total Activity" => SensorRole.StorageTotalActivity,
        SensorType.Level when n == "Available Spare" => SensorRole.StorageSpare,
        SensorType.Level when n == "Available Spare Threshold" => SensorRole.StorageSpareThreshold,
        SensorType.Level when n == "Percentage Used" => SensorRole.StorageWear,
        SensorType.Factor when n == "Power On Count" => SensorRole.StoragePowerCycles,
        SensorType.Data when n is "Data Read" or "Host Reads" => SensorRole.StorageDataRead,
        SensorType.Data when n is "Data Written" or "Host Writes" => SensorRole.StorageDataWritten,
        SensorType.Data when n == "Free Space" => SensorRole.StorageFreeSpace,
        SensorType.Data when n == "Total Space" => SensorRole.StorageTotalSpace,
        _ => SensorRole.None
    };
    private static SensorRole Network(SensorType st, string n) => st switch
    {
        SensorType.Throughput when n == "Upload Speed" => SensorRole.NetUpload,
        SensorType.Throughput when n == "Download Speed" => SensorRole.NetDownload,
        SensorType.Load when n == "Network Utilization" => SensorRole.NetUtilization,
        SensorType.Data when n == "Data Uploaded" => SensorRole.NetDataUploaded,
        SensorType.Data when n == "Data Downloaded" => SensorRole.NetDataDownloaded,
        _ => SensorRole.None
    };
}
