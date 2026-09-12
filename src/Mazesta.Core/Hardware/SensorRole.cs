namespace Mazesta.Core.Hardware;

public enum SensorRole
{
    None,
    CpuPackageTemp, CpuCoreTemp, CpuTctlTdie, CpuCcdTemp, CpuCoreClock, CpuCoreClockAverage, CpuEffectiveClock, CpuEffectiveClockAverage, CpuBusClock,
    CpuVcore, CpuPackagePower, CpuCorePower, CpuTotalLoad, CpuThreadLoad, CpuFan,
    GpuCoreTemp, GpuHotSpotTemp, GpuVramTemp, GpuCoreClock, GpuMemoryClock, GpuLoad3D, GpuLoadD3D3D, GpuLoadCompute, GpuLoadVideo, GpuLoadMemoryController,
    GpuPower, GpuVoltage, GpuFanRpm, GpuFanPercent, GpuVramTotal, GpuVramUsed, GpuVramFree,
    RamUsed, RamFree, RamTotal, RamLoad, DimmTemp,
    BoardTemp, ChipsetTemp, BoardFan, BoardVoltage,
    StorageTemp, StorageUsedSpace, StorageReadRate, StorageWriteRate, StorageRemainingLife, StoragePowerOnHours,
    NetUpload, NetDownload, NetUtilization
}
