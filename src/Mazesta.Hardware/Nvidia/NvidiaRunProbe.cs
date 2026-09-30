using System.Text; using Mazesta.Core.Health.Checkup;
namespace Mazesta.Hardware.Nvidia;

/// <summary>
/// Samples an NVIDIA card once a second while a benchmark runs on it: the driver's own word on what holds its clock (power cap, thermal slowdown,
/// hardware slowdown, an external power brake) and the PCIe link it runs at, counted only over the seconds the card was busy (70 % or more), as
/// at rest the reasons are "idle" and the link drops to save power. Read-only: nothing is set on the card. No NVIDIA driver, or no card of that
/// name: <see cref="Start"/> gives null.
/// </summary>
public sealed class NvidiaRunProbe : IDisposable
{
    // nvmlClocksEventReasons bits.
    private const ulong SwPowerCap = 0x4, HwSlowdown = 0x8, SwThermal = 0x20, HwThermal = 0x40, HwPowerBrake = 0x80;
    private readonly IntPtr _device; private readonly Timer _timer; private readonly object _lock = new();
    private readonly int? _slowdownC, _limitW;
    private int _samples, _power, _swThermal, _hw, _hwThermal, _brake, _gen, _width; private bool _stopped;

    private NvidiaRunProbe(IntPtr device)
    {
        _device = device;
        uint c = 0, mw = 0;
        _slowdownC = Nvml.Call(() => Nvml.nvmlDeviceGetTemperatureThreshold(device, Nvml.TemperatureThresholdSlowdown, out c)) == Nvml.Success && c > 0 ? (int)c : null;
        _limitW = Nvml.Call(() => Nvml.nvmlDeviceGetEnforcedPowerLimit(device, out mw)) == Nvml.Success && mw > 0 ? (int)(mw / 1000) : null;
        _timer = new Timer(_ => Sample(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    /// <summary>Starts sampling the card named <paramref name="gpuName"/> (the only NVIDIA card when there is one), or null.</summary>
    public static NvidiaRunProbe? Start(string? gpuName)
    {
        try
        {
            if (Nvml.Call(Nvml.nvmlInit_v2) != Nvml.Success || Nvml.nvmlDeviceGetCount_v2(out uint count) != Nvml.Success || count == 0) return null;
            var found = new List<(IntPtr Device, string Name)>();
            for (uint i = 0; i < count; i++)
                if (Nvml.nvmlDeviceGetHandleByIndex_v2(i, out var d) == Nvml.Success) found.Add((d, Nvml.Text(b => Nvml.nvmlDeviceGetName(d, b, (uint)b.Length)) ?? ""));
            var pick = found.Count == 1 ? found[0] : found.FirstOrDefault(f => gpuName is not null && Norm(f.Name) == Norm(gpuName));
            return pick.Device == IntPtr.Zero ? null : new NvidiaRunProbe(pick.Device);
        }
        catch (DllNotFoundException) { return null; }
    }

    private static string Norm(string s) { var t = new StringBuilder(); foreach (char c in s) if (char.IsLetterOrDigit(c)) t.Append(char.ToUpperInvariant(c)); return t.ToString(); }

    private void Sample()
    {
        lock (_lock)
        {
            if (_stopped) return;
            var use = new Nvml.Utilization();
            if (Nvml.Call(() => Nvml.nvmlDeviceGetUtilizationRates(_device, out use)) != Nvml.Success || use.Gpu < 70) return;
            ulong reasons = 0;
            int result = Nvml.Call(() => Nvml.nvmlDeviceGetCurrentClocksEventReasons(_device, out reasons));
            if (result != Nvml.Success) result = Nvml.Call(() => Nvml.nvmlDeviceGetCurrentClocksThrottleReasons(_device, out reasons));
            if (result == Nvml.Success)
            {
                _samples++;
                if ((reasons & SwPowerCap) != 0) _power++;
                if ((reasons & SwThermal) != 0) _swThermal++;
                if ((reasons & HwSlowdown) != 0) _hw++;
                if ((reasons & HwThermal) != 0) _hwThermal++;
                if ((reasons & HwPowerBrake) != 0) _brake++;
            }
            uint gen = 0, width = 0;
            if (Nvml.Call(() => Nvml.nvmlDeviceGetCurrPcieLinkGeneration(_device, out gen)) == Nvml.Success) _gen = Math.Max(_gen, (int)gen);
            if (Nvml.Call(() => Nvml.nvmlDeviceGetCurrPcieLinkWidth(_device, out width)) == Nvml.Success) _width = Math.Max(_width, (int)width);
        }
    }

    /// <summary>Stops sampling and gives what was counted; the link is the fastest and widest seen while busy (null when never busy).</summary>
    public (GpuThrottleCounts Counts, int? Gen, int? Width) Stop()
    {
        _timer.Dispose();
        lock (_lock)
        {
            _stopped = true;
            return (new GpuThrottleCounts(_samples, _power, _swThermal, _hw, _hwThermal, _brake, _slowdownC, _limitW), _gen > 0 ? _gen : null, _width > 0 ? _width : null);
        }
    }

    public void Dispose() { _timer.Dispose(); lock (_lock) _stopped = true; }
}
