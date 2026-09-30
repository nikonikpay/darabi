using System.Runtime.InteropServices; using System.Text;
namespace Mazesta.Hardware.Nvidia;

/// <summary>
/// The parts of NVIDIA's public management library (nvml.dll, installed with every NVIDIA driver in System32) that the tuning page uses. Only
/// documented entry points are declared - no private NVAPI calls - so what the page offers is what NVIDIA supports on this driver. Newer calls
/// (clock offsets, driver 555+) are missing from older drivers; <see cref="Call"/> turns that into a "not supported" result instead of a crash.
/// </summary>
internal static class Nvml
{
    private const string Dll = "nvml.dll";
    public const int Success = 0, NotSupported = 3, NoPermission = 4, GpuLost = 15, FunctionMissing = -1;
    public const int ClockGraphics = 0, ClockMemory = 2, TemperatureGpu = 0, FanPolicyManual = 1, PstateUnknown = 32;

    [StructLayout(LayoutKind.Sequential)]
    public struct ClockOffset { public uint Version; public int Type; public int Pstate; public int OffsetMHz; public int MinOffsetMHz; public int MaxOffsetMHz; }
    /// <summary>NVML_STRUCT_VERSION(ClockOffset, 1): the struct size with the version number in the top byte.</summary>
    public static readonly uint ClockOffsetVersion = (uint)Marshal.SizeOf<ClockOffset>() | (1u << 24);

