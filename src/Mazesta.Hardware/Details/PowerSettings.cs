using System.Runtime.InteropServices; using Mazesta.Core.Health.Checkup;
namespace Mazesta.Hardware.Details;

/// <summary>
/// Whether the machine runs on mains or battery, and the active power plan's processor settings for that source: the maximum processor state
/// (PROCTHROTTLEMAX) and the boost mode (PERFBOOSTMODE). Read-only, from the documented power APIs; a value Windows does not give stays null.
/// </summary>
public static class PowerSettings
{
    private static readonly Guid Processor = new("54533251-82be-4824-96c1-47b60b740d00"), MaxState = new("bc5038f7-23e0-4960-96da-33abaf5935ec"),
        BoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");

    public static PowerFacts Read()
    {
        bool? mains = null, battery = null;
        if (GetSystemPowerStatus(out var s))
        {
            mains = s.ACLineStatus switch { 0 => false, 1 => true, _ => null };
            battery = s.BatteryFlag switch { 255 => null, _ => (s.BatteryFlag & 128) == 0 };   // 128: no system battery
        }
        int? max = null, boost = null;
        if (PowerGetActiveScheme(IntPtr.Zero, out var schemePtr) == 0)
        {
            try
            {
                var scheme = Marshal.PtrToStructure<Guid>(schemePtr);
                uint? Value(Guid setting)
                {
                    var sub = Processor; uint v;
                    uint r = mains == false ? PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out v) : PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out v);
                    return r == 0 ? v : null;
                }
                max = Value(MaxState) is { } m and <= 100 ? (int)m : null;
                boost = Value(BoostMode) is { } b ? (int)b : null;
            }
            finally { LocalFree(schemePtr); }
        }
        return new(mains, battery, max, boost);
    }

    [StructLayout(LayoutKind.Sequential)] private struct SystemPowerStatus { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, out uint value);
}
