namespace Mazesta.Core.Ai;

/// <summary>
/// A language model the AI benchmark can download: one GGUF file, pinned by size and SHA-256 so a changed or damaged file is never run. The
/// figures that decide whether it fits were read from the file's own header (not from a model card): <see cref="Layers"/>,
/// <see cref="KvBytesPerToken"/> (f16 keys and values of the layers that keep a full-attention cache; sliding-window and recurrent layers keep
/// a small fixed state, left to the overhead), and <see cref="ActiveBytes"/>, the weights read for each generated token - all of them for a
/// dense model, the shared part plus the chosen experts' share for a mixture-of-experts one.
/// </summary>
/// <param name="PurposeKey">Localisation key: what the model is good for and how it differs from the others.</param>
public sealed record AiModel(string Id, string Name, string Params, string Quant, string File, string Url, long Bytes, string Sha256, string License,
    int Layers, long KvBytesPerToken, long ActiveBytes, int ContextMax, bool MixtureOfExperts, string TierKey, string PurposeKey);

/// <summary>The llama.cpp build the benchmark downloads (MIT): its Vulkan package runs on NVIDIA, AMD and Intel GPUs and carries the CPU backends
/// too, so one download serves both. Pinned like the models; a newer build is a code change, not something the app fetches on its own.</summary>
public sealed record AiRuntime(string Build, string File, string Url, long Bytes, string Sha256);

public static class AiCatalog
{
    private const string Hf = "https://huggingface.co/ggml-org/";

    public static AiRuntime Runtime { get; } = new("b11265", "llama-b11265-bin-win-vulkan-x64.zip",
        "https://github.com/ggml-org/llama.cpp/releases/download/b11265/llama-b11265-bin-win-vulkan-x64.zip", 33068919, "8500b062a736204fdbd6530546f3511551e149bebda7f28d79ad2b3c71f08bcf");

