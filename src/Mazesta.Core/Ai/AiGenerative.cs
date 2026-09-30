namespace Mazesta.Core.Ai;

/// <summary>What a generative model makes.</summary>
public enum AiMedia { Image, Video, Audio, Mesh }

/// <summary>How a generative model would run on this machine: at its publisher's own quality, only in a reduced way (a quantized file,
/// weights moved out to system memory, a lower resolution: slower or plainer), or not at all.</summary>
public enum AiGenFit { Full, Reduced, No }

/// <summary>Why a model does not run here, or null when it does.</summary>
public enum AiGenBlock { NoGpu, NeedsNvidia, LittleVram }

/// <summary>
/// An image, video, audio or 3D model that can be run on one's own computer (in ComfyUI or its publisher's own tools; the app does not download
/// or run these). The figures are the ones published for it, not measured here: <see cref="FullVram"/> is the graphics memory its publisher
/// asks for the normal path, <see cref="MinVram"/> the least that the publisher or the maker of the runtime it is run in (ComfyUI, Unsloth)
/// says it runs in with quantization or offloading. <see cref="Source"/> is where each figure was read (checked on 2026-10-01).
/// </summary>
/// <param name="NvidiaOnly">Its own code needs CUDA: an AMD or Intel card does not run it, whatever its memory.</param>
/// <param name="License">The weights' licence as published; null where it was not confirmed.</param>
/// <param name="NoteKey">Localisation key of a limit worth knowing (tested only on Linux, territories the licence leaves out), or null.</param>
public sealed record AiGenModel(string Id, string Name, AiMedia Media, string Params, long MinVram, long FullVram, bool NvidiaOnly,
    string? License, string Runtime, string Source, string PurposeKey, string? NoteKey = null);

public sealed record AiGenVerdict(AiGenFit Fit, AiGenBlock? Block);

public static class AiGenCatalog
{
    private const long G = AiFitter.Gib;

