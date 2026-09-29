using System.IO.Compression; using System.Security.Cryptography; using Mazesta.Core.Ai;
namespace Mazesta.Diagnostics.Ai;

/// <summary>A download's progress: bytes so far of the whole, and the speed over the last moments.</summary>
public readonly record struct DownloadProgress(long Done, long Total, double BytesPerSecond);

/// <summary>
/// Where the AI benchmark keeps what it downloads - <c>Data/ai</c>: the runtime unpacked under <c>runtime/&lt;build&gt;</c>, the models under
/// <c>models</c> - and how it gets them. A file is written to a <c>.part</c> file first, continued where it stopped after a cancel or a broken
/// connection (HTTP range), and becomes the real file only after its size and SHA-256 match the catalog: a file that is there is a verified one.
/// Nothing is fetched unless the user asks for it on the page.
/// </summary>
public sealed class AiFiles(string dataRoot, HttpClient http)
{
    public string Root { get; } = Path.Combine(dataRoot, "ai");
    public string ModelsDir => Path.Combine(Root, "models");
    public string RuntimeDir => Path.Combine(Root, "runtime", AiCatalog.Runtime.Build);
    public string BenchExe => Path.Combine(RuntimeDir, "llama-bench.exe");

    public string ModelPath(AiModel m) => Path.Combine(ModelsDir, m.File);
    public bool HasModel(AiModel m) => new FileInfo(ModelPath(m)) is { Exists: true } f && f.Length == m.Bytes;
    public bool HasRuntime => File.Exists(BenchExe);
    /// <summary>What a stopped download already has (it continues from there).</summary>
    public long PartialBytes(AiModel m) => new FileInfo(ModelPath(m) + ".part") is { Exists: true } f ? f.Length : 0;

    public Task GetModelAsync(AiModel m, IProgress<DownloadProgress>? progress, CancellationToken ct)
        => DownloadAsync(m.Url, ModelPath(m), m.Bytes, m.Sha256, progress, ct);

    public async Task GetRuntimeAsync(IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var r = AiCatalog.Runtime; string zip = Path.Combine(Root, "runtime", r.File);
        await DownloadAsync(r.Url, zip, r.Bytes, r.Sha256, progress, ct).ConfigureAwait(false);
        string temp = RuntimeDir + ".unpack";
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        ZipFile.ExtractToDirectory(zip, temp);
        if (!File.Exists(Path.Combine(temp, "llama-bench.exe"))) throw new InvalidDataException("The llama.cpp package has no llama-bench.exe.");
        if (Directory.Exists(RuntimeDir)) Directory.Delete(RuntimeDir, true);
        Directory.Move(temp, RuntimeDir);
        File.Delete(zip);
    }

    /// <summary>Deletes a model and anything left of its download.</summary>
    public void DeleteModel(AiModel m) { foreach (var f in new[] { ModelPath(m), ModelPath(m) + ".part" }) if (File.Exists(f)) File.Delete(f); }

    private async Task DownloadAsync(string url, string target, long size, string sha256, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string part = target + ".part";
        long have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (have > size) { File.Delete(part); have = 0; }
        if (have < size)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (have > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (have > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent) have = 0;   // the server sent the whole file again
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var file = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
            var buffer = new byte[1 << 20]; long done = have, mark = have; var clock = System.Diagnostics.Stopwatch.StartNew(); double speed = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                if (done + n > size) throw new InvalidDataException($"{Path.GetFileName(target)} is larger than expected ({size} bytes).");
                await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false); done += n;
                if (clock.ElapsedMilliseconds >= 500) { speed = (done - mark) / clock.Elapsed.TotalSeconds; mark = done; clock.Restart(); progress?.Report(new(done, size, speed)); }
            }
            progress?.Report(new(done, size, speed));
        }
        if (new FileInfo(part).Length != size) throw new IOException($"{Path.GetFileName(target)}: the download stopped early; try again to continue it.");
        string actual;
        await using (var check = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true))
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(check, ct).ConfigureAwait(false));
        if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            throw new InvalidDataException($"{Path.GetFileName(target)} does not match its published checksum and was deleted.");
        }
        File.Move(part, target, overwrite: true);
    }
}
