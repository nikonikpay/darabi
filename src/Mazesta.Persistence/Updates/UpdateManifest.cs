using System.Security.Cryptography; using System.Text.Json; using System.Text.Json.Serialization; using System.Text.RegularExpressions;
namespace Mazesta.Persistence.Updates;

/// <summary>A file the site offers: its path under the update folder, its exact size and its SHA-256. A download that differs in either is thrown away.</summary>
public sealed record UpdateFile(string File, long Size, string Sha256);

/// <summary>The app's newest release: its version, the zip (the published folder, without Data), and what changed, in both languages.</summary>
public sealed record AppRelease(string Version, string File, long Size, string Sha256, DateTimeOffset Date, string? NotesFa = null, string? NotesEn = null)
{
    [JsonIgnore] public UpdateFile Package => new(File, Size, Sha256);
}

/// <summary>
/// <c>update.json</c> on the shop's site: the newest app release and the data files (the benchmark comparison lists). It is signed as a whole
/// (<c>update.json.sig</c>, ECDSA P-256 over its exact bytes) with the shop's private key, which never leaves the shop; every file it names carries
/// its SHA-256. So a changed site, or a file changed on the way, can at most withhold an update - it cannot give the app something to run.
/// </summary>
public sealed partial record UpdateManifest(int Format, DateTimeOffset Published, AppRelease? App, IReadOnlyList<UpdateFile> Data)
{
    public const int CurrentFormat = 1;
    public const string FileName = "update.json", SignatureName = "update.json.sig";
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Reads a manifest whose signature is already checked; throws <see cref="InvalidDataException"/> when a file name or hash is not plain.</summary>
    public static UpdateManifest Parse(byte[] json)
    {
        var m = JsonSerializer.Deserialize<UpdateManifest>(json, Json) ?? throw new InvalidDataException("Empty manifest.");
        if (m.Format != CurrentFormat) throw new InvalidDataException($"Manifest format {m.Format} is not understood by this version.");
        foreach (var f in (m.App is null ? [] : new[] { m.App.Package }).Concat(m.Data ?? []))
            if (!SafePath().IsMatch(f.File) || f.File.Contains("..", StringComparison.Ordinal) || f.Size <= 0 || !Hash().IsMatch(f.Sha256))
                throw new InvalidDataException($"Manifest entry {f.File} is not valid.");
        if (m.App is not null && !System.Version.TryParse(m.App.Version, out _)) throw new InvalidDataException($"Version {m.App.Version} is not valid.");
        return m with { Data = m.Data ?? [] };
    }

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    /// <summary>True when the release is newer than <paramref name="current"/> (compared as versions, not as text: 0.10 is after 0.9).</summary>
    public bool IsNewer(string current) => App is not null && System.Version.TryParse(App.Version, out var offered) && (!System.Version.TryParse(current, out var mine) || offered > mine);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._@=-]*(/[A-Za-z0-9][A-Za-z0-9._@=-]*)?$")] private static partial Regex SafePath();
    [GeneratedRegex("^[0-9a-f]{64}$")] private static partial Regex Hash();
}

/// <summary>ECDSA P-256 with SHA-256, the signature as the 64 raw bytes (r‖s) in Base64. The app holds only the public key.</summary>
public static class UpdateSigning
{
    public static bool Verify(byte[] data, string signatureBase64, string publicKeyBase64)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return key.VerifyData(data, Convert.FromBase64String(signatureBase64.Trim()), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception e) when (e is FormatException or CryptographicException) { return false; }
    }

    public static string Sign(byte[] data, string privateKeyPem)
    {
        using var key = ECDsa.Create(); key.ImportFromPem(privateKeyPem);
        return Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    /// <summary>A new key pair: the private key as PKCS#8 PEM (kept by the shop, never committed) and the public key as Base64 for the app.</summary>
    public static (string PrivatePem, string PublicBase64) NewKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

    public static string Sha256(string file) { using var s = File.OpenRead(file); return Convert.ToHexStringLower(SHA256.HashData(s)); }
}