    /// <summary>Smallest first. All Apache-2.0, from the llama.cpp project's own Hugging Face organisation (ggml-org), except Qwen3.5 4B, which ggml-org
    /// does not publish: bartowski's quantisation of Qwen's own weights, pinned the same way. Gemma 4 E2B and E4B keep a large per-layer embedding
    /// table that is looked up, not read whole, for each token: their <see cref="AiModel.ActiveBytes"/> leave it out. Their cache figure counts
    /// only the full-attention layers that keep their own cache (the last layers share an earlier layer's).</summary>
    public static IReadOnlyList<AiModel> Models { get; } =
    [
        new("qwen3.5-0.8b", "Qwen3.5 0.8B", "0.8B", "Q8_0", "Qwen3.5-0.8B-Q8_0.gguf", Hf + "Qwen3.5-0.8B-GGUF/resolve/main/Qwen3.5-0.8B-Q8_0.gguf",
            833592096, "37ae482d336108d23516fa35e8e0c4126688d81018b87178a18d752a1357814f", "Apache-2.0", 25, 12288, 822629632, 262144, false, "Ai_Tier_Tiny", "Ai_Purpose_Qwen35_08"),
        new("qwen3-4b", "Qwen3 4B", "4B", "Q4_K_M", "Qwen3-4B-Q4_K_M.gguf", Hf + "Qwen3-4B-GGUF/resolve/main/Qwen3-4B-Q4_K_M.gguf",
            2497280640, "ab27b9bfa375a178d6cba48f3ad892b94b7739659dcc7aae8058ce0ffed6b328", "Apache-2.0", 36, 147456, 2491323904, 40960, false, "Ai_Tier_Small", "Ai_Purpose_Qwen3_4"),
        new("gemma-4-e2b", "Gemma 4 E2B", "5.1B (2.3B)", "Q4_0", "gemma-4-E2B-it-Q4_0.gguf", Hf + "gemma-4-E2B-it-GGUF/resolve/main/gemma-4-E2B-it-Q4_0.gguf",
            2841481184, "8e30dff3ac4c8434c49a7036fa15564bdbb6044e42bf04550bf1a096ad7e6a52", "Apache-2.0", 35, 6144, 1504459872, 131072, false, "Ai_Tier_Small", "Ai_Purpose_Gemma4_E2"),
        new("qwen3.5-4b", "Qwen3.5 4B", "4B", "Q4_K_M", "Qwen_Qwen3.5-4B-Q4_K_M.gguf", "https://huggingface.co/bartowski/Qwen_Qwen3.5-4B-GGUF/resolve/main/Qwen_Qwen3.5-4B-Q4_K_M.gguf",
            3013027808, "13c16f426047e2de38cd075bdade4a7bcbc8c774384876f677740cda65f8a983", "Apache-2.0", 33, 32768, 3002058752, 262144, false, "Ai_Tier_Small", "Ai_Purpose_Qwen35_4"),
        new("gemma-4-e4b", "Gemma 4 E4B", "8B (4.5B)", "Q4_0", "gemma-4-E4B-it-Q4_0.gguf", Hf + "gemma-4-E4B-it-GGUF/resolve/main/gemma-4-E4B-it-Q4_0.gguf",
            4590807392, "a555b900214b477d8880e7832e0b8925e139b0159640036b09fe472b6f2097f2", "Apache-2.0", 42, 16384, 2989537600, 131072, false, "Ai_Tier_Small", "Ai_Purpose_Gemma4_E4"),
        new("qwen3-14b", "Qwen3 14B", "14B", "Q4_K_M", "Qwen3-14B-Q4_K_M.gguf", Hf + "Qwen3-14B-GGUF/resolve/main/Qwen3-14B-Q4_K_M.gguf",
            9001753376, "5ff1fe7a07aebc8d090682d01b17cf268a1b4680c6477050ce75a600aecb9efb", "Apache-2.0", 40, 163840, 8995793920, 40960, false, "Ai_Tier_Medium", "Ai_Purpose_Qwen3_14"),
        new("gpt-oss-20b", "gpt-oss 20B", "21B (3.6B)", "MXFP4", "gpt-oss-20b-MXFP4.gguf", Hf + "gpt-oss-20b-GGUF/resolve/main/gpt-oss-20b-MXFP4.gguf",
            12109566624, "27cd6c432c7672cb812a92f611cf3ba7bbc35928262bb1e1253ff4ee6ae35901", "Apache-2.0", 24, 24576, 3190031616, 131072, true, "Ai_Tier_Moe", "Ai_Purpose_GptOss20"),
        new("qwen3.8-27b", "Qwen3.8 27B", "27B", "Q4_K_M", "Qwen3.8-27B-Q4_K_M.gguf", Hf + "Qwen3.8-27B-GGUF/resolve/main/Qwen3.8-27B-Q4_K_M.gguf",
            18973870528, "c600de0300ae8a0eb3a6c0b8b5561b8b96f16bd2c863c2a66c42de29d391a747", "Apache-2.0", 64, 65536, 18962876416, 262144, false, "Ai_Tier_Large", "Ai_Purpose_Qwen38_27"),
        new("qwen3.6-35b-a3b", "Qwen3.6 35B-A3B", "35B (3B)", "Q4_K_M", "Qwen3.6-35B-A3B-Q4_K_M.gguf", Hf + "Qwen3.6-35B-A3B-GGUF/resolve/main/Qwen3.6-35B-A3B-Q4_K_M.gguf",
            20419565568, "671e47e0ec53c665d048b98c3ecbfd5236b5ca9c3e02ed19fc8f81f7b85140c7", "Apache-2.0", 40, 20480, 2855414272, 262144, true, "Ai_Tier_Moe", "Ai_Purpose_Qwen36_35"),
    ];

    public static AiModel? Find(string id) => Models.FirstOrDefault(m => m.Id == id);
}
