using System.Net.Http; using System.Security.Cryptography; using System.Text.Json;
namespace Mazesta.Persistence.Updates;

/// <summary>The manifest could not be trusted or read: not signed by the shop's key, damaged, or of a format this version does not know.</summary>
public sealed class UpdateRejectedException(string message) : Exception(message);

/// <summary>
/// Reads the signed manifest from the update folder on the shop's site and downloads what it names. Only files under that folder are fetched, and
/// every file is written to a temporary name, checked (size and SHA-256 against the signed manifest) and only then moved into place.
/// </summary>
public sealed class UpdateClient(Uri folder, string publicKey, HttpClient http)
{
    public Uri Folder => folder;

    public async Task<UpdateManifest> CheckAsync(CancellationToken ct)
    {
        // The address changes with every check, so no cache on the way (the site's or a proxy's) hands back yesterday's manifest.
        string fresh = "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var json = await http.GetByteArrayAsync(new Uri(folder, UpdateManifest.FileName + fresh), ct).ConfigureAwait(false);
        var sig = await http.GetStringAsync(new Uri(folder, UpdateManifest.SignatureName + fresh), ct).ConfigureAwait(false);
        if (!UpdateSigning.Verify(json, sig, publicKey)) throw new UpdateRejectedException("The update manifest is not signed by the shop's key.");
        try { return UpdateManifest.Parse(json); }
        catch (Exception e) when (e is JsonException or InvalidDataException) { throw new UpdateRejectedException(e.Message); }
    }

    /// <summary>Downloads one file to <paramref name="target"/>; <paramref name="progress"/> gets the fraction done. Throws, leaving no file, when it does not match.</summary>
    public async Task DownloadAsync(UpdateFile file, string target, Action<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string tmp = target + ".part";
        try
        {
            using (var res = await http.GetAsync(new Uri(folder, file.File), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                res.EnsureSuccessStatusCode();
                await using var src = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1 << 16]; long done = 0; int n;
                while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    done += n;
                    if (done > file.Size) throw new InvalidDataException($"{file.File} is larger than the manifest says.");
                    sha.AppendData(buffer, 0, n); await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    progress?.Invoke((double)done / file.Size);
                }
                if (done != file.Size) throw new InvalidDataException($"{file.File} is {done} bytes, the manifest says {file.Size}.");
                if (Convert.ToHexStringLower(sha.GetHashAndReset()) != file.Sha256) throw new InvalidDataException($"{file.File} does not match its SHA-256 in the manifest.");
            }
            File.Move(tmp, target, overwrite: true);
        }
        finally { try { File.Delete(tmp); } catch (IOException) { } }
    }
}

/// <summary>
/// The data files of the manifest (the benchmark comparison lists) kept in <c>Data/benchdb</c>: only files whose hash changed are downloaded, files
/// the manifest no longer lists are removed. <c>files.json</c> there records what is on disk, so an interrupted sync simply resumes next time.
/// </summary>
public static class DataSync
{
    public const string Prefix = "benchdb/";
    private const string StateFile = "files.json";

    public sealed record Result(int Downloaded, int Removed, int Total);

    public static async Task<Result> SyncAsync(UpdateClient client, UpdateManifest manifest, string folder, CancellationToken ct)
    {
        var state = Read(folder);
        var wanted = manifest.Data.Where(f => f.File.StartsWith(Prefix, StringComparison.Ordinal)).ToDictionary(f => f.File[Prefix.Length..]);
        int downloaded = 0, removed = 0;
        foreach (var (name, file) in wanted)
        {
            string path = Path.Combine(folder, name);
            if (state.GetValueOrDefault(name) == file.Sha256 && File.Exists(path)) continue;
            await client.DownloadAsync(file, path, null, ct).ConfigureAwait(false);
            state[name] = file.Sha256; downloaded++;
            Write(folder, state);
        }
        foreach (var name in state.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
        {
            try { File.Delete(Path.Combine(folder, name)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            state.Remove(name); removed++;
        }
        Write(folder, state);
        return new Result(downloaded, removed, wanted.Count);
    }

    /// <summary>How many lists are on disk (0 when none was ever downloaded).</summary>
    public static int Count(string folder) => Read(folder).Count;

    private static Dictionary<string, string> Read(string folder)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(folder, StateFile))) ?? []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    private static void Write(string folder, Dictionary<string, string> state)
    {
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, StateFile), tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state)); File.Move(tmp, file, overwrite: true);
    }
}
