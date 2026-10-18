using System.IO.MemoryMappedFiles; using System.Text.Json; using Mazesta.Core.Health;
namespace Mazesta.Persistence;

/// <summary>
/// The app's latest temperatures and loads, handed to the tray through a small named block of memory: while the app runs it reads the hardware
/// anyway, so the tray closes its own sensors and reads these instead (one reader of the hardware, not two). Both run elevated in one session.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class HealthShare
{
    private const string Name = @"Local\Mazesta.Health"; private const int Size = 2048;
    private sealed record Shared(DateTimeOffset Time, HealthSample Sample);
    private static MemoryMappedFile? s_file;

    public static void Publish(HealthSample sample)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new Shared(DateTimeOffset.UtcNow, sample));
        if (json.Length > Size - 4) return;
        s_file ??= MemoryMappedFile.CreateOrOpen(Name, Size);
        using var view = s_file.CreateViewAccessor();
        view.Write(0, 0); view.WriteArray(4, json, 0, json.Length); view.Write(0, json.Length);   // the length last: a reader mid-write sees 0 or a torn text, and skips
    }

    /// <summary>The app's sample when one at most <paramref name="maxAge"/> old is there; null otherwise (no app, or it has read nothing lately).</summary>
    public static HealthSample? Read(TimeSpan maxAge)
    {
        try
        {
            using var file = MemoryMappedFile.OpenExisting(Name, MemoryMappedFileRights.Read);
            using var view = file.CreateViewAccessor(0, Size, MemoryMappedFileAccess.Read);
            int n = view.ReadInt32(0); if (n <= 0 || n > Size - 4) return null;
            var bytes = new byte[n]; view.ReadArray(4, bytes, 0, n);
            var shared = JsonSerializer.Deserialize<Shared>(bytes);
            return shared is not null && DateTimeOffset.UtcNow - shared.Time <= maxAge ? shared.Sample : null;
        }
        catch (Exception e) when (e is FileNotFoundException or IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
}
