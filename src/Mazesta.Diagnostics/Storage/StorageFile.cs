using Microsoft.Win32.SafeHandles;
namespace Mazesta.Diagnostics.Storage;

/// <summary>
/// The scratch file of a storage test: uniquely named, created with <c>CreateNew</c> so an existing file is
/// never touched, deleted by the OS when the handle closes (also after a crash or cancel), and opened
/// unbuffered with write-through so reads and writes reach the device instead of the system cache.
/// Unbuffered I/O demands sector-aligned buffers, offsets and lengths - hence <see cref="Block"/> and
/// <c>NativeBlock</c>'s 4096-byte alignment.
/// </summary>
internal sealed class StorageFile : IDisposable
{
    public const int Block = 1 << 20, Sector = 4096;
    private const FileOptions NoBuffering = (FileOptions)0x20000000;
    private const long FreeSpaceMargin = 1L << 30;   // never fill a customer's disk to the brim

    private readonly SafeFileHandle _handle;
    public long Length { get; }

    private StorageFile(SafeFileHandle handle, long length) { _handle = handle; Length = length; }

    /// <summary>Throws <see cref="IOException"/> when the drive cannot hold the file plus the safety margin.</summary>
    public static StorageFile Create(string directory, long length)
    {
        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root)) throw new StorageUnavailableException($"Folder not found: {root}");
        long free = new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace;
        if (free < length + FreeSpaceMargin) throw new StorageUnavailableException($"{free >> 20} MiB free; the test file ({length >> 20} MiB) plus a 1 GiB margin does not fit.");
        string path = Path.Combine(root, $".mazesta-test-{Guid.NewGuid():N}.tmp");
        var handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, FileOptions.WriteThrough | FileOptions.DeleteOnClose | NoBuffering, length);
        return new(handle, length);
    }

    public void Write(ReadOnlySpan<byte> data, long offset) => RandomAccess.Write(_handle, data, offset);
    public int Read(Span<byte> buffer, long offset) => RandomAccess.Read(_handle, buffer, offset);
    public void Dispose() => _handle.Dispose();

    /// <summary>The fixed, ready drives as choices for the drive option; the system drive first.</summary>
    public static IReadOnlyList<OptionChoice> DriveChoices()
    {
        string system = Path.GetPathRoot(Environment.SystemDirectory) ?? "";
        return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .OrderByDescending(d => string.Equals(d.Name, system, StringComparison.OrdinalIgnoreCase)).ThenBy(d => d.Name)
            .Select(d => new OptionChoice(d.RootDirectory.FullName, $"{d.Name} {(string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : d.VolumeLabel + " ")}({d.AvailableFreeSpace >> 30} GB free)")).ToList();
    }

    /// <summary>The chosen drive/folder, or the first choice when none was chosen.</summary>
    public static string ResolveTarget(string chosen) => chosen.Length > 0 ? chosen : DriveChoices() is { Count: > 0 } c ? c[0].Value : throw new StorageUnavailableException("No fixed drive is available.");
}

/// <summary>The test cannot run here at all (no such folder, no room, no drive) - reported as Unsupported, not as a failing drive.</summary>
internal sealed class StorageUnavailableException(string message) : IOException(message);
