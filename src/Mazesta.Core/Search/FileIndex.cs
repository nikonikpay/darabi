using System.Numerics; using System.Runtime.InteropServices; using System.Runtime.Intrinsics; using System.Text;
namespace Mazesta.Core.Search;

/// <summary>
/// The names of one drive's files and folders, kept compact for the quick file search: every name in one pool as UTF-8 (a Latin name takes a
/// byte a letter), and per entry only its parent, where its name starts, its length and two flags. A path is not kept: it is the chain of
/// parents, put together only for the rows on screen. Built single-threaded before anyone searches it; after that, changes go through
/// <see cref="Edit"/>, which keeps searches out while it runs.
/// </summary>
public sealed class FileIndex
{
    public const byte Dir = 1, Gone = 2;
    private int[] _parent, _start; private ushort[] _len; private byte[] _flags; private byte[] _names;
    private int _count, _used, _live;
    // After the drive is read (Trim) its names lie in entry order, each followed by a 0 byte, so one vectorised search runs over the whole pool and a
    // hit is placed by a binary search of where the names start. Names written later (made, renamed, moved) are searched one by one.
    private int[] _frozenStart = []; private int _frozen, _frozenUsed; private readonly HashSet<int> _late = [];
    private readonly ReaderWriterLockSlim _lock = new();

    /// <summary>The drive's root as a path ("C:\"); entry 0 is the root itself.</summary>
    public string Root { get; }
    /// <summary>Files and folders in the index, the root not counted.</summary>
    public int Count => _live;
    /// <summary>Entries ever added, the root and removed ones included: the bound for walking them by index.</summary>
    public int Entries => _count;
    /// <summary>The memory the index holds, in bytes (its arrays): for the log, to compare with other search tools.</summary>
    public long Bytes => _parent.Length * 8L + _len.Length * 2L + _flags.Length + _names.Length + _frozenStart.Length * 4L;

    public FileIndex(string root, int capacity = 1 << 12, int nameCapacity = 1 << 16)
    {
        Root = root; int n = Math.Max(capacity, 1);
        _parent = new int[n]; _start = new int[n]; _len = new ushort[n]; _flags = new byte[n]; _names = new byte[Math.Max(nameCapacity, 64)];
        Add(-1, root.TrimEnd('\\'), true); _live = 0;   // the root: its name is the drive ("C:"), so a path is the names joined
    }

    /// <summary>A new entry under <paramref name="parent"/>; its index. A name longer than 255 characters (none on NTFS) is cut.</summary>
    public int Add(int parent, ReadOnlySpan<char> name, bool dir)
    {
        if (_count == _parent.Length) Grow(_count * 2);
        var (start, length) = Keep(name);
        int i = _count++; _parent[i] = parent; _start[i] = start; _len[i] = length; _flags[i] = dir ? Dir : (byte)0; _live++;
        if (_frozen > 0) _late.Add(i);
        return i;
    }

    /// <summary>The entry's parent set after the fact (a drive read in its own order meets children before their folders).</summary>
    public void SetParent(int entry, int parent) => _parent[entry] = parent;

    public void Remove(int entry) { if (entry > 0 && entry < _count && (_flags[entry] & Gone) == 0) { _flags[entry] |= Gone; _live--; } }

    /// <summary>Renamed or moved: the new name goes to the end of the pool (the old bytes stay unused until the index is built again).</summary>
    public void Move(int entry, int parent, ReadOnlySpan<char> name)
    {
        if (entry <= 0 || entry >= _count) return;
        (_start[entry], _len[entry]) = Keep(name); _parent[entry] = parent;
        if (_frozen > 0) _late.Add(entry);
    }

    /// <summary>Once the drive has been read: the spare room of building in doubling steps given back, and the names fixed for the pool search.</summary>
    public void Trim()
    {
        Grow(_count); Array.Resize(ref _names, Math.Max(_used, 64));
        _frozen = _count; _frozenUsed = _used; _frozenStart = _start[.._count]; _late.Clear();
    }

    /// <summary>Changes after the index is in use: searches wait while <paramref name="change"/> runs.</summary>
    public T Edit<T>(Func<FileIndex, T> change)
    {
        _lock.EnterWriteLock();
        try { return change(this); } finally { _lock.ExitWriteLock(); }
    }