    /// <summary>Within each kind, the most capable first: the suggestion is the first that runs at full quality, else the first that runs at all.</summary>
    public static IReadOnlyList<AiGenModel> Models { get; } =
    [
        // Unsloth: 11-12 GB with its Q4_K_M GGUF, FP8 on 6 GB with offloading (under 2x slower), 24 GB+ for FP8/INT8 without it. unsloth.ai/docs/models/qwen-image-2.1
        new("qwen-image-2.1", "Qwen-Image 2.1", AiMedia.Image, "7B + 8B", 6 * G, 24 * G, false, null, "ComfyUI · stable-diffusion.cpp", "unsloth.ai/docs/models/qwen-image-2.1", "AiGen_Purpose_QwenImage"),
        // Black Forest Labs: about 13 GB; about 8 GB quantized. Apache-2.0.
        new("flux.2-klein-4b", "FLUX.2 [klein] 4B", AiMedia.Image, "4B", 8 * G, 13 * G, false, "Apache-2.0", "ComfyUI", "help.bfl.ai", "AiGen_Purpose_FluxKlein"),

        // ComfyUI: "a 12 GB card plus offloading can run it"; 24 GB for the normal path. 33B, 42.5 GB at the least (int8). blog.comfy.org/p/minimax-h3-day-0-support-in-comfyui
        new("minimax-h3", "MiniMax H3", AiMedia.Video, "33B", 12 * G, 24 * G, false, "MiniMax Community", "ComfyUI", "blog.comfy.org", "AiGen_Purpose_MiniMaxH3", "AiGen_Note_H3"),
        // Lightricks: an NVIDIA GPU with 32 GB, 32 GB of RAM, CUDA 12.7+; quantized (FP8/GGUF) builds on 16 GB. ltx.io
        new("ltx-2.5", "LTX-2.5", AiMedia.Video, "", 16 * G, 32 * G, true, null, "ComfyUI", "ltx.io", "AiGen_Purpose_Ltx"),
        // Wan-AI: 24 GB (RTX 4090) with offloading for 720p; ComfyUI: fits 8 GB with its native offloading. Apache-2.0.
        new("wan2.2-ti2v-5b", "Wan 2.2 TI2V 5B", AiMedia.Video, "5B", 8 * G, 24 * G, false, "Apache-2.0", "ComfyUI", "github.com/Wan-Video/Wan2.2", "AiGen_Purpose_Wan5b"),

        // MiniMax Music 3: no official minimum; about 27 GB used in a test on a 48 GB card, 32 GB suggested; 8 GB claimed with layer streaming.
        new("minimax-music-3", "MiniMax Music 3", AiMedia.Audio, "8B + 0.6B", 8 * G, 32 * G, true, null, "Python (CUDA)", "huggingface.co/MiniMaxAI/MiniMax-Music3", "AiGen_Purpose_MiniMaxMusic", "AiGen_Note_Linux"),
        // Qwen3-TTS 1.7B: about 4.2 GB at FP16; 8 GB comfortable. Apache-2.0.
        new("qwen3-tts-1.7b", "Qwen3-TTS 1.7B", AiMedia.Audio, "1.7B", 6 * G, 8 * G, false, "Apache-2.0", "Python · ComfyUI", "github.com/QwenLM/Qwen3-TTS", "AiGen_Purpose_QwenTts"),

        // TencentARC Pixal3D: built on TRELLIS.2, 24 GB for the official path, a low-memory refine mode on 16 GB.
        new("pixal3d", "Pixal3D", AiMedia.Mesh, "", 16 * G, 24 * G, true, null, "Python (CUDA) · ComfyUI", "github.com/TencentARC/Pixal3D", "AiGen_Purpose_Pixal3d", "AiGen_Note_Linux"),
        // Microsoft: "an NVIDIA GPU with at least 24GB", tested on Linux only; about 12 GB for 512-resolution output in community builds. MIT.
        new("trellis.2-4b", "TRELLIS.2 4B", AiMedia.Mesh, "4B", 12 * G, 24 * G, true, "MIT", "Python (CUDA) · ComfyUI", "huggingface.co/microsoft/TRELLIS.2-4B", "AiGen_Purpose_Trellis2", "AiGen_Note_Linux"),
        // Tencent: 10 GB for the shape alone, 29 GB for shape and texture together.
        new("hunyuan3d-2.1", "Hunyuan3D 2.1", AiMedia.Mesh, "", 10 * G, 29 * G, true, "Tencent Hunyuan Community", "Python (CUDA) · ComfyUI", "github.com/Tencent-Hunyuan/Hunyuan3D-2.1", "AiGen_Purpose_Hunyuan3d", "AiGen_Note_Hunyuan"),
    ];

    /// <summary>A card reports a little under its nominal size (a "24 GB" card shows 23.9 GiB, some less), and publishers name the nominal size.</summary>
    public const long Slack = G / 2;

    public static bool IsNvidia(string? gpu) => gpu is not null && (gpu.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || gpu.Contains("GeForce", StringComparison.OrdinalIgnoreCase)
        || gpu.Contains("Quadro", StringComparison.OrdinalIgnoreCase) || gpu.Contains("RTX", StringComparison.Ordinal) || gpu.Contains("Tesla", StringComparison.Ordinal));

    /// <summary>All of these run on the graphics card; system memory only helps the reduced, offloaded path, so the card decides.</summary>
    public static AiGenVerdict Judge(AiGenModel m, AiMachine pc)
    {
        if (pc.VramBytes is not > 0 || pc.GpuName is null) return new(AiGenFit.No, AiGenBlock.NoGpu);
        if (m.NvidiaOnly && !IsNvidia(pc.GpuName)) return new(AiGenFit.No, AiGenBlock.NeedsNvidia);
        long vram = pc.VramBytes.Value + Slack;
        return vram >= m.FullVram ? new(AiGenFit.Full, null) : vram >= m.MinVram ? new(AiGenFit.Reduced, null) : new(AiGenFit.No, AiGenBlock.LittleVram);
    }

    public static AiGenModel? Suggest(AiMedia media, AiMachine pc)
    {
        var kind = Models.Where(m => m.Media == media).Select(m => (m, v: Judge(m, pc))).ToList();
        return kind.Where(x => x.v.Fit == AiGenFit.Full).Select(x => x.m).FirstOrDefault() ?? kind.Where(x => x.v.Fit == AiGenFit.Reduced).Select(x => x.m).FirstOrDefault();
    }
}
