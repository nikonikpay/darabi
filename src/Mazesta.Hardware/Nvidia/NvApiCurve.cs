using System.Runtime.InteropServices;
using Mazesta.Core.Tuning;
namespace Mazesta.Hardware.Nvidia;

/// <summary>
/// The voltage/frequency table the NVIDIA driver itself runs a card by, read through NVAPI (nvapi64.dll, which the driver installs): the same
/// table the makers' tuning programs draw as the card's curve. It is there at once - nothing has to be put under load to see it. The calls are
/// read-only and not in NVIDIA's public headers, so every answer is checked before it is believed: a structure the driver refuses, a table with
/// too few points, voltages or clocks outside what a graphics card runs at, or a curve that does not rise, and there is no curve (null), never a
/// guessed one. The table holds the clocks as they are now, so the offset in force at each point (the clock-boost table) is taken off again:
/// what is returned is the card's stock curve whatever is applied at the moment.
/// </summary>
public static class NvApiCurve
{
    private const uint Initialize = 0x0150E828, EnumPhysicalGpus = 0xE5AC921F, GetFullName = 0xCEEE8E9F, ClockBoostMask = 0x507B4B59, VfpCurve = 0x21537AD4, ClockBoostTable = 0x23F1B133;
    private const int Head = 68, Points = 255, MaskSize = Head + Points * 24, CurveSize = Head + Points * 28, TableSize = Head + Points * 36;

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr Query(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EnumCall([Out] IntPtr[] gpus, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BlockCall(IntPtr gpu, [In, Out] byte[] block);

    private static readonly object Gate = new(); private static bool _tried, _ready;
    private static T? Function<T>(uint id) where T : Delegate { var p = Query(id); return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p); }

    /// <summary>The stock curve of the NVIDIA card named <paramref name="name"/> (as NVML or Windows names it), lowest voltage first, or null
    /// when the driver does not give one that can be trusted. With several cards of one name there is no telling which is meant: null.</summary>
    public static IReadOnlyList<VfPoint>? ReadStock(string name)
    {
        try
        {
            lock (Gate)
            {
                if (!_tried) { _tried = true; _ready = Function<InitCall>(Initialize) is { } init && init() == 0; }
                if (!_ready || Function<EnumCall>(EnumPhysicalGpus) is not { } list || Function<BlockCall>(GetFullName) is not { } fullName) return null;
                var gpus = new IntPtr[64];
                if (list(gpus, out uint count) != 0 || count == 0) return null;
                var named = gpus.Take((int)count).Where(g =>
                {
                    var text = new byte[64];
                    if (fullName(g, text) != 0) return false;
                    string n = System.Text.Encoding.ASCII.GetString(text).TrimEnd('\0').Trim();
                    return n.Length > 0 && (name.Contains(n, StringComparison.OrdinalIgnoreCase) || n.Contains(name, StringComparison.OrdinalIgnoreCase));
                }).ToList();
                return named.Count == 1 || (named.Count == 0 && count == 1) ? Read(named.Count == 1 ? named[0] : gpus[0]) : null;
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or MarshalDirectiveException) { return null; }
    }

    private static IReadOnlyList<VfPoint>? Read(IntPtr gpu)
    {
        if (Function<BlockCall>(ClockBoostMask) is not { } maskCall || Function<BlockCall>(VfpCurve) is not { } curveCall) return null;
        var mask = Block(MaskSize);
        if (maskCall(gpu, mask) != 0) return null;
        var curve = Block(CurveSize); Array.Copy(mask, 4, curve, 4, 32);
        if (curveCall(gpu, curve) != 0) return null;
        // The offsets in force: without them the curve read is the tuned one. A driver that will not give them leaves no way to tell, unless
        // nothing is applied; that cannot be known here, so no curve then.
        var table = Block(TableSize); Array.Copy(mask, 4, table, 4, 32);
        if (Function<BlockCall>(ClockBoostTable) is not { } tableCall || tableCall(gpu, table) != 0) return null;

        var points = new List<VfPoint>();
        for (int i = 0; i < Points; i++)
        {
            if ((mask[4 + i / 8] >> (i % 8) & 1) == 0) continue;
            int at = Head + i * 28;
            if (BitConverter.ToUInt32(curve, at) != 0) continue;   // 0: the graphics clock (the table holds the memory's entry too)
            double khz = BitConverter.ToUInt32(curve, at + 4), microvolts = BitConverter.ToUInt32(curve, at + 8), delta = BitConverter.ToInt32(table, Head + i * 36 + 20);
            // The graphics core's points come first, voltage rising; what follows them (the memory's entries, at clocks no core runs) starts lower again.
            if (points.Count > 0 && microvolts / 1e6 <= points[^1].VoltageV) break;
            points.Add(new((khz - delta) / 1000, microvolts / 1e6));
        }
        return Plausible(points) ? points : null;
    }

    /// <summary>A graphics card's curve: a fair number of points, between 0.3 and 1.6 V and 100 and 4000 MHz, voltage and clock both rising.</summary>
    internal static bool Plausible(IReadOnlyList<VfPoint> p)
    {
        if (p.Count < 8) return false;
        for (int i = 0; i < p.Count; i++)
        {
            if (p[i].VoltageV is < 0.3 or > 1.6 || p[i].ClockMHz is < 100 or > 4000) return false;
            if (i > 0 && (p[i].VoltageV < p[i - 1].VoltageV || p[i].ClockMHz < p[i - 1].ClockMHz - 0.5)) return false;
        }
        return p[^1].ClockMHz > p[0].ClockMHz && p[^1].VoltageV > p[0].VoltageV;
    }

    private static byte[] Block(int size) { var b = new byte[size]; BitConverter.TryWriteBytes(b, (uint)(size | 1 << 16)); return b; }
}
