using System.Diagnostics; using System.IO.Compression; using System.Net.Http; using System.Security.Cryptography;
using Mazesta.Core.Drivers;
namespace Mazesta.Diagnostics.Drivers;

/// <summary>
/// The motherboard's driver packages from their makers (see <see cref="BoardDrivers"/>), downloaded into Data/drivers/board and installed by the
/// maker's own installer, which shows its own window (each maker's installer takes different silent switches, and some none; the user sees and
/// finishes it). Only the makers' own download hosts are accepted. A file is run only when its hash is the one the maker publishes (ASUS's
/// SHA-256, Intel's SHA-1) or, where none is published (AMD, ASUS's older files), when Windows trusts its signature (and, for AMD, the signer is AMD).
/// </summary>
public sealed class BoardDriverService(HttpClient http, string downloadDir)
{
    /// <summary>amd.com answers only a browser: its pages and its download host refuse other clients, and the file only to a visitor of its page.</summary>
    private const string BrowserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
    private static readonly string[] Hosts = ["dlcdnets.asus.com", "drivers.amd.com", "downloadmirror.intel.com"];

    public async Task<IReadOnlyList<BoardPackage>?> AsusAsync(string model, CancellationToken ct)
    {
        var drivers = BoardDrivers.ParseAsus(await http.GetStringAsync(BoardDrivers.AsusDriversUrl(model), ct).ConfigureAwait(false));
        if (drivers is null) return null;
        var bios = BoardDrivers.ParseAsus(await http.GetStringAsync(BoardDrivers.AsusBiosUrl(model), ct).ConfigureAwait(false), bios: true) ?? [];
        return [.. drivers, .. bios];
    }

    public async Task<BoardPackage?> AmdChipsetAsync(string? boardModel, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, BoardDrivers.AmdChipsetPage(boardModel));
        req.Headers.UserAgent.ParseAdd(BrowserAgent); req.Headers.AcceptLanguage.ParseAdd("en-US");
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return BoardDrivers.ParseAmdChipset(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    /// <summary>Intel's packages from the zip the Driver &amp; Support Assistant reads (software-configurations.json inside it).</summary>
    public async Task<IReadOnlyList<BoardPackage>> IntelAsync(CancellationToken ct)
    {
        await using var stream = new MemoryStream(await http.GetByteArrayAsync(BoardDrivers.IntelDataUrl, ct).ConfigureAwait(false));
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.GetEntry("software-configurations.json") ?? throw new InvalidDataException("Intel's data has no software-configurations.json");
        using var reader = new StreamReader(entry.Open());
        return BoardDrivers.ParseIntel(await reader.ReadToEndAsync(ct).ConfigureAwait(false));
    }

    /// <summary>Downloads the package (kept when already whole and its hash matches) and checks the maker's hash; returns the file.</summary>
    public async Task<string> DownloadAsync(BoardPackage p, IProgress<double>? progress, CancellationToken ct)
    {
        var uri = new Uri(p.Url);
        if (uri.Scheme != "https" || !Hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("not a maker's download address: " + p.Url);
        Directory.CreateDirectory(downloadDir);
        string file = Path.Combine(downloadDir, Path.GetFileName(uri.LocalPath)), part = file + ".part";
        if (File.Exists(file) && HashError(p, file) is null) { progress?.Report(1); return file; }
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        if (uri.Host == "drivers.amd.com") { req.Headers.UserAgent.ParseAdd(BrowserAgent); req.Headers.Referrer = new Uri(BoardDrivers.AmdChipsetPage(null)); }
        using var response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } final && !Hosts.Contains(final.Host, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("the download was sent elsewhere: " + final);
        long? total = response.Content.Headers.ContentLength;
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
            if (HashError(p, part) is { } bad) throw new InvalidOperationException(bad);
            File.Move(part, file, overwrite: true);
            return file;
        }
        finally { if (File.Exists(part)) File.Delete(part); }
    }

    /// <summary>Why the file is not the maker's (its hash differs from the published one), or null. A package with no published hash passes here.</summary>
    public static string? HashError(BoardPackage p, string file)
    {
        if (p.Sha256 is null && p.Sha1 is null) return null;
        using var s = File.OpenRead(file);
        string got = Convert.ToHexString(p.Sha256 is not null ? SHA256.HashData(s) : SHA1.HashData(s));
        string want = p.Sha256 ?? p.Sha1!;
        return got.Equals(want, StringComparison.OrdinalIgnoreCase) ? null : $"the file's hash {got} is not the one the maker publishes ({want})";
    }

    /// <summary>
    /// The installer to run: the downloaded program itself, or, for a zip, the maker's setup inside it (ASUS's AsusSetup.exe, else the shallowest
    /// Setup.exe), unpacked beside it. Null when the zip holds no installer (the folder is then opened for the user).
    /// </summary>
    public static string? Installer(string file, out string folder)
    {
        folder = Path.GetDirectoryName(file)!;
        if (!file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return file;
        folder = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file));
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        ZipFile.ExtractToDirectory(file, folder);   // .NET refuses entries that would land outside the folder
        var exes = Directory.EnumerateFiles(folder, "*.exe", SearchOption.AllDirectories).OrderBy(f => f.Count(c => c == Path.DirectorySeparatorChar)).ToList();
        return exes.FirstOrDefault(f => Path.GetFileName(f).Equals("AsusSetup.exe", StringComparison.OrdinalIgnoreCase))
            ?? exes.FirstOrDefault(f => Path.GetFileName(f).Equals("Setup.exe", StringComparison.OrdinalIgnoreCase))
            ?? (exes.Count == 1 ? exes[0] : null);
    }

    /// <summary>
    /// Why the installer may not run, or null. A package whose hash the maker publishes was proven by it (see <see cref="HashError"/>); one without
    /// must carry a signature Windows trusts, and AMD's must be AMD's.
    /// </summary>
    public static string? SignatureError(BoardPackage p, string exe)
    {
        if (p.Sha256 is not null || p.Sha1 is not null) return null;
        if (Authenticode.Verify(exe) is { } error) return error;
        string? who = Authenticode.Signer(exe);
        return p.Source == "amd" && who?.StartsWith("Advanced Micro Devices", StringComparison.Ordinal) != true ? "signed by " + (who ?? "nobody") : null;
    }

    /// <summary>Runs the maker's installer with its own window and waits for it; returns its exit code.</summary>
    public static async Task<int> RunAsync(string exe, CancellationToken ct)
    {
        using var proc = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! })
            ?? throw new InvalidOperationException("the installer did not start");
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return proc.ExitCode;
    }
}
