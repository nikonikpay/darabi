using System.Diagnostics;
using Mazesta.Core.Drivers;
namespace Mazesta.Diagnostics.Drivers;

/// <summary>
/// An NVIDIA card's drivers from NVIDIA itself: the product lookup and the releases of both lines (see <see cref="NvidiaDrivers"/>), the download
/// into Data/drivers, and the install. The file is run only when Windows trusts its Authenticode signature and the signer is NVIDIA Corporation
/// (NVIDIA publishes no hashes; its signature is what the file carries). The installer runs silently ("-s -noreboot"): the screen goes black for a
/// moment while the driver changes, and Windows may want a restart, which is told and never done here. "-clean" is NVIDIA's clean install
/// (the driver's settings go back to their defaults).
/// </summary>
public sealed class NvidiaDriverService(HttpClient http, string downloadDir)
{
    private string? _seriesXml, _productsXml;

    public async Task<NvidiaProduct?> FindAsync(string gpuName, CancellationToken ct)
    {
        _seriesXml ??= await http.GetStringAsync(NvidiaDrivers.SeriesUrl, ct).ConfigureAwait(false);
        _productsXml ??= await http.GetStringAsync(NvidiaDrivers.ProductsUrl, ct).ConfigureAwait(false);
        return NvidiaDrivers.FindProduct(gpuName, _seriesXml, _productsXml);
    }

    public async Task<IReadOnlyList<NvidiaRelease>> ReleasesAsync(NvidiaProduct p, bool studio, CancellationToken ct)
        => NvidiaDrivers.ParseReleases(await http.GetStringAsync(NvidiaDrivers.LookupUrl(p, studio), ct).ConfigureAwait(false), studio);

    /// <summary>Downloads the release (kept if already complete) and returns the file; only NVIDIA's own download host is accepted.</summary>
    public async Task<string> DownloadAsync(NvidiaRelease r, IProgress<double>? progress, CancellationToken ct)
    {
        var uri = new Uri(r.Url);
        if (uri.Scheme != "https" || !uri.Host.EndsWith(".nvidia.com", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("not an NVIDIA download address: " + r.Url);
        Directory.CreateDirectory(downloadDir);
        string file = Path.Combine(downloadDir, Path.GetFileName(uri.LocalPath)), part = file + ".part";
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        if (File.Exists(file) && total is { } t && new FileInfo(file).Length == t) { progress?.Report(1); return file; }
        try
        {
            await using (var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
            {
                var buffer = new byte[1 << 20]; long done = 0; int n;
                while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false); done += n;
                    if (total is > 0) progress?.Report((double)done / total.Value);
                }
                if (total is { } want && done != want) throw new IOException($"the download stopped at {done} of {want} bytes");
            }
            File.Move(part, file, overwrite: true);
            return file;
        }
        finally { if (File.Exists(part)) File.Delete(part); }
    }

    /// <summary>Windows trusts the file's signature, and the certificate it was signed with is NVIDIA Corporation's; otherwise why not.</summary>
    public static string? CheckSignature(string file)
    {
        if (Authenticode.Verify(file) is { } error) return error;
        return Authenticode.Signer(file) is { } who && who.StartsWith("NVIDIA Corporation", StringComparison.Ordinal) ? null : "signed by " + (Authenticode.Signer(file) ?? "nobody");
    }

    /// <summary>Runs NVIDIA's installer silently and waits for it; returns its exit code (0 is success; NVIDIA's other codes mean a restart is
    /// due or the install failed, told as they are).</summary>
    public static async Task<int> InstallAsync(string file, bool clean, CancellationToken ct)
    {
        if (CheckSignature(file) is { } why) throw new InvalidOperationException("the driver's signature is not NVIDIA's: " + why);
        using var p = Process.Start(new ProcessStartInfo(file, "-s -noreboot" + (clean ? " -clean" : "")) { UseShellExecute = false, CreateNoWindow = true })
            ?? throw new InvalidOperationException("the installer did not start");
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        return p.ExitCode;
    }
}
