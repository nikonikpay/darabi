using System.Diagnostics; using System.Globalization; using System.Runtime.InteropServices; using Mazesta.Core.Search;
namespace Mazesta.Tray.FileSearch;

/// <summary>
/// The quick file search's window: a box to type in and the matches below it, the best first, found again at every key. It is a plain native
/// window (no web page), so it opens at once; it hides when Esc is pressed or another window is clicked, and is kept for the next time.
/// Enter opens the file, Ctrl+Enter its folder with it selected, Ctrl+C copies its path. Files are opened through Explorer, so they run as
/// the user, not with the tray's administrator rights. Only the rows on screen read a size and a date from the disk.
/// </summary>
internal sealed class SearchForm : Form
{
    private const int Max = 500;
    private static readonly Color Text1 = Theme.Paper, Dim = Theme.Paper2;
    private int _sortCol = -1; private bool _sortUp = true;
    private readonly DriveSet _drives; private readonly string _shortcut;
    private readonly ImageList _icons = new() { ColorDepth = ColorDepth.Depth32Bit }; private readonly Dictionary<string, int> _iconOf = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _box; private readonly ListView _list; private readonly Label _status;
    private List<(FileIndex Index, int Entry)> _hits = []; private readonly Dictionary<int, ListViewItem> _rows = [];
    private CancellationTokenSource? _cts; private int _total; private long _ms;
    private bool? _dirs; private HashSet<string>? _exts;   // the chips: files or folders only, one kind of file only

