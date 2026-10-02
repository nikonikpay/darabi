using Mazesta.Core.Hardware;
namespace Mazesta.Hardware.Nvidia;

/// <summary>One PCI Express counter NVML keeps: its field id (nvml.h NVML_FI_DEV_PCIE_*), the name it is shown by, and what it counts.</summary>
internal sealed record PcieCounter(uint Field, string Name, SensorRole Role, bool PerLane = false);

/// <summary>One NVIDIA card's PCI Express counters that its driver keeps, by NVML's device index and the card's name.</summary>
internal sealed record PcieCard(uint Index, string Name, IReadOnlyList<PcieCounter> Counters);

/// <summary>
/// The PCI Express error counters NVIDIA's driver keeps for a card since the computer started (the same counters HWiNFO shows under "PCI Express
/// Error Counters"), read through NVML's documented field values. Which a card keeps depends on the card and the driver: only the fields that answer
/// when the app starts are offered, so a counter the driver does not keep is never shown as a zero. Errors on single lanes are added up into one
/// number. No NVIDIA driver: no cards.
/// </summary>
internal static class NvmlPcieCounters
{
    /// <summary>The kinds of error the link itself detects (each one also counted by the card's own AER registers); their sum is the total.</summary>
    public static readonly PcieCounter[] All =
    [
        new(175, "PCIe Receiver Errors", SensorRole.GpuPcieErrorCounter),
        new(176, "PCIe Bad TLP Count", SensorRole.GpuPcieErrorCounter),
        new(178, "PCIe Bad DLLP Count", SensorRole.GpuPcieErrorCounter),
        new(182, "PCIe LCRC Error Count", SensorRole.GpuPcieErrorCounter),
        new(179, "PCIe Non-Fatal Error Count", SensorRole.GpuPcieErrorCounter),
        new(180, "PCIe Fatal Error Count", SensorRole.GpuPcieErrorCounter),
        new(181, "PCIe Unsupported Request Count", SensorRole.GpuPcieErrorCounter),
        new(173, "PCIe Correctable Error Count", SensorRole.GpuPcieRetryCounter),
        new(183, "PCIe Lane Errors (all lanes)", SensorRole.GpuPcieRetryCounter, PerLane: true),
        new(94, "PCIe Replay Count", SensorRole.GpuPcieRetryCounter),
        new(95, "PCIe Replay Rollover Count", SensorRole.GpuPcieRetryCounter),
        new(177, "PCIe NAKs Sent", SensorRole.GpuPcieRetryCounter),
        new(174, "PCIe NAKs Received", SensorRole.GpuPcieRetryCounter),
        new(169, "PCIe Recovery Count", SensorRole.GpuPcieRetryCounter),
    ];
    public const string TotalName = "PCIe Errors (Total)";
    private const int Lanes = 16;

    public static IReadOnlyList<PcieCard> Find()
    {
        try
        {
            if (Nvml.Call(Nvml.nvmlInit_v2) != Nvml.Success || Nvml.nvmlDeviceGetCount_v2(out uint count) != Nvml.Success) return [];
            var cards = new List<PcieCard>();
            for (uint i = 0; i < count; i++)
            {
                if (Nvml.nvmlDeviceGetHandleByIndex_v2(i, out var d) != Nvml.Success) continue;
                string? name = Nvml.Text(b => Nvml.nvmlDeviceGetName(d, b, (uint)b.Length));
                var kept = All.Where(c => Read(d, c) is not null).ToList();
                if (name is not null && kept.Count > 0) cards.Add(new(i, name, kept));
            }
            return cards;
        }
        catch (DllNotFoundException) { return []; }
    }

    /// <summary>The counters' values now, in the card's order; null for one that did not answer this time.</summary>
    public static double?[] Read(PcieCard card)
    {
        var values = new double?[card.Counters.Count];
        try
        {
            if (Nvml.nvmlDeviceGetHandleByIndex_v2(card.Index, out var d) != Nvml.Success) return values;
            for (int i = 0; i < values.Length; i++) values[i] = Read(d, card.Counters[i]);
        }
        catch (DllNotFoundException) { }
        return values;
    }

    private static double? Read(IntPtr device, PcieCounter c)
    {
        var fields = c.PerLane ? Enumerable.Range(0, Lanes).Select(l => new Nvml.FieldValue { FieldId = c.Field, ScopeId = (uint)l }).ToArray() : [new Nvml.FieldValue { FieldId = c.Field }];
        if (Nvml.Call(() => Nvml.nvmlDeviceGetFieldValues(device, fields.Length, fields)) != Nvml.Success) return null;
        double sum = 0; bool any = false;
        foreach (var f in fields) if (f.Return == Nvml.Success && Value(f) is { } v) { sum += v; any = true; }
        return any ? sum : null;
    }

    // nvmlValueType_t: 0 double, 1 unsigned int, 2 unsigned long (32 bits on Windows), 3 unsigned long long, 4 signed long long, 5 signed int, 6 unsigned short.
    private static double? Value(Nvml.FieldValue f) => f.ValueType switch
    {
        0 => f.Double, 1 or 2 => (uint)f.Raw, 3 => f.Raw, 4 => (long)f.Raw, 5 => (int)(uint)f.Raw, 6 => (ushort)f.Raw, _ => null,
    };
}
