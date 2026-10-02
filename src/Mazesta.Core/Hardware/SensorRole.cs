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
    NetUpload, NetDownload, NetUtilization,
    // Readings the app shows in details and the overlay but does not judge by (appended, so names stored by earlier versions stay valid).
    CpuCcdMaxTemp, CpuCcdAverageTemp, CpuCoreMaxLoad, CpuSocVoltage, CpuCoreVid, CpuPerCorePower, CpuCoreMultiplier,
    GpuLoadBus, GpuPowerPercent, GpuLoadEngine, GpuD3DMemoryDedicated, GpuD3DMemoryShared, GpuPcieRx, GpuPcieTx,
    VirtualMemoryLoad, VirtualMemoryUsed, VirtualMemoryFree,
    /// <summary>A DIMM's specification (capacity, sensor resolution, and the thermal limits it was programmed with, 0 when not programmed),
    /// not a live reading.</summary>
    DimmSpec, DimmTiming,
    BoardFanControl,
    StorageTempLimit, StorageReadActivity, StorageWriteActivity, StorageTotalActivity, StorageSpare, StorageSpareThreshold, StorageWear, StoragePowerCycles,
    StorageDataRead, StorageDataWritten, StorageFreeSpace, StorageTotalSpace,
    NetDataUploaded, NetDataDownloaded,
    /// <summary>How far a core is below the limit the processor itself reports (Intel: IA32_TEMPERATURE_TARGET), so its temperature plus this
    /// is that limit (TjMax). Not a temperature: never shown or judged as one.</summary>
    CpuTjMaxDistance,
    /// <summary>A graphics card's PCI Express error counters since the computer started (NVIDIA's driver): <see cref="GpuPcieErrorTotal"/> is the
    /// sum of the link's own error kinds, each of which is a <see cref="GpuPcieErrorCounter"/>; replays, NAKs and recoveries (what the link does
    /// about an error) are <see cref="GpuPcieRetryCounter"/>.</summary>
    GpuPcieErrorTotal, GpuPcieErrorCounter, GpuPcieRetryCounter
}