    private static HashSet<string> Ext(string list) => new(list.Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string>[] Kinds =
    [
        Ext("pdf doc docx xls xlsx ppt pptx txt rtf odt ods csv md epub"), Ext("jpg jpeg png gif bmp webp tif tiff svg psd ico heic raw cr2 nef arw exr hdr tga ai"),
        Ext("mp4 mkv avi mov wmv webm flv m4v ts mpg mpeg"), Ext("mp3 wav flac aac ogg m4a wma opus"), Ext("zip rar 7z tar gz bz2 xz iso cab"),
        Ext("exe msi bat cmd lnk appx"), Ext("cs js ts py cpp c h hpp java json xml html css php sql ps1 sh go rs lua")
    ];

    // chips that act as one choice: clicking one turns the others off (the one on stays on), and the search runs again
    private void Group<T>(FlowLayoutPanel host, (string Text, T Value)[] options, Action<T> set)
    {
        var chips = new List<CheckBox>();
        foreach (var (text, value) in options)
        {
            var c = Theme.Chip(text); c.Checked = chips.Count == 0; chips.Add(c); host.Controls.Add(c);
            c.Click += (_, _) => { foreach (var o in chips) o.Checked = o == c; set(value); Find(); };
        }
    }

    public SearchForm(DriveSet drives, string shortcut)
    {
        _drives = drives; _shortcut = shortcut;
        Theme.Window(this); Text = TrayText.SearchTitle; ShowInTaskbar = true; StartPosition = FormStartPosition.Manual; KeyPreview = true; MinimumSize = new Size(700, 420);
        _box = new TextBox { Dock = DockStyle.Top, Font = Theme.Reg(15f), PlaceholderText = TrayText.SearchHint }; Theme.Field(_box); _box.BackColor = Theme.Ink3;
        _icons.ImageSize = new Size(LogicalToDeviceUnits(16), LogicalToDeviceUnits(16));
        _list = new ListView { Dock = DockStyle.Fill, View = View.Details, VirtualMode = true, HideSelection = false, MultiSelect = false, SmallImageList = _icons };
        Theme.List(_list, () => _sortCol < 0 ? null : (_sortCol, _sortUp), SortBy); Theme.DarkScroll(_list);
        _list.Columns.Add(TrayText.SearchName, 300); _list.Columns.Add(TrayText.SearchFolder, 460); _list.Columns.Add(TrayText.SearchSize, 90, HorizontalAlignment.Right); _list.Columns.Add(TrayText.SearchDate, 140);
        _status = new Label { Dock = DockStyle.Bottom, Height = 32, ForeColor = Dim, BackColor = Theme.Ink2, TextAlign = ContentAlignment.MiddleRight, RightToLeft = RightToLeft.Yes, Padding = new Padding(12, 0, 12, 0) };
        var boxPad = new Panel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(12, 10, 12, 8), BackColor = Theme.Ink2 }; boxPad.Controls.Add(_box);
        var chips = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12, 0, 12, 4), RightToLeft = RightToLeft.Yes, BackColor = Theme.Ink2 };
        Group<bool?>(chips, [(TrayText.SearchAll, null), (TrayText.SearchFiles, false), (TrayText.SearchFolders, true)], v => _dirs = v);
        chips.Controls.Add(new Panel { Width = 16, Height = 1, Margin = Padding.Empty });   // a gap between the two groups of chips
        Group<HashSet<string>?>(chips, [(TrayText.SearchAnyKind, null), (TrayText.SearchDocs, Kinds[0]), (TrayText.SearchImages, Kinds[1]), (TrayText.SearchVideo, Kinds[2]), (TrayText.SearchAudio, Kinds[3]), (TrayText.SearchArchives, Kinds[4]), (TrayText.SearchPrograms, Kinds[5]), (TrayText.SearchCode, Kinds[6])], v => _exts = v);
        Controls.Add(_list); Controls.Add(_status); Controls.Add(chips); Controls.Add(boxPad); Controls.Add(Theme.Banner(Theme.Search, Theme.Gpu, TrayText.SearchTitle, TrayText.SearchSub));
        var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        menu.Items.Add(TrayText.SearchOpen, null, (_, _) => Open(false)); menu.Items.Add(TrayText.SearchOpenFolder, null, (_, _) => Open(true)); menu.Items.Add(TrayText.SearchCopyPath, null, (_, _) => CopyPath());
        _list.ContextMenuStrip = menu;

        _box.TextChanged += (_, _) => Find();
        _list.RetrieveVirtualItem += (_, e) => e.Item = Row(e.ItemIndex);
        _list.DoubleClick += (_, _) => Open(false);
        // the folder column takes what the others leave, so a wider window has no empty strip beside the list
        _list.Resize += (_, _) => { int rest = _list.ClientSize.Width - _list.Columns[0].Width - _list.Columns[2].Width - _list.Columns[3].Width - SystemInformation.VerticalScrollBarWidth - 2; if (rest > 80 && rest != _list.Columns[1].Width) _list.Columns[1].Width = rest; };
        _drives.Changed += () => { try { BeginInvoke(() => { if (Visible) Find(); else ShowState(); }); } catch (InvalidOperationException) { } };
    }

    /// <summary>Opened by the shortcut or the menu: in the middle of the screen the mouse is on, in front, the drives brought up to date first.</summary>
    public void Summon()
    {
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        if (!Visible) { var size = new Size(Math.Max(MinimumSize.Width, Math.Min(screen.Width * 55 / 100, LogicalToDeviceUnits(1100))), Math.Max(MinimumSize.Height, screen.Height * 60 / 100)); Bounds = new Rectangle(screen.X + (screen.Width - size.Width) / 2, screen.Y + (screen.Height - size.Height) / 3, size.Width, size.Height); }
        Show(); Front(); _box.Focus(); _box.SelectAll();
        Task.Run(() => { _drives.Refresh(); try { BeginInvoke(Find); } catch (InvalidOperationException) { } });
    }

    private void Find()
    {
        _cts?.Cancel(); var cts = _cts = new CancellationTokenSource();
        var query = FileQuery.Parse(_box.Text);
        if (query is null) { ShowHits([], 0, 0); return; }
        query.Dirs = _dirs; query.Extensions = _exts;
        Task.Run(() =>
        {
            var clock = Stopwatch.StartNew();
            try { var (hits, total) = _drives.Search(query, Max, cts.Token); hits = Sorted(hits); long ms = clock.ElapsedMilliseconds; BeginInvoke(() => { if (!cts.IsCancellationRequested) ShowHits(hits, total, ms); }); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { }   // the window closed meanwhile
        });
    }

    // a click on a header orders the matches found (at most Max); the order stays for the next searches. The size and date are read from the disk
    // for those matches only (a slow network drive would make this wait).
    private void SortBy(int col)
    {
        _sortUp = _sortCol == col ? !_sortUp : true; _sortCol = col;
        _hits = Sorted(_hits); _rows.Clear(); _list.Invalidate(); _list.Columns[0].Width = _list.Columns[0].Width;
    }

    private List<(FileIndex Index, int Entry)> Sorted(List<(FileIndex Index, int Entry)> hits)
    {
        int col = _sortCol; bool up = _sortUp;
        if (col < 0 || hits.Count < 2) return hits;
        IEnumerable<(FileIndex Index, int Entry)> sorted;
        if (col <= 1) sorted = hits.OrderBy(h => col == 0 ? h.Index.Name(h.Entry) : h.Index.PathOf(h.Entry), StringComparer.CurrentCultureIgnoreCase);
        else
        {
            var stat = hits.ToDictionary(h => h, h => Stat(h.Index, h.Entry));
            sorted = hits.OrderBy(h => col == 2 ? stat[h].Size : stat[h].Ticks);
        }
        return [.. up ? sorted : sorted.Reverse()];
    }

    // size (-1 for a folder or an unreadable one) and last write time of one match
    private static (long Size, long Ticks) Stat(FileIndex x, int e)
    {
        try
        {
            string path = x.PathOf(e);
            if (x.IsDir(e)) { var d = new DirectoryInfo(path); return (-1, d.Exists ? d.LastWriteTimeUtc.Ticks : 0); }
            var f = new FileInfo(path); return f.Exists ? (f.Length, f.LastWriteTimeUtc.Ticks) : (-1, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return (-1, 0); }
    }

    private void ShowHits(List<(FileIndex Index, int Entry)> hits, int total, long ms)
    {
        _hits = hits; _total = total; _ms = ms; _rows.Clear();
        _list.VirtualListSize = hits.Count;
        if (hits.Count > 0) { _list.SelectedIndices.Clear(); _list.SelectedIndices.Add(0); _list.EnsureVisible(0); }
        _list.Invalidate(); ShowState();
    }

    private void ShowState()
    {
        var (ready, all, names) = _drives.State;
        string index = ready < all ? TrayText.SearchReading(ready, all) : TrayText.SearchIndexed(names, all);
        _status.Text = _box.TextLength == 0 ? $"{index} · {_shortcut}" : $"{TrayText.SearchFound(_total, _hits.Count, _ms)} · {index}";
    }

    private ListViewItem Row(int i)
    {
        if (_rows.TryGetValue(i, out var cached)) return cached;
        if (i < 0 || i >= _hits.Count) return new ListViewItem(["", "", "", ""]);   // a closing window asks for rows once more after the matches were let go
        var (x, e) = _hits[i]; string path = x.PathOf(e), name = x.Name(e), folder = Path.GetDirectoryName(path) ?? x.Root;
        string size = "", date = "";
        try
        {
            if (x.IsDir(e)) { var d = new DirectoryInfo(path); if (d.Exists) date = d.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }
            else { var f = new FileInfo(path); if (f.Exists) { size = Bytes(f.Length); date = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); } }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        var item = new ListViewItem([name, folder, size, date]) { Tag = path, ForeColor = x.IsDir(e) ? Theme.Yellow : Text1, ImageIndex = IconOf(path, name, x.IsDir(e)) };
        if (_rows.Count > 2000) _rows.Clear();
        _rows[i] = item; return item;
    }

    // Windows' own small icon: a folder's, or the file kind's by its extension (Blender's for a .blend, Word's for a .docx), made once per kind.
    // A program, shortcut or icon file has its own picture, so it is read from the file itself (once per file; only the rows on screen get here).
    private int IconOf(string path, string name, bool dir)
    {
        string ext = dir ? "" : Path.GetExtension(name).ToLowerInvariant();
        bool own = ext is ".exe" or ".lnk" or ".ico" or ".scr";
        string key = dir ? @"\dir" : own ? path : ext.Length is > 1 and < 16 ? ext : @"\file";
        if (_iconOf.TryGetValue(key, out int i)) return i;
        var info = new ShFileInfo(); i = -1;
        uint flags = 0x100 | 0x1 | (own ? 0u : 0x10u) /* ICON | SMALLICON | USEFILEATTRIBUTES (the kind, not the file) */;
        if (SHGetFileInfo(own ? path : dir ? "folder" : "file" + ext, dir ? 0x10u : 0x80u, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags) != 0 && info.Icon != 0)
        {
            using (var icon = Icon.FromHandle(info.Icon)) _icons.Images.Add(icon);
            DestroyIcon(info.Icon); i = _icons.Images.Count - 1;
        }
        if (_iconOf.Count > 3000) { _iconOf.Clear(); _icons.Images.Clear(); _rows.Clear(); return -1; }   // many programs: start over rather than grow
        _iconOf[key] = i; return i;
    }

    private static string Bytes(long b) => b < 1024 ? FormattableString.Invariant($"{b} B") : b < 1 << 20 ? FormattableString.Invariant($"{b / 1024.0:0.#} KB") : b < 1L << 30 ? FormattableString.Invariant($"{b / 1048576.0:0.#} MB") : FormattableString.Invariant($"{b / 1073741824.0:0.##} GB");

    private string? Selected => _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _hits.Count ? Row(_list.SelectedIndices[0]).Tag as string : null;

    // Explorer opens it: as the signed-in user, whatever program the file belongs to (the tray runs as administrator, its children would too).
    private void Open(bool folder)
    {
        if (Selected is not { } path) return;
        string arg = folder ? $"/select,\"{path}\"" : $"\"{path}\"";
        try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), arg) { UseShellExecute = false })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private void CopyPath() { if (Selected is { } p) try { Clipboard.SetText(p); } catch (ExternalException) { } }

    protected override bool ProcessCmdKey(ref Message msg, Keys keys)
    {
        switch (keys)
        {
            case Keys.Escape: Hide(); return true;
            case Keys.Enter: Open(false); return true;
            case Keys.Control | Keys.Enter: Open(true); return true;
            case Keys.Down or Keys.Up or Keys.PageDown or Keys.PageUp when _box.Focused && _hits.Count > 0:
                int at = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : 0, page = Math.Max(1, _list.ClientSize.Height / Math.Max(1, _list.Font.Height + 6));
                at = Math.Clamp(at + keys switch { Keys.Down => 1, Keys.Up => -1, Keys.PageDown => page, _ => -page }, 0, _hits.Count - 1);
                _list.SelectedIndices.Clear(); _list.SelectedIndices.Add(at); _list.EnsureVisible(at); return true;
            case Keys.Control | Keys.C when !_box.Focused || _box.SelectionLength == 0: CopyPath(); return true;
        }
        return base.ProcessCmdKey(ref msg, keys);
    }

    // turned off in the settings: the rows let go of the index, so its memory can be returned
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _cts?.Cancel(); _hits = []; _rows.Clear(); _icons.Dispose(); }
        base.Dispose(disposing);
    }

    protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } base.OnFormClosing(e); }

    // The shortcut was pressed in another program: Windows lets a background window come forward only when it joins that program's input for a moment.
    private void Front()
    {
        uint front = GetWindowThreadProcessId(GetForegroundWindow(), out _), me = GetCurrentThreadId();
        bool joined = front != 0 && front != me && AttachThreadInput(me, front, true);
        try { SetForegroundWindow(Handle); Activate(); } finally { if (joined) AttachThreadInput(me, front, false); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public nint Icon; public int Index; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool on);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
