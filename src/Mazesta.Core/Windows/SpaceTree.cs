namespace Mazesta.Core.Windows;

/// <summary>
/// A drive's (or folder's) files and folders with their sizes, for the disk-space window: one entry per file or folder in flat arrays (a whole
/// drive is millions of them), filled in any order - an NTFS table lists children before their folders - then <see cref="Seal"/>ed: each folder
/// gets the size and file count of everything under it, its children biggest first, and an entry whose folder never came is dropped.
/// A folder Windows refused to list is marked <see cref="Unread"/>, so its size is "at least", never a measured total.
/// </summary>
public sealed class DiskTree
{
    private const byte Dir = 1, Refused = 2, Inside = 4, Live = 8;
    private int[] _parent; private string?[] _name; private long[] _size; private int[] _files; private byte[] _flags;
    private int[] _first = [], _kids = [];
    private int _count;

    public string Root { get; }
    /// <summary>The root's entry (NTFS: record 5; a walked folder: 0).</summary>
    public int RootEntry { get; }
    /// <summary>Files and folders under the root, after <see cref="Seal"/>.</summary>
    public int Count { get; private set; }

    public DiskTree(string root, int rootEntry = 0, int capacity = 1024)
    {
        Root = root; RootEntry = rootEntry; capacity = Math.Max(capacity, rootEntry + 1);
        _parent = new int[capacity]; _name = new string?[capacity]; _size = new long[capacity]; _files = new int[capacity]; _flags = new byte[capacity];
        Set(rootEntry, rootEntry, root, true);
    }

    /// <summary>An entry's place and name; the first one given wins (a file's other hard-link names are not counted twice).</summary>
    public void Set(int entry, int parent, string name, bool dir)
    {
        Grow(entry);
        if (_name[entry] is not null) return;
        _parent[entry] = parent; _name[entry] = name; _flags[entry] |= dir ? Dir : (byte)0;
    }

    /// <summary>A new entry after the last one (a walked folder's own numbering).</summary>
    public int Add(int parent, string name, bool dir) { int e = Math.Max(_count, RootEntry + 1); Set(e, parent, name, dir); return e; }

    public void AddSize(int entry, long bytes) { Grow(entry); _size[entry] += bytes; }
    public void MarkUnread(int entry) { Grow(entry); _flags[entry] |= Refused; }

    private void Grow(int entry)
    {
        if (entry >= _parent.Length)
        {
            int n = Math.Max(entry + 1, _parent.Length * 2);
            Array.Resize(ref _parent, n); Array.Resize(ref _name, n); Array.Resize(ref _size, n); Array.Resize(ref _files, n); Array.Resize(ref _flags, n);
        }
        _count = Math.Max(_count, entry + 1);
    }

    /// <summary>After filling: folders' totals, children biggest first, entries cut off from the root dropped.</summary>
    public void Seal()
    {
        int n = _count;
        // which entries reach the root: each chain walked once, its verdict kept on the way back (0 unknown, 1 yes, 2 no, 3 on this walk)
        var reach = new byte[n]; reach[RootEntry] = 1; var path = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (_name[i] is null || reach[i] != 0) continue;
            int x = i; path.Clear();
            while (x >= 0 && x < n && reach[x] == 0 && _name[x] is not null && path.Count < 4096) { reach[x] = 3; path.Add(x); x = _parent[x]; }
            byte verdict = x >= 0 && x < n && reach[x] == 1 ? (byte)1 : (byte)2;
            foreach (int p in path) reach[p] = verdict;
        }
        int[] count = new int[n]; Count = 0;
        for (int i = 0; i < n; i++)
        {
            if (reach[i] != 1 || i == RootEntry) continue;
            _flags[i] |= Live; Count++; count[_parent[i]]++;
            if ((_flags[i] & Dir) != 0)
            {
                if ((_flags[i] & Refused) != 0) for (int p = _parent[i]; ; p = _parent[p]) { _flags[p] |= Inside; if (p == RootEntry) break; }
                continue;
            }
            _files[i] = 1; long s = _size[i];
            for (int p = _parent[i]; ; p = _parent[p]) { _size[p] += s; _files[p]++; if (p == RootEntry) break; }
        }
        _flags[RootEntry] |= Live;
        _first = new int[n + 1];
        for (int i = 0; i < n; i++) _first[i + 1] = _first[i] + count[i];
        _kids = new int[_first[n]]; var at = (int[])_first.Clone();
        for (int i = 0; i < n; i++) if ((_flags[i] & Live) != 0 && i != RootEntry) _kids[at[_parent[i]]++] = i;
        var bySize = Comparer<int>.Create((a, b) => _size[b].CompareTo(_size[a]));
        for (int i = 0; i < n; i++) if (count[i] > 1) Array.Sort(_kids, _first[i], count[i], bySize);
    }

    public ReadOnlySpan<int> Children(int entry) => _kids.AsSpan(_first[entry], _first[entry + 1] - _first[entry]);
    public string Name(int entry) => _name[entry] ?? "";
    public long Size(int entry) => _size[entry];
    /// <summary>Files under a folder (1 for a file).</summary>
    public int Files(int entry) => _files[entry];
    public bool IsDir(int entry) => (_flags[entry] & Dir) != 0;
    public int Parent(int entry) => _parent[entry];
    /// <summary>Windows refused to list this folder: its size is not known.</summary>
    public bool Unread(int entry) => (_flags[entry] & Refused) != 0;
    /// <summary>A folder somewhere under this one could not be read: its size is "at least".</summary>
    public bool PartUnread(int entry) => (_flags[entry] & Inside) != 0;

    public string PathOf(int entry)
    {
        var names = new Stack<string>();
        for (int e = entry; e != RootEntry; e = _parent[e]) names.Push(_name[e]!);
        return names.Count == 0 ? Root : Path.Combine(Root, string.Join(Path.DirectorySeparatorChar, names));
    }

    /// <summary>
    /// Files (and folders, with <paramref name="folders"/>) whose name holds <paramref name="text"/> in any case, biggest first, at most
    /// <paramref name="max"/>; with no text, the biggest files. <paramref name="total"/> is how many matched in all.
    /// </summary>
    public List<int> Find(string text, bool folders, int max, out int total, CancellationToken ct = default)
    {
        var best = new PriorityQueue<int, long>(); total = 0;
        for (int i = 0; i < _count; i++)
        {
            if ((_flags[i] & Live) == 0 || i == RootEntry || (!folders && (_flags[i] & Dir) != 0)) continue;
            if (text.Length > 0 && !_name[i]!.Contains(text, StringComparison.OrdinalIgnoreCase)) continue;
            if ((++total & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            if (best.Count < max) best.Enqueue(i, _size[i]);
            else if (best.TryPeek(out _, out long least) && _size[i] > least) best.EnqueueDequeue(i, _size[i]);
        }
        var list = new List<int>(best.Count);
        while (best.TryDequeue(out int e, out _)) list.Add(e);
        list.Reverse();
        return list;
    }
}
