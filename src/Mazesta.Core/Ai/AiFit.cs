namespace Mazesta.Core.Ai;

/// <summary>Where a model would run: wholly on the GPU, split between the GPU and system RAM, on the CPU alone, or nowhere.</summary>
public enum AiFitMode { Gpu, Split, Cpu, TooBig }

/// <summary>What the machine offers a model. Null where it is not known (no GPU, or its memory or bandwidth could not be read).</summary>
/// <param name="GpuBandwidthBytesPerSecond">The GPU memory's peak bandwidth from its bus width and memory clock (NVML), not a measurement.</param>
public sealed record AiMachine(string? GpuName, long? VramBytes, double? GpuBandwidthBytesPerSecond, long RamTotalBytes, long RamAvailableBytes);

/// <param name="NeedBytes">Weights, the cache for <see cref="AiFitter.Context"/> tokens and the runtime's working buffers.</param>
/// <param name="GpuShare">The part of the model that would sit on the GPU (1 on the GPU alone, 0 on the CPU).</param>
/// <param name="Tight">It fits, but with little room: other programs' use of the same memory can push it out.</param>
/// <param name="CeilingTokensPerSecond">Generation can not be faster than the memory can deliver the weights each token needs: bandwidth over the
/// active weights. A ceiling, not a prediction - real runs reach a part of it; only a measurement says how much. Known only on the GPU alone.</param>
/// <param name="MaxContext">The longest text (tokens) whose cache would still fit beside the weights on the GPU, up to the model's own limit.</param>
public sealed record AiFit(AiFitMode Mode, long NeedBytes, double GpuShare, bool Tight, double? CeilingTokensPerSecond, int? MaxContext);

/// <summary>
/// Whether a model can run on this machine, before anything is downloaded - the question tools such as llmfit answer, here from the model's
/// own header figures and this machine's measured memory. It is an estimate and says so; the benchmark then measures the real speed. The
/// margins follow llama.cpp: it keeps 1 GiB of each device free when it fits a model (its --fit-target default), and a chat needs a few
/// thousand tokens of context, so the cache is counted for <see cref="Context"/> tokens.
/// </summary>
public static class AiFitter
{
    public const int Context = 4096;
    public const long Gib = 1L << 30;
    /// <summary>Compute buffers, the recurrent and sliding-window state, the output logits: about half a GiB at 4096 tokens for these models.</summary>
    public const long OverheadBytes = Gib / 2;
    /// <summary>Kept free on each device, as llama.cpp does, for the desktop and other programs.</summary>
    public const long ReserveBytes = Gib;

    public static long NeedBytes(AiModel m, int context = Context) => m.Bytes + m.KvBytesPerToken * context + OverheadBytes;

    public static AiFit Fit(AiModel m, AiMachine pc)
    {
        long need = NeedBytes(m);
        long ram = Math.Max(0, pc.RamAvailableBytes - ReserveBytes);
        if (pc.VramBytes is { } vram and > 0)
        {
            long gpu = Math.Max(0, vram - ReserveBytes);
            if (need <= gpu)
            {
                double? ceiling = pc.GpuBandwidthBytesPerSecond is { } bw ? bw / m.ActiveBytes : null;
                long room = gpu - m.Bytes - OverheadBytes;
                int maxContext = (int)Math.Min(m.ContextMax, room / Math.Max(1, m.KvBytesPerToken));
                return new(AiFitMode.Gpu, need, 1, need > gpu * 0.85, ceiling, maxContext);
            }
            if (need <= gpu + ram) return new(AiFitMode.Split, need, (double)gpu / need, need > (gpu + ram) * 0.85, null, null);
            return new(AiFitMode.TooBig, need, 0, false, null, null);
        }
        return need <= ram ? new(AiFitMode.Cpu, need, 0, need > ram * 0.85, null, null) : new(AiFitMode.TooBig, need, 0, false, null, null);
    }

    /// <summary>The model to suggest for this machine: the largest that runs wholly on the GPU with room to spare, else the largest that runs at
    /// all without being tight; null when none does. Largest is by file size: within this catalog a bigger model is the more capable one.</summary>
    public static AiModel? Recommend(IEnumerable<AiModel> models, AiMachine pc)
    {
        var fits = models.Select(m => (m, f: Fit(m, pc))).Where(x => x.f.Mode != AiFitMode.TooBig && !x.f.Tight).ToList();
        return fits.Where(x => x.f.Mode == AiFitMode.Gpu).OrderByDescending(x => x.m.Bytes).Select(x => x.m).FirstOrDefault()
            ?? fits.OrderByDescending(x => x.m.Bytes).Select(x => x.m).FirstOrDefault();
    }

    /// <summary>Peak memory bandwidth from NVML's figures: the bus width in bytes times the data rate, which is twice the memory clock NVML
    /// reports (GDDR6, GDDR6X and HBM alike: an RTX 3090 reports 9751 MHz on 384 bits, 936 GB/s).</summary>
    public static double? GpuBandwidth(int? busWidthBits, int? maxMemoryClockMhz)
        => busWidthBits is > 0 and { } bits && maxMemoryClockMhz is > 0 and { } mhz ? bits / 8.0 * mhz * 2e6 : null;
}