    public bool IsDir(int entry) => (_flags[entry] & Dir) != 0;
    public bool IsGone(int entry) => (_flags[entry] & Gone) != 0;
    public string Name(int entry) => Encoding.UTF8.GetString(_names, _start[entry], _len[entry]);

    /// <summary>The entry's full path, put together from its parents ("C:\Users\me\a.txt").</summary>
    public string PathOf(int entry)
    {
        _lock.EnterReadLock();
        try
        {
            var chain = new List<int>(16);
            for (int e = entry; e >= 0 && chain.Count < 256; e = _parent[e]) chain.Add(e);   // (a cap: a damaged chain cannot loop for ever)
            if (chain.Count == 1) return Root;
            var sb = new StringBuilder(260);
            for (int k = chain.Count - 1; k >= 0; k--) { sb.Append(Encoding.UTF8.GetString(_names, _start[chain[k]], _len[chain[k]])); if (k > 0) sb.Append('\\'); }
            return sb.ToString();
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Every entry that matches, counted; the <paramref name="max"/> best of them (by <see cref="FileQuery.Rank"/>, then the shorter name, then
    /// the shorter folder name: C:\Windows before a long WinSxS folder) added to <paramref name="best"/> as (key, entry).
    /// </summary>
    public int Search(FileQuery query, int max, List<(long Key, int Entry)> best, CancellationToken ct = default)
    {
        _lock.EnterReadLock();
        try { return query.Anchor is { } a && _frozen > 1 ? SearchPool(query, a, max, best, ct) : SearchEach(query, max, best, ct); }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>The first word looked for in the whole pool at once (vectorised), each hit placed in its name and that name ranked; then the late names.</summary>
    private int SearchPool(FileQuery query, FileQuery.Word word, int max, List<(long Key, int Entry)> best, CancellationToken ct)
    {
        int total = 0, w = word.Utf8.Length; object merge = new();
        const int Chunk = 1 << 21;
        Parallel.For(0, (_frozenUsed + Chunk - 1) / Chunk, new ParallelOptions { CancellationToken = ct }, () => new Top(max), (c, loop, local) =>
        {
            int from = c * Chunk, to = Math.Min(_frozenUsed, from + Chunk);   // hits that start in [from, to); the span runs on so one at the edge is whole
            var pool = _names.AsSpan(0, Math.Min(_frozenUsed, to + w - 1));
            for (int at = from; at < to;)
            {
                int hit = word.Find(pool[at..]);
                if (hit < 0 || at + hit >= to) break;
                hit += at;
                int e = Array.BinarySearch(_frozenStart, 0, _frozen, hit); if (e < 0) e = ~e - 1;   // the name that starts at or before the hit
                int end = _frozenStart[e] + _len[e];
                if (e > 0 && (_flags[e] & Gone) == 0 && _start[e] == _frozenStart[e] && hit + w <= end) Offer(query, e, local);
                at = Math.Max(hit + 1, end + 1);   // one hit a name is enough: the rest of it is skipped
            }
            return local;
        }, local => { lock (merge) { total += local.Count; local.Into(best); } });
        var late = new Top(max);
        foreach (int e in _late) if (e > 0 && (_flags[e] & Gone) == 0) Offer(query, e, late);
        total += late.Count; late.Into(best);
        return total;
    }

    /// <summary>Every name tried in turn (patterns only, or a word with letters of other alphabets that have case, or an index not yet fixed).</summary>
    private int SearchEach(FileQuery query, int max, List<(long Key, int Entry)> best, CancellationToken ct)
    {
        int total = 0, count = _count; object merge = new();
        const int Chunk = 1 << 15;
        Parallel.For(0, (count + Chunk - 1) / Chunk, new ParallelOptions { CancellationToken = ct }, () => new Top(max), (c, loop, local) =>
        {
            int end = Math.Min(count, (c + 1) * Chunk);
            for (int i = Math.Max(1, c * Chunk); i < end; i++) if ((_flags[i] & Gone) == 0) Offer(query, i, local);
            return local;
        }, local => { lock (merge) { total += local.Count; local.Into(best); } });
        return total;
    }

    private void Offer(FileQuery query, int e, Top top)
    {
        int rank = query.Rank(_names.AsSpan(_start[e], _len[e]));
        if (rank < 0) return;
        // among equal names the shorter path first (C:\Windows\System32\cmd.exe before WinSxS\...\r\cmd.exe): measured up the chain only for the
        // name itself, which few entries are; the others go by their folder's name
        int p = _parent[e], tie = 0;
        if (rank == 0) for (int a = p, n = 0; a >= 0 && n < 64; a = _parent[a], n++) tie += _len[a] + 1;
        else if (p >= 0) tie = _len[p];
        top.Add(e, ((long)rank << 32) | ((long)_len[e] << 16) | (long)Math.Min(tie, 0xFFFF));
    }

    /// <summary>A worker's best: a max-heap by negated key, so the worst kept is the one replaced.</summary>
    private sealed class Top(int max)
    {
        private readonly PriorityQueue<int, long> _q = new();
        public int Count;
        public void Add(int e, long key)
        {
            Count++;
            if (_q.Count < max) _q.Enqueue(e, -key);
            else if (_q.TryPeek(out _, out long worst) && -key > worst) _q.EnqueueDequeue(e, -key);
        }
        public void Into(List<(long, int)> best) { while (_q.TryDequeue(out int e, out long k)) best.Add((-k, e)); }
    }

    // ponytail: a name with a lone surrogate (not valid UTF-16, which NTFS allows) is kept with U+FFFD in its place, so its path is not quite right
    private (int Start, ushort Length) Keep(ReadOnlySpan<char> name)
    {
        if (name.Length > 255) name = name[..255];
        int room = Encoding.UTF8.GetMaxByteCount(name.Length) + 1;
        if (_used + room > _names.Length) Array.Resize(ref _names, (int)Math.Min(Array.MaxLength, Math.Max(_names.Length * 2L, _used + room)));
        int n = Encoding.UTF8.GetBytes(name, _names.AsSpan(_used)), start = _used;
        _used += n; _names[_used++] = 0;   // the end mark: a word never runs across two names
        return (start, (ushort)n);
    }

    private void Grow(int size)
    {
        size = Math.Max(size, 1);
        Array.Resize(ref _parent, size); Array.Resize(ref _start, size); Array.Resize(ref _len, size); Array.Resize(ref _flags, size);
    }
}

/// <summary>
/// What was typed: words that must all be in the name (any case, any order), or patterns with * and ? that the whole name must fit ("*.mp4",
/// "report??.pdf"). A name is ranked 0 when it is the first word (with or without its extension), 1 when it starts with it, 2 otherwise.
/// Words are matched on the UTF-8 names as they lie: a Latin word ignoring case byte by byte, a word without case (Persian) as its bytes; only a
/// word in another alphabet with case (é, Я) has each name turned back into text first.
/// </summary>
public sealed class FileQuery
{
    public sealed class Word
    {
        public string Text { get; }
        public byte[] Utf8 { get; }
        /// <summary>All ASCII: matched ignoring case byte by byte. Otherwise <see cref="Cased"/> says whether case matters at all.</summary>
        public bool Ascii { get; }
        public bool Cased { get; }
        internal Word(string text) { Text = text; Utf8 = Encoding.UTF8.GetBytes(text); Ascii = System.Text.Ascii.IsValid(text); Cased = text.Any(c => char.ToUpperInvariant(c) != char.ToLowerInvariant(c)); }

        /// <summary>Where the word starts in <paramref name="s"/>, or -1. Only for <see cref="Ascii"/> or uncased words.</summary>
        public int Find(ReadOnlySpan<byte> s)
        {
            if (!Ascii) return s.IndexOf(Utf8);
            int w = Utf8.Length;
            if (w == 1) { byte c = Utf8[0], lo = Lower(c), up = (byte)char.ToUpperInvariant((char)c); return lo == up ? s.IndexOf(lo) : s.IndexOfAny(lo, up); }
            // The first two letters looked for together, 32 bytes at a time: a letter's case bit is set before comparing (other bytes as they are),
            // so a common first letter alone does not stop the search at every name. Each place where both fit is then compared whole.
            byte t0 = Lower(Utf8[0]), t1 = Lower(Utf8[1]), m0 = IsLetter(Utf8[0]) ? (byte)0x20 : (byte)0, m1 = IsLetter(Utf8[1]) ? (byte)0x20 : (byte)0;
            int i = 0;
            if (Vector256.IsHardwareAccelerated && s.Length >= 33)
            {
                var v0 = Vector256.Create(t0); var v1 = Vector256.Create(t1); var k0 = Vector256.Create(m0); var k1 = Vector256.Create(m1);
                ref byte r = ref MemoryMarshal.GetReference(s);
                for (; i + 33 <= s.Length; i += 32)
                {
                    var hit = Vector256.Equals(Vector256.LoadUnsafe(ref r, (nuint)i) | k0, v0) & Vector256.Equals(Vector256.LoadUnsafe(ref r, (nuint)(i + 1)) | k1, v1);
                    for (uint bits = hit.ExtractMostSignificantBits(); bits != 0; bits &= bits - 1)
                    {
                        int at = i + BitOperations.TrailingZeroCount(bits);
                        if (at + w <= s.Length && System.Text.Ascii.EqualsIgnoreCase(s.Slice(at, w), Utf8)) return at;
                    }
                }
            }
            for (; i + w <= s.Length; i++)
                if ((s[i] | m0) == t0 && (s[i + 1] | m1) == t1 && System.Text.Ascii.EqualsIgnoreCase(s.Slice(i, w), Utf8)) return i;
            return -1;
        }

        private static bool IsLetter(byte b) => (uint)((b | 0x20) - 'a') <= 'z' - 'a';
        private static byte Lower(byte b) => IsLetter(b) ? (byte)(b | 0x20) : b;

        public bool StartOf(ReadOnlySpan<byte> s) => s.Length >= Utf8.Length && (Ascii ? System.Text.Ascii.EqualsIgnoreCase(s[..Utf8.Length], Utf8) : s.StartsWith(Utf8));
    }

    private readonly Word[] _words; private readonly string[] _globs; private readonly bool _bytes;
    private FileQuery(Word[] words, string[] globs) { _words = words; _globs = globs; _bytes = globs.Length == 0 && words.All(w => w.Ascii || !w.Cased); }

    /// <summary>The first word, which the index looks for across all names at once (null when only patterns were typed, or it has case outside ASCII).</summary>
    public Word? Anchor => _words.Length > 0 && (_words[0].Ascii || !_words[0].Cased) ? _words[0] : null;

    /// <summary>Null when nothing is asked (only spaces).</summary>
    public static FileQuery? Parse(string? text)
    {
        var parts = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        return new([.. parts.Where(p => p.IndexOfAny(['*', '?']) < 0).Select(p => new Word(p))], [.. parts.Where(p => p.IndexOfAny(['*', '?']) >= 0)]);
    }

    /// <summary>The name's rank (0 the word itself, 1 starts with it, 2 has it), or -1 when it does not match.</summary>
    public int Rank(ReadOnlySpan<byte> name)
    {
        if (_bytes)
        {
            foreach (var w in _words) if (w.Utf8.Length > name.Length || w.Find(name) < 0) return -1;
            var first = _words[0];
            if (!first.StartOf(name)) return 2;
            return name.Length == first.Utf8.Length || name.LastIndexOf((byte)'.') == first.Utf8.Length ? 0 : 1;
        }
        Span<char> text = stackalloc char[512];   // 255 characters at most, each one or two UTF-16 units
        return Rank(text[..Encoding.UTF8.GetChars(name, text)]);
    }

    /// <summary>The same on the name as text (patterns, or a word with case outside ASCII).</summary>
    public int Rank(ReadOnlySpan<char> name)
    {
        foreach (var w in _words) if (w.Text.Length > name.Length || name.IndexOf(w.Text, StringComparison.OrdinalIgnoreCase) < 0) return -1;
        foreach (var g in _globs) if (!Glob(name, g)) return -1;
        if (_words.Length == 0) return 2;
        string first = _words[0].Text;
        if (!name.StartsWith(first, StringComparison.OrdinalIgnoreCase)) return 2;
        return name.Length == first.Length || name.LastIndexOf('.') == first.Length ? 0 : 1;
    }

    /// <summary>A whole-name match with * (any run) and ? (one character), any case: the usual greedy matcher that steps back to the last *.</summary>
    private static bool Glob(ReadOnlySpan<char> s, string p)
    {
        int i = 0, j = 0, star = -1, mark = 0;
        while (i < s.Length)
        {
            if (j < p.Length && (p[j] == '?' || char.ToUpperInvariant(p[j]) == char.ToUpperInvariant(s[i]))) { i++; j++; }
            else if (j < p.Length && p[j] == '*') { star = j++; mark = i; }
            else if (star >= 0) { j = star + 1; i = ++mark; }
            else return false;
        }
        while (j < p.Length && p[j] == '*') j++;
        return j == p.Length;
    }
}