    [DllImport(Dll)] public static extern int nvmlInit_v2();
    [DllImport(Dll)] public static extern IntPtr nvmlErrorString(int result);
    [DllImport(Dll)] public static extern int nvmlDeviceGetCount_v2(out uint count);
    [DllImport(Dll)] public static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport(Dll)] public static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);
    [DllImport(Dll)] public static extern int nvmlDeviceGetUUID(IntPtr device, byte[] uuid, uint length);
    [DllImport(Dll)] public static extern int nvmlDeviceGetSupportedPerformanceStates(IntPtr device, [Out] int[] pstates, uint size);
    [DllImport(Dll)] public static extern int nvmlDeviceGetClockOffsets(IntPtr device, ref ClockOffset info);
    [DllImport(Dll)] public static extern int nvmlDeviceSetClockOffsets(IntPtr device, ref ClockOffset info);
    [DllImport(Dll)] public static extern int nvmlDeviceSetGpuLockedClocks(IntPtr device, uint minMHz, uint maxMHz);
    [DllImport(Dll)] public static extern int nvmlDeviceResetGpuLockedClocks(IntPtr device);
    [DllImport(Dll)] public static extern int nvmlDeviceGetMaxClockInfo(IntPtr device, int type, out uint mhz);
    [DllImport(Dll)] public static extern int nvmlDeviceGetClockInfo(IntPtr device, int type, out uint mhz);
    [DllImport(Dll)] public static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint celsius);
    [DllImport(Dll)] public static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);
    [DllImport(Dll)] public static extern int nvmlDeviceGetPowerManagementLimitConstraints(IntPtr device, out uint minMilliwatts, out uint maxMilliwatts);
    [DllImport(Dll)] public static extern int nvmlDeviceGetPowerManagementDefaultLimit(IntPtr device, out uint milliwatts);
    [DllImport(Dll)] public static extern int nvmlDeviceGetPowerManagementLimit(IntPtr device, out uint milliwatts);
    [DllImport(Dll)] public static extern int nvmlDeviceSetPowerManagementLimit(IntPtr device, uint milliwatts);
    [DllImport(Dll)] public static extern int nvmlDeviceGetNumFans(IntPtr device, out uint count);
    [DllImport(Dll)] public static extern int nvmlDeviceGetFanSpeed_v2(IntPtr device, uint fan, out uint percent);
    [DllImport(Dll)] public static extern int nvmlDeviceGetMinMaxFanSpeed(IntPtr device, out uint min, out uint max);
    [DllImport(Dll)] public static extern int nvmlDeviceGetFanControlPolicy_v2(IntPtr device, uint fan, out uint policy);
    [DllImport(Dll)] public static extern int nvmlDeviceSetFanSpeed_v2(IntPtr device, uint fan, uint percent);
    [DllImport(Dll)] public static extern int nvmlDeviceSetDefaultFanSpeed_v2(IntPtr device, uint fan);
    // Read-only device facts for the specification pages.
    [StructLayout(LayoutKind.Sequential)] public struct Bar1Memory { public ulong Total, Free, Used; }
    [DllImport(Dll)] public static extern int nvmlDeviceGetCurrPcieLinkGeneration(IntPtr device, out uint gen);
    [DllImport(Dll)] public static extern int nvmlDeviceGetCurrPcieLinkWidth(IntPtr device, out uint width);
    [DllImport(Dll)] public static extern int nvmlDeviceGetMaxPcieLinkGeneration(IntPtr device, out uint gen);
    [DllImport(Dll)] public static extern int nvmlDeviceGetMaxPcieLinkWidth(IntPtr device, out uint width);
    [DllImport(Dll)] public static extern int nvmlDeviceGetVbiosVersion(IntPtr device, byte[] version, uint length);
    [DllImport(Dll)] public static extern int nvmlDeviceGetMemoryBusWidth(IntPtr device, out uint bits);
    [DllImport(Dll)] public static extern int nvmlDeviceGetNumGpuCores(IntPtr device, out uint cores);
    [DllImport(Dll)] public static extern int nvmlDeviceGetBAR1MemoryInfo(IntPtr device, ref Bar1Memory info);
    [DllImport(Dll)] public static extern int nvmlDeviceGetCudaComputeCapability(IntPtr device, out int major, out int minor);
    [DllImport(Dll)] public static extern int nvmlDeviceGetArchitecture(IntPtr device, out uint architecture);
    [DllImport(Dll)] public static extern int nvmlDeviceGetPciInfo_v3(IntPtr device, byte[] info);
    // Read while a benchmark runs: why the clock is held (the "clock event reasons", named "throttle reasons" before driver 535), how busy the
    // card is, the temperature at which it slows itself, and the power limit in force.
    [StructLayout(LayoutKind.Sequential)] public struct Utilization { public uint Gpu, Memory; }
    public const int TemperatureThresholdSlowdown = 1;
    [DllImport(Dll)] public static extern int nvmlDeviceGetCurrentClocksEventReasons(IntPtr device, out ulong reasons);
    [DllImport(Dll)] public static extern int nvmlDeviceGetCurrentClocksThrottleReasons(IntPtr device, out ulong reasons);
    [DllImport(Dll)] public static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization utilization);
    [DllImport(Dll)] public static extern int nvmlDeviceGetTemperatureThreshold(IntPtr device, int threshold, out uint celsius);
    [DllImport(Dll)] public static extern int nvmlDeviceGetEnforcedPowerLimit(IntPtr device, out uint milliwatts);

    /// <summary>Runs one NVML call; an entry point this driver does not export reads as <see cref="FunctionMissing"/>.</summary>
    public static int Call(Func<int> call) { try { return call(); } catch (EntryPointNotFoundException) { return FunctionMissing; } }

    public static string Describe(int result)
    {
        if (result == FunctionMissing) return "not available in this driver version";
        try { return Marshal.PtrToStringAnsi(nvmlErrorString(result)) ?? $"NVML error {result}"; } catch (EntryPointNotFoundException) { return $"NVML error {result}"; }
    }

    public static string? Text(Func<byte[], int> call)
    {
        var buffer = new byte[96];
        if (Call(() => call(buffer)) != Success) return null;
        int end = Array.IndexOf(buffer, (byte)0);
        return Encoding.ASCII.GetString(buffer, 0, end < 0 ? buffer.Length : end);
    }
}
