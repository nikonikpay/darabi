using System.Diagnostics; using System.Runtime.InteropServices; using System.Security.Cryptography.X509Certificates;
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
        if (Trust.Verify(file) is { } error) return error;
        try
        {
#pragma warning disable SYSLIB0057   // only the signer's name is read here; the trust itself is WinVerifyTrust's answer above
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            return cert.GetNameInfo(X509NameType.SimpleName, false).StartsWith("NVIDIA Corporation", StringComparison.Ordinal) ? null : "signed by " + cert.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch (System.Security.Cryptography.CryptographicException e) { return e.Message; }
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

    /// <summary>WinVerifyTrust with the Authenticode policy: the file's signature is whole and chains to a root Windows trusts.</summary>
    private static class Trust
    {
        private static readonly Guid Action = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileInfo { public int Size; public string Path; public IntPtr Handle; public IntPtr Subject; }

        [StructLayout(LayoutKind.Sequential)]
        private struct Data
        {
            public int Size; public IntPtr Policy, Sip; public int UiChoice, Revocation, UnionChoice; public IntPtr File; public int StateAction; public IntPtr State;
            [MarshalAs(UnmanagedType.LPWStr)] public string? Url; public int ProvFlags, UiContext; public IntPtr Signature;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref Data data);

        public static string? Verify(string path)
        {
            var info = new FileInfo { Size = Marshal.SizeOf<FileInfo>(), Path = path };
            IntPtr pInfo = Marshal.AllocHGlobal(info.Size);
            try
            {
                Marshal.StructureToPtr(info, pInfo, false);
                // No UI, whole-chain revocation check, a file subject, verify then close the state.
                var data = new Data { Size = Marshal.SizeOf<Data>(), UiChoice = 2, Revocation = 0, UnionChoice = 1, File = pInfo, StateAction = 1 };
                int result = WinVerifyTrust(new IntPtr(-1), Action, ref data);
                data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), Action, ref data);
                return result == 0 ? null : $"Windows does not trust the signature (0x{result:X8})";
            }
            finally { Marshal.DestroyStructure<FileInfo>(pInfo); Marshal.FreeHGlobal(pInfo); }
        }
    }
}
