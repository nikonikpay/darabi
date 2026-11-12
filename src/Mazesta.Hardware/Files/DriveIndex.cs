using System.Buffers.Binary; using System.Runtime.InteropServices; using Microsoft.Win32.SafeHandles; using Mazesta.Core.Search;
namespace Mazesta.Hardware.Files;

/// <summary>
/// An NTFS drive's names read straight from its master file table, as fast file search tools do (no folder is walked): the table's records are
/// read from the disk in large pieces and every name of every file taken from them - a file with several hard links (Windows' own files in
/// System32 and WinSxS) is found under each. After that the drive is kept current from its change journal whenever <see cref="CatchUp"/>
/// is asked; nothing runs in between. Needs an elevated process. The journal is only read (Windows keeps one; a drive without one is read
/// again from its table instead, <see cref="HasJournal"/>).
/// </summary>
public sealed unsafe class NtfsDrive : IDisposable
{
    private const uint QueryJournal = 0x000900F4, ReadJournal = 0x000900BB, VolumeData = 0x00090064, FileRecord = 0x00090068;
    private const int Create = 0x100, Delete = 0x200, RenameNew = 0x2000, HardLink = 0x10000, RootRecord = 5, FirstUserRecord = 24;
    private const long RecordMask = 0x0000_FFFF_FFFF_FFFF;
    private readonly SafeFileHandle _volume; private long _journalId, _nextUsn; private int _recordSize = 1024;
    private int[] _byRecord = [];                          // a record's first name in the index (a folder has only one)
    private readonly Dictionary<long, List<int>> _links = [];   // the other names of a file with hard links

    public string Root { get; }
    public bool HasJournal => _journalId != 0;

    private NtfsDrive(string root, SafeFileHandle volume) { Root = root; _volume = volume; }

