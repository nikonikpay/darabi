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
    private const int MinMb = 16, MaxMb = 65536;   // 64 GiB: a sustained write has to outlast a large SLC cache

    private readonly SafeFileHandle _handle;
    public long Length { get; }
    /// <summary>The folder the file is in, for a test that also works through the file system there.</summary>
    public string Folder { get; }

    private StorageFile(SafeFileHandle handle, long length, string folder) { _handle = handle; Length = length; Folder = folder; }

    /// <summary>Why a file-size option cannot be used, or null when it can.</summary>
    public static string? CheckSizeMb(int mb) => mb is < MinMb or > MaxMb ? $"The test file must be between {MinMb} MiB and {MaxMb >> 10} GiB." : null;
    /// <summary>The file length for a size option, rounded down to whole blocks.</summary>
    public static long LengthOf(int mb) => (long)mb << 20 & ~(long)(Block - 1);

    /// <summary>Throws <see cref="IOException"/> when the drive cannot hold the file plus the safety margin. <paramref name="asynchronous"/>
    /// allows several requests in flight. Benchmarks turn <paramref name="writeThrough"/> off, as disk benchmarks do: it makes every write a
    /// forced-unit-access flush, which measures the flush path rather than the drive's speed; the tests keep it, they verify the media.</summary>
    public static StorageFile Create(string directory, long length, bool asynchronous = false, bool writeThrough = true)
    {
        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root)) throw new StorageUnavailableException($"Folder not found: {root}");
        long free = new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace;
        if (free < length + FreeSpaceMargin) throw new StorageUnavailableException($"{free >> 20} MiB free; the test file ({length >> 20} MiB) plus a 1 GiB margin does not fit.");
        string path = Path.Combine(root, $".mazesta-test-{Guid.NewGuid():N}.tmp");
        var handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, (asynchronous ? FileOptions.Asynchronous : 0) | (writeThrough ? FileOptions.WriteThrough : 0) | FileOptions.DeleteOnClose | NoBuffering, length);
        return new(handle, length, root);
    }

    /// <summary>An existing file opened unbuffered for reading, so what is read comes from the drive, not from Windows' cache.</summary>
    public static StorageFile OpenRead(string path) { var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, NoBuffering); return new(h, RandomAccess.GetLength(h), Path.GetDirectoryName(path)!); }

    public void Write(ReadOnlySpan<byte> data, long offset) => RandomAccess.Write(_handle, data, offset);
    public int Read(Span<byte> buffer, long offset) => RandomAccess.Read(_handle, buffer, offset);
    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, long offset, CancellationToken ct) => RandomAccess.WriteAsync(_handle, data, offset, ct);
    public ValueTask<int> ReadAsync(Memory<byte> buffer, long offset, CancellationToken ct) => RandomAccess.ReadAsync(_handle, buffer, offset, ct);
    public void Dispose() => _handle.Dispose();

    /// <summary>The fixed, ready drives as choices for the drive option; the system drive first.</summary>
    public static IReadOnlyList<OptionChoice> DriveChoices()
    {
        string system = Path.GetPathRoot(Environment.SystemDirectory) ?? "";
        return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .OrderByDescending(d => string.Equals(d.Name, system, StringComparison.OrdinalIgnoreCase)).ThenBy(d => d.Name)
            .Select(d => new OptionChoice(d.RootDirectory.FullName, $"{d.Name} {(string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : d.VolumeLabel + " ")}({d.AvailableFreeSpace >> 30} GB free)")).ToList();
    }

    /// <summary>The drive option's value for "every fixed drive, one after the other".</summary>
    public const string AllDrives = "*";

    /// <summary>The drive choices of a test that can go through every drive: the list, with "All drives" first when there are two or more.</summary>
    public static IReadOnlyList<OptionChoice> DriveChoicesWithAll() => DriveChoices() is { Count: > 1 } c ? [new(AllDrives, "Test_Option_AllDrives", true, "all fixed drives"), .. c] : DriveChoices();

    /// <summary>The folders a run goes through: every fixed drive for "All drives", else the one chosen (the first when none).</summary>
    public static IReadOnlyList<string> Targets(string chosen) => chosen == AllDrives ? [.. DriveChoices().Select(c => c.Value)] : [ResolveTarget(chosen)];

    /// <summary>The chosen drive/folder, or the first choice when none was chosen.</summary>
    public static string ResolveTarget(string chosen) => chosen.Length > 0 ? chosen : DriveChoices() is { Count: > 0 } c ? c[0].Value : throw new StorageUnavailableException("No fixed drive is available.");
}

/// <summary>The test cannot run here at all (no such folder, no room, no drive) - reported as Unsupported, not as a failing drive.</summary>
internal sealed class StorageUnavailableException(string message) : IOException(message);
