using System.Net; using System.Net.Http; using System.Security.Cryptography; using Xunit; using Mazesta.Core.Ai; using Mazesta.Diagnostics.Ai;
namespace Mazesta.Diagnostics.Tests;

public class AiTests
{
    /// <summary>llama-bench b11265's JSON on the owner's RTX 3090 with Qwen3 4B Q4_K_M (trimmed to the fields read).</summary>
    private const string BenchJson = """
        load_backend: loaded Vulkan backend
        [
          { "build_number": 11265, "backends": "Vulkan", "devices": "Vulkan0", "model_type": "qwen3 4B Q4_K - Medium", "n_threads": 16,
            "n_prompt": 512, "n_gen": 0, "avg_ts": 6881.509183, "stddev_ts": 147.937126 },
          { "build_number": 11265, "backends": "Vulkan", "devices": "Vulkan0", "model_type": "qwen3 4B Q4_K - Medium", "n_threads": 16,
            "n_prompt": 0, "n_gen": 128, "avg_ts": 194.766477, "stddev_ts": 1.825112 }
        ]
        """;

    [Fact] public void Reads_prompt_and_generation_speed_from_llama_bench_json()
    {
        var r = LlamaBenchmark.Parse(BenchJson)!;
        Assert.Equal(6881.5, r.PromptTokensPerSecond!.Value, 1); Assert.Equal(194.77, r.GenerationTokensPerSecond!.Value, 2);
        Assert.Equal("Vulkan", r.Backend); Assert.Equal("Vulkan0", r.Devices); Assert.Equal(16, r.Threads); Assert.Equal(11265, r.Build);
        Assert.Null(LlamaBenchmark.Parse("error: failed to load model")); Assert.Null(LlamaBenchmark.Parse("[ { broken"));
    }

    [Fact] public void Progress_counts_the_warm_up_as_a_run()
    {
        Assert.Equal(1 / 8.0, LlamaBenchmark.Progress("llama-bench: benchmark 1/2: warmup prompt run"));
        Assert.Equal(2 / 8.0, LlamaBenchmark.Progress("llama-bench: benchmark 1/2: prompt run 1/3"));
        Assert.Equal(1.0, LlamaBenchmark.Progress("llama-bench: benchmark 2/2: generation run 3/3"));
        Assert.Null(LlamaBenchmark.Progress("llama-bench: benchmark 2/2: starting"));
    }

    [Fact] public void Lists_the_vulkan_devices_llama_cpp_sees()
    {
        var d = LlamaBenchmark.Devices("Available devices:\r\n  Vulkan0: NVIDIA GeForce RTX 3090 (24539 MiB, 23753 MiB free)\r\n  Vulkan1: AMD Radeon(TM) Graphics (512 MiB, 400 MiB free)\r\n");
        Assert.Equal([("Vulkan0", "NVIDIA GeForce RTX 3090"), ("Vulkan1", "AMD Radeon(TM) Graphics")], d);
        Assert.Empty(LlamaBenchmark.Devices("Available devices:\n"));
    }

    /// <summary>Serves one file, honouring a byte range; can cut the body short to play a broken connection.</summary>
    private sealed class FileServer(byte[] body, int? cutAt = null) : HttpMessageHandler
    {
        public List<long?> Ranges { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            long from = request.Headers.Range?.Ranges.First().From ?? 0; Ranges.Add(request.Headers.Range?.Ranges.First().From);
            var part = body[(int)from..(cutAt is { } c && c > from ? c : body.Length)];
            return Task.FromResult(new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(part) });
        }
    }

    private static AiModel Model(byte[] body, string? sha = null) => AiCatalog.Models[0] with { File = "m.gguf", Url = "https://example.test/m.gguf", Bytes = body.Length, Sha256 = sha ?? Convert.ToHexStringLower(SHA256.HashData(body)) };

    [Fact] public async Task A_download_continues_where_it_stopped_and_becomes_the_file_only_when_its_checksum_matches()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mazesta-ai-" + Guid.NewGuid().ToString("N"));
        try
        {
            var body = new byte[300_000]; new Random(3).NextBytes(body); var model = Model(body);
            var broken = new AiFiles(dir, new HttpClient(new FileServer(body, cutAt: 120_000)));
            await Assert.ThrowsAsync<IOException>(() => broken.GetModelAsync(model, null, CancellationToken.None));
            Assert.False(broken.HasModel(model)); Assert.Equal(120_000, broken.PartialBytes(model));

            var server = new FileServer(body); var files = new AiFiles(dir, new HttpClient(server));
            await files.GetModelAsync(model, null, CancellationToken.None);
            Assert.Equal([120_000L], server.Ranges); Assert.True(files.HasModel(model)); Assert.Equal(0, files.PartialBytes(model));
            Assert.Equal(body, File.ReadAllBytes(files.ModelPath(model)));
            files.DeleteModel(model); Assert.False(files.HasModel(model));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact] public async Task A_file_that_does_not_match_its_checksum_is_deleted_never_kept()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mazesta-ai-" + Guid.NewGuid().ToString("N"));
        try
        {
            var body = new byte[50_000]; var model = Model(body, sha: new string('0', 64)); var files = new AiFiles(dir, new HttpClient(new FileServer(body)));
            await Assert.ThrowsAsync<InvalidDataException>(() => files.GetModelAsync(model, null, CancellationToken.None));
            Assert.False(files.HasModel(model)); Assert.Equal(0, files.PartialBytes(model));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact] public void Results_are_kept_per_model_and_device_across_restarts()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mazesta-ai-" + Guid.NewGuid().ToString("N"));
        try
        {
            new AiResults(dir).Set("qwen3-4b", "gpu", new(DateTimeOffset.UnixEpoch, 6881.5, 194.8, "x"));
            var back = new AiResults(dir);
            Assert.Equal(194.8, back.Get("qwen3-4b", "gpu")!.GenerationTokensPerSecond); Assert.Null(back.Get("qwen3-4b", "cpu"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