    /// <summary>The drive opened for reading its table, or null (not NTFS, or no right to open it) with why.</summary>
    public static NtfsDrive? TryOpen(string root, out string? why)
    {
        why = null;
        try { if (!string.Equals(new DriveInfo(root).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) { why = "not NTFS"; return null; } }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { why = e.Message; return null; }
        var h = CreateFile(@"\\.\" + root.TrimEnd('\\'), 0x80000000 /* GENERIC_READ */, 3 /* share read+write */, 0, 3 /* OPEN_EXISTING */, 0, 0);
        if (h.IsInvalid) { why = $"open {Marshal.GetLastWin32Error()}"; h.Dispose(); return null; }
        return new NtfsDrive(root, h);
    }

    /// <summary>Every file and folder of the drive, read from its table. A name whose folder is not in the table (Windows' own hidden ones) is left out.</summary>
    public FileIndex Build(CancellationToken ct = default)
    {
        long* journal = stackalloc long[10];
        if (Io(QueryJournal, null, 0, journal, 80, out _)) { _journalId = journal[0]; _nextUsn = journal[2]; } else { _journalId = 0; _nextUsn = 0; }
        byte* v = stackalloc byte[128];
        if (!Io(VolumeData, null, 0, v, 128, out _)) throw new IOException($"The NTFS data of {Root} could not be read ({Marshal.GetLastWin32Error()}).");
        // NTFS_VOLUME_DATA_BUFFER: BytesPerCluster at 44, BytesPerFileRecordSegment at 48, MftValidDataLength at 56, MftStartLcn at 64
        int cluster = *(int*)(v + 44); _recordSize = *(int*)(v + 48); long valid = *(long*)(v + 56), mftStart = *(long*)(v + 64);
        long records = valid / _recordSize;

        // the table's own pieces on the disk, from its first record's data runs
        var first = new byte[_recordSize]; RandomAccess.Read(_volume, first, mftStart * cluster);
        var runs = Fixup(first) ? DataRuns(first) : [];
        if (runs.Sum(r => r.Clusters) * cluster < valid) throw new IOException($"The file table of {Root} could not be placed on the disk.");

        int guess = (int)Math.Min(records, int.MaxValue / 64);
        _byRecord = new int[guess + 1024]; Array.Fill(_byRecord, -1); _byRecord[RootRecord] = 0; _links.Clear();
        var index = new FileIndex(Root, guess + guess / 8, guess * 24);
        var parentOf = new int[guess + guess / 8];
        var chunk = new byte[Math.Max(_recordSize, 4 << 20) / _recordSize * _recordSize];
        long record = 0;
        foreach (var (lcn, clusters) in runs)
        {
            long at = lcn * cluster, left = clusters * cluster;
            while (left > 0 && record < records)
            {
                ct.ThrowIfCancellationRequested();
                int size = (int)Math.Min(chunk.Length, left); size -= size % _recordSize;
                if (size == 0) break;
                int got = RandomAccess.Read(_volume, chunk.AsSpan(0, size), at);
                if (got <= 0) break;
                for (int o = 0; o + _recordSize <= got && record < records; o += _recordSize, record++)
                {
                    var r = chunk.AsSpan(o, _recordSize);
                    if (!Fixup(r)) continue;
                    Names(r, record, (owner, parent, name, dir) =>
                    {
                        int e = index.Add(-1, name, dir);
                        if (e >= parentOf.Length) Array.Resize(ref parentOf, parentOf.Length * 2);
                        parentOf[e] = parent; Remember(owner, e);
                    });
                }
                at += got; left -= got;
            }
        }
        // the table is in its own order: children come before their folders, so the parents are joined at the end
        for (int e = 1; e < index.Entries; e++)
        {
            int p = parentOf[e] < _byRecord.Length ? _byRecord[parentOf[e]] : -1;
            if (p >= 0 && p != e) index.SetParent(e, p); else index.Remove(e);
        }
        index.Trim();
        return index;
    }

    /// <summary>
    /// The journal's changes since the last read applied to <paramref name="index"/> (made, deleted, renamed, moved, a hard link added or
    /// taken). False when the journal cannot say (none, deleted, or it has wrapped past what was read): the drive is read again with <see cref="Build"/>.
    /// </summary>
    public bool CatchUp(FileIndex index)
    {
        if (_journalId == 0) return false;
        var buffer = new byte[1 << 16];
        long* request = stackalloc long[5];
        fixed (byte* b = buffer)
        {
            byte* start = b;
            while (true)
            {
                // READ_USN_JOURNAL_DATA_V0: StartUsn, ReasonMask + ReturnOnlyOnClose, Timeout, BytesToWaitFor, UsnJournalID
                request[0] = _nextUsn; request[1] = Create | Delete | RenameNew | HardLink; request[2] = 0; request[3] = 0; request[4] = _journalId;
                if (!Io(ReadJournal, request, 40, b, buffer.Length, out int got)) return false;
                long next = *(long*)b;
                if (got > 8) index.Edit(x => { Changes(x, start, got); return 0; });
                bool done = next == _nextUsn || got <= 8; _nextUsn = next;
                if (done) return true;
            }
        }
    }

    private void Changes(FileIndex x, byte* b, int got)
    {
        for (int at = 8; at < got;)
        {
            var r = new ReadOnlySpan<byte>(b + at, got - at); int length = BinaryPrimitives.ReadInt32LittleEndian(r);
            if (length <= 0) break;
            if (BinaryPrimitives.ReadUInt16LittleEndian(r[4..]) == 2) Apply(x, r[..length]);   // USN_RECORD_V2
            at += length;
        }
    }

    private void Apply(FileIndex x, ReadOnlySpan<byte> r)
    {
        long record = BinaryPrimitives.ReadInt64LittleEndian(r[8..]) & RecordMask, parent = BinaryPrimitives.ReadInt64LittleEndian(r[16..]) & RecordMask;
        uint reason = BinaryPrimitives.ReadUInt32LittleEndian(r[40..]);
        int known = record < _byRecord.Length ? _byRecord[record] : -1;
        if ((reason & Delete) != 0) { Forget(x, record); return; }   // reasons add up until the file is closed: deleted wins
        if ((reason & HardLink) != 0 || _links.ContainsKey(record)) { Reread(x, record); return; }   // which of its names changed is in the table, not the journal
        if ((reason & (Create | RenameNew)) == 0) return;
        int p = parent < _byRecord.Length ? _byRecord[parent] : -1;
        if (p < 0) return;   // its folder is not in the index (one of Windows' hidden ones)
        var name = MemoryMarshal.Cast<byte, char>(r.Slice(BinaryPrimitives.ReadUInt16LittleEndian(r[58..]), BinaryPrimitives.ReadUInt16LittleEndian(r[56..])));
        if (known > 0) x.Move(known, p, name);
        else Remember(record, x.Add(p, name, (BinaryPrimitives.ReadUInt32LittleEndian(r[52..]) & 0x10) != 0));
    }

    /// <summary>A file's names read again from its record in the table (FSCTL_GET_NTFS_FILE_RECORD), after a hard link was added or taken.</summary>
    private void Reread(FileIndex x, long record)
    {
        Forget(x, record);
        var output = new byte[_recordSize + 16];
        fixed (byte* o = output)
        {
            long ask = record;
            if (!Io(FileRecord, &ask, 8, o, output.Length, out _) || (*(long*)o & RecordMask) != record) return;   // gone, or Windows gave a lower record
            var r = output.AsSpan(12, Math.Min(*(int*)(o + 8), _recordSize));
            Fixup(r, lenient: true);
            Names(r, record, (owner, parent, name, dir) => { int p = parent < _byRecord.Length ? _byRecord[parent] : -1; if (p >= 0) Remember(owner, x.Add(p, name, dir)); });
        }
    }

    private void Forget(FileIndex x, long record)
    {
        if (record < _byRecord.Length && _byRecord[record] > 0) { x.Remove(_byRecord[record]); _byRecord[record] = -1; }
        if (_links.Remove(record, out var more)) foreach (int e in more) x.Remove(e);
    }

    private void Remember(long record, int entry)
    {
        if (record >= _byRecord.Length) { int old = _byRecord.Length; Array.Resize(ref _byRecord, (int)Math.Max(record + 1024, old * 2L)); Array.Fill(_byRecord, -1, old, _byRecord.Length - old); }
        if (_byRecord[record] < 0) _byRecord[record] = entry;
        else (_links.TryGetValue(record, out var l) ? l : _links[record] = []).Add(entry);
    }

    private delegate void NameFound(long owner, int parent, ReadOnlySpan<char> name, bool dir);

    /// <summary>
    /// The names in one record of the table: each $FILE_NAME attribute but the DOS short ones. An extension record's names belong to its base
    /// record. Windows' own records (the first 24) and the root's name are left out.
    /// </summary>
    private static void Names(Span<byte> r, long record, NameFound found)
    {
        if ((BinaryPrimitives.ReadUInt16LittleEndian(r[22..]) & 1) == 0) return;   // not in use
        long baseRecord = BinaryPrimitives.ReadInt64LittleEndian(r[32..]) & RecordMask, owner = baseRecord != 0 ? baseRecord : record;
        if (owner < FirstUserRecord) return;
        for (int a = BinaryPrimitives.ReadUInt16LittleEndian(r[20..]); a + 24 <= r.Length;)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(r[a..]); int length = BinaryPrimitives.ReadInt32LittleEndian(r[(a + 4)..]);
            if (type == 0xFFFFFFFF || length <= 0 || a + length > r.Length) break;
            if (type == 0x30 && r[a + 8] == 0)   // $FILE_NAME, always resident
            {
                int value = a + BinaryPrimitives.ReadUInt16LittleEndian(r[(a + 20)..]);
                int chars = r[value + 64]; byte space = r[value + 65];
                if (space != 2 && value + 66 + chars * 2 <= r.Length)   // 2: the DOS 8.3 name, a second spelling of a name already there
                {
                    long parent = BinaryPrimitives.ReadInt64LittleEndian(r[value..]) & RecordMask;
                    bool dir = (BinaryPrimitives.ReadUInt32LittleEndian(r[(value + 56)..]) & 0x10000000) != 0;
                    if (parent <= int.MaxValue) found(owner, (int)parent, MemoryMarshal.Cast<byte, char>(r.Slice(value + 66, chars * 2)), dir);
                }
            }
            a += length;
        }
    }

    /// <summary>
    /// The record's update sequence put back: the last two bytes of each 512-byte stride were swapped for a check number when it was written,
    /// so a torn write shows. False when the record is not a file record or a stride does not check (a damaged record is not read).
    /// </summary>
    private static bool Fixup(Span<byte> r, bool lenient = false)
    {
        if (r.Length < 48 || r[0] != (byte)'F' || r[1] != (byte)'I' || r[2] != (byte)'L' || r[3] != (byte)'E') return false;
        int usa = BinaryPrimitives.ReadUInt16LittleEndian(r[4..]), count = BinaryPrimitives.ReadUInt16LittleEndian(r[6..]);
        if (count < 2 || usa + count * 2 > r.Length) return lenient;
        int stride = r.Length / (count - 1); ushort check = BinaryPrimitives.ReadUInt16LittleEndian(r[usa..]);
        for (int i = 1; i < count; i++)
        {
            int end = i * stride - 2;
            if (end + 2 > r.Length) break;
            if (BinaryPrimitives.ReadUInt16LittleEndian(r[end..]) != check) { if (lenient) continue; return false; }
            r[end] = r[usa + i * 2]; r[end + 1] = r[usa + i * 2 + 1];
        }
        return true;
    }

    /// <summary>The unnamed $DATA attribute's runs in a record: where the file's pieces lie, as (first cluster, clusters).</summary>
    private static List<(long Lcn, long Clusters)> DataRuns(ReadOnlySpan<byte> r)
    {
        var runs = new List<(long, long)>();
        for (int a = BinaryPrimitives.ReadUInt16LittleEndian(r[20..]); a + 24 <= r.Length;)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(r[a..]); int length = BinaryPrimitives.ReadInt32LittleEndian(r[(a + 4)..]);
            if (type == 0xFFFFFFFF || length <= 0 || a + length > r.Length) break;
            if (type == 0x80 && r[a + 8] == 1 && r[a + 9] == 0)   // non-resident, no name
            {
                long lcn = 0;
                for (int p = a + BinaryPrimitives.ReadUInt16LittleEndian(r[(a + 32)..]); p < a + length && r[p] != 0;)
                {
                    int lengthBytes = r[p] & 0xF, offsetBytes = r[p] >> 4; p++;
                    long count = 0; for (int i = 0; i < lengthBytes; i++) count |= (long)r[p + i] << (8 * i);
                    p += lengthBytes;
                    if (offsetBytes > 0)
                    {
                        long delta = 0; for (int i = 0; i < offsetBytes; i++) delta |= (long)r[p + i] << (8 * i);
                        if ((r[p + offsetBytes - 1] & 0x80) != 0) delta |= -1L << (8 * offsetBytes);   // signed: a run can lie before the last
                        p += offsetBytes; lcn += delta; runs.Add((lcn, count));
                    }
                }
                break;
            }
            a += length;
        }
        return runs;
    }

    private bool Io(uint code, void* input, int inSize, void* output, int outSize, out int got) => DeviceIoControl(_volume, code, input, inSize, output, outSize, out got, 0);

    public void Dispose() => _volume.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, void* input, int inSize, void* output, int outSize, out int returned, nint overlapped);
}

/// <summary>A drive without a file table to read (exFAT, FAT32): its folders walked, junctions not followed. Read again when it is old.</summary>
public static class FolderDrive
{
    public static FileIndex Walk(string root, CancellationToken ct = default)
    {
        var index = new FileIndex(root);
        var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
        var stack = new Stack<(string Path, int Entry)>(); stack.Push((root, 0));
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (path, entry) = stack.Pop();
            try
            {
                foreach (var e in new DirectoryInfo(path).EnumerateFileSystemInfos("*", opts))
                {
                    bool dir = (e.Attributes & FileAttributes.Directory) != 0; int i = index.Add(entry, e.Name, dir);
                    if (dir && (e.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push((e.FullName, i));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }   // a folder Windows refuses: its names are not in the index
        }
        index.Trim();
        return index;
    }
}
