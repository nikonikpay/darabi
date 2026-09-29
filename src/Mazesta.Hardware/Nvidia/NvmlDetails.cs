using System.Text;
namespace Mazesta.Hardware.Nvidia;

/// <summary>One NVIDIA card as NVML reports it; a value the driver does not give stays null.</summary>
internal sealed record NvmlCard(int? PciBus, string? PciBusId, int? CurrentGen, int? CurrentWidth, int? MaxGen, int? MaxWidth, string? Vbios, int? BusWidthBits, int? Cores,
    string? Architecture, string? ComputeCapability, long? Bar1Bytes, int? MaxCoreClockMhz, int? MaxMemoryClockMhz, int? PowerDefaultW, int? PowerMaxW);

/// <summary>
/// The read-only facts NVML gives about each NVIDIA card: VBIOS, memory bus width, CUDA cores, architecture, compute capability, BAR1 size (the
/// window the CPU sees the card's memory through: about the whole VRAM when Resizable BAR is in use, 256 MB when it is not), maximum clocks and
/// power limits, and the PCI bus it sits on (to pair it with Windows' device). Checked on the owner's RTX 3090 against HWiNFO. No NVIDIA driver:
/// an empty list.
/// </summary>
internal static class NvmlDetails
{
    private static readonly string[] Architectures = ["", "", "Kepler", "Maxwell", "Pascal", "Volta", "Turing", "Ampere", "Ada Lovelace", "Hopper", "Blackwell"];

    public static IReadOnlyList<NvmlCard> Read()
    {
        try
        {
            if (Nvml.Call(Nvml.nvmlInit_v2) != Nvml.Success || Nvml.nvmlDeviceGetCount_v2(out uint count) != Nvml.Success) return [];
            var cards = new List<NvmlCard>();
            for (uint i = 0; i < count; i++)
            {
                if (Nvml.nvmlDeviceGetHandleByIndex_v2(i, out var d) != Nvml.Success) continue;
                int? Get(Func<int> call, Func<uint> value) => Nvml.Call(call) == Nvml.Success ? (int)value() : null;
                uint g1 = 0, w1 = 0, g2 = 0, w2 = 0, bus = 0, cores = 0, arch = 0, core = 0, mem = 0, def = 0, min = 0, max = 0;
                var bar = new Nvml.Bar1Memory(); int ccMajor = 0, ccMinor = 0;
                string? vbios = Nvml.Text(b => Nvml.nvmlDeviceGetVbiosVersion(d, b, (uint)b.Length));
                var pci = new byte[128]; string? busId = null; int? pciBus = null;
                if (Nvml.Call(() => Nvml.nvmlDeviceGetPciInfo_v3(d, pci)) == Nvml.Success)
                {
                    // nvmlPciInfo_t: busIdLegacy[16], domain, bus, device, pciDeviceId, pciSubSystemId, busId[32].
                    busId = Encoding.ASCII.GetString(pci, 36, 32).TrimEnd('\0');
                    pciBus = (int)BitConverter.ToUInt32(pci, 20);
                }
                cards.Add(new(pciBus, busId,
                    Get(() => Nvml.nvmlDeviceGetCurrPcieLinkGeneration(d, out g1), () => g1), Get(() => Nvml.nvmlDeviceGetCurrPcieLinkWidth(d, out w1), () => w1),
                    Get(() => Nvml.nvmlDeviceGetMaxPcieLinkGeneration(d, out g2), () => g2), Get(() => Nvml.nvmlDeviceGetMaxPcieLinkWidth(d, out w2), () => w2),
                    vbios, Get(() => Nvml.nvmlDeviceGetMemoryBusWidth(d, out bus), () => bus), Get(() => Nvml.nvmlDeviceGetNumGpuCores(d, out cores), () => cores),
                    Nvml.Call(() => Nvml.nvmlDeviceGetArchitecture(d, out arch)) == Nvml.Success && arch < Architectures.Length && Architectures[arch].Length > 0 ? Architectures[arch] : null,
                    Nvml.Call(() => Nvml.nvmlDeviceGetCudaComputeCapability(d, out ccMajor, out ccMinor)) == Nvml.Success ? $"{ccMajor}.{ccMinor}" : null,
                    Nvml.Call(() => Nvml.nvmlDeviceGetBAR1MemoryInfo(d, ref bar)) == Nvml.Success && bar.Total > 0 ? (long)bar.Total : null,
                    Get(() => Nvml.nvmlDeviceGetMaxClockInfo(d, Nvml.ClockGraphics, out core), () => core), Get(() => Nvml.nvmlDeviceGetMaxClockInfo(d, Nvml.ClockMemory, out mem), () => mem),
                    Get(() => Nvml.nvmlDeviceGetPowerManagementDefaultLimit(d, out def), () => def / 1000),
                    Get(() => Nvml.nvmlDeviceGetPowerManagementLimitConstraints(d, out min, out max), () => max / 1000)));
            }
            return cards;
        }
        catch (DllNotFoundException) { return []; }
    }
}
