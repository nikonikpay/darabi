using System.Diagnostics; using System.Globalization; using System.Runtime.InteropServices; using Mazesta.Core.Windows; using Mazesta.Hardware.Files;
namespace Mazesta.Tray.Space;

/// <summary>
/// Where the disk space went, in a window of its own (as WizTree shows it). An NTFS drive's sizes are read straight from its file table in seconds;
/// another drive, or a folder picked, is walked. The folders as a tree, biggest first, each with its share of its folder; or the biggest files of the
/// whole drive; typing finds files and folders by name, biggest first. Folders and files carry Windows' own icons. Double-click or Enter opens a
/// folder in the tree or shows a file in Explorer (as the user, not elevated). Closing the window drops the tree, so its memory is returned.
/// </summary>
internal sealed class SpaceForm : Form
{
    private const int MaxHits = 5000;
    private static readonly Color Back = Color.FromArgb(24, 24, 27), Field = Color.FromArgb(36, 36, 40), On = Color.FromArgb(70, 70, 80), Text1 = Color.FromArgb(235, 235, 240), Dim = Color.FromArgb(150, 150, 160), Folder = Color.FromArgb(240, 200, 90);
    private readonly ComboBox _drive; private readonly TextBox _find; private readonly Button _treeMode, _filesMode; private readonly ListView _list; private readonly Label _status;
    private readonly ImageList _icons = new() { ColorDepth = ColorDepth.Depth32Bit }; private readonly Dictionary<string, int> _iconOf = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _roots = []; private readonly System.Windows.Forms.Timer _typing = new() { Interval = 200 };
    private readonly List<int> _rows = [], _depth = []; private readonly HashSet<int> _open = []; private List<int> _hits = []; private readonly Dictionary<int, ListViewItem> _cache = [];
    private DiskTree? _tree; private CancellationTokenSource? _scan, _search; private bool _flat, _biggest; private int _total; private string _done = "";

    public SpaceForm()
    {
        Text = TrayText.Space; ShowInTaskbar = true; StartPosition = FormStartPosition.Manual; KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10f); BackColor = Back; ForeColor = Text1; MinimumSize = new Size(760, 420);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 4), RightToLeft = RightToLeft.Yes, WrapContents = true };
        _drive = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = Text1, Width = 380, RightToLeft = RightToLeft.No };
        _find = new TextBox { Width = 300, BorderStyle = BorderStyle.FixedSingle, BackColor = Field, ForeColor = Text1, PlaceholderText = TrayText.SpaceFind, RightToLeft = RightToLeft.No };
        _treeMode = Toggle(TrayText.SpaceTree, () => Mode(false)); _filesMode = Toggle(TrayText.SpaceBiggest, () => Mode(true));
        bar.Controls.AddRange([_drive, Toggle(TrayText.SpaceAgain, Scan), _treeMode, _filesMode, _find]);
        _list = new ListView { Dock = DockStyle.Fill, View = View.Details, VirtualMode = true, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None, BackColor = Back, ForeColor = Text1, HeaderStyle = ColumnHeaderStyle.Nonclickable, SmallImageList = _icons };
        _list.Columns.Add(TrayText.SearchName, 420); _list.Columns.Add(TrayText.SearchSize, 100, HorizontalAlignment.Right); _list.Columns.Add(TrayText.SpaceShare, 80, HorizontalAlignment.Right);
        _list.Columns.Add(TrayText.SpaceFiles, 90, HorizontalAlignment.Right); _list.Columns.Add(TrayText.SearchFolder, 420);
        _status = new Label { Dock = DockStyle.Bottom, Height = 26, ForeColor = Dim, TextAlign = ContentAlignment.MiddleRight, RightToLeft = RightToLeft.Yes, Padding = new Padding(8, 0, 8, 0) };
        Controls.Add(_list); Controls.Add(_status); Controls.Add(bar);
        var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        menu.Items.Add(TrayText.SearchOpenFolder, null, (_, _) => Reveal()); menu.Items.Add(TrayText.SearchCopyPath, null, (_, _) => CopyPath());
        _list.ContextMenuStrip = menu;

        _list.RetrieveVirtualItem += (_, e) => e.Item = Row(e.ItemIndex);
        _list.DoubleClick += (_, _) => Act();
        _find.TextChanged += (_, _) => { _typing.Stop(); _typing.Start(); };
        _typing.Tick += (_, _) => { _typing.Stop(); Find(); };
        _drive.SelectionChangeCommitted += (_, _) => Pick();
        HandleCreated += (_, _) => { int on = 1; DwmSetWindowAttribute(Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, 4); };
        _list.HandleCreated += (_, _) => { SetWindowTheme(_list.Handle, "DarkMode_Explorer", null); SetWindowTheme(SendMessage(_list.Handle, 0x101F /* LVM_GETHEADER */, 0, 0), "DarkMode_ItemsView", null); };
        _icons.ImageSize = new Size(LogicalToDeviceUnits(16), LogicalToDeviceUnits(16));
        Drives(); Modes();
    }

    private Button Toggle(string text, Action click)
    {
        var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = Text1, Margin = new Padding(4, 0, 4, 4), Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = 0; b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Opened from the tray menu or the app: in the middle of the screen the mouse is on, the system drive read at once.</summary>
    public void Summon()
    {
        if (!Visible)
        {
            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            var size = new Size(Math.Max(MinimumSize.Width, screen.Width * 70 / 100), Math.Max(MinimumSize.Height, screen.Height * 75 / 100));
            Bounds = new Rectangle(screen.X + (screen.Width - size.Width) / 2, screen.Y + (screen.Height - size.Height) / 2, size.Width, size.Height);
        }
        Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate();
        if (_tree is null && _scan is null) Scan();
    }

    private void Drives()
    {
        string system = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                _drive.Items.Add($"{d.Name}  {d.VolumeLabel}  —  {TrayText.SpaceFree(Bytes(d.AvailableFreeSpace), Bytes(d.TotalSize))}"); _roots.Add(d.Name);
            }
            catch (IOException) { }
        }
        _drive.Items.Add(TrayText.SpaceFolder);
        _drive.SelectedIndex = Math.Max(0, _roots.FindIndex(r => string.Equals(r, system, StringComparison.OrdinalIgnoreCase)));
    }

    // the last item asks for a folder; a folder picked is added above it
    private void Pick()
    {
        if (_drive.SelectedIndex < _roots.Count) { Scan(); return; }
        using var d = new FolderBrowserDialog { Description = TrayText.SpaceFolder, UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (d.ShowDialog(this) != DialogResult.OK) { _drive.SelectedIndex = 0; return; }
        _drive.Items.Insert(_roots.Count, d.SelectedPath); _roots.Add(d.SelectedPath); _drive.SelectedIndex = _roots.Count - 1;
        Scan();
    }

    private void Scan()
    {
        if (_drive.SelectedIndex < 0 || _drive.SelectedIndex >= _roots.Count) return;
        string root = _roots[_drive.SelectedIndex];
        _scan?.Cancel(); _search?.Cancel(); var cts = _scan = new CancellationTokenSource();
        _tree = null; _rows.Clear(); _depth.Clear(); _open.Clear(); _hits = []; _cache.Clear(); _list.VirtualListSize = 0; _list.Invalidate();
        _status.Text = TrayText.SpaceReading(root, null);
        Task.Run(() =>
        {
            var clock = Stopwatch.StartNew(); long next = 0;
            void Say(string text) { if (Environment.TickCount64 < next) return; next = Environment.TickCount64 + 200; Ui(() => { if (cts == _scan) _status.Text = text; }); }
            try
            {
                bool drive = string.Equals(Path.GetPathRoot(root), root, StringComparison.OrdinalIgnoreCase);
                using var ntfs = drive ? NtfsDrive.TryOpen(root, out _) : null;
                var tree = ntfs is not null ? ntfs.Space(cts.Token, p => Say(TrayText.SpaceReading(root, (int)(p * 100)))) : FolderSpace.Walk(root, cts.Token, n => Say(TrayText.SpaceWalking(root, n)));
                double seconds = clock.Elapsed.TotalSeconds; bool table = ntfs is not null;
                Ui(() => { if (cts != _scan) return; _scan = null; _tree = tree; _done = TrayText.SpaceDone(Bytes(tree.Size(tree.RootEntry)), tree.Files(tree.RootEntry), seconds, table); ShowTree(); });
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Ui(() => { if (cts == _scan) { _scan = null; _status.Text = TrayText.SpaceFailed(e.Message); } }); }
        });
    }

    private void ShowTree()
    {
        if (_tree is not { } t) return;
        _rows.Clear(); _depth.Clear(); _open.Clear(); _rows.Add(t.RootEntry); _depth.Add(0);
        Expand(0);
        Find();
    }

    private void Mode(bool biggest) { _biggest = biggest; Modes(); if (_find.TextLength > 0) _find.Clear(); else Find(); }

    private void Modes() { _treeMode.BackColor = _biggest ? Field : On; _filesMode.BackColor = _biggest ? On : Field; }

    // typing, or "biggest files": a flat list found off the window's thread; no text in the tree mode brings the tree back as it was
    private void Find()
    {
        _search?.Cancel();
        if (_tree is not { } t) return;
        string text = _find.Text.Trim();
        if (text.Length == 0 && !_biggest) { _flat = false; ShowRows(0); return; }
        var cts = _search = new CancellationTokenSource();
        Task.Run(() =>
        {
            try { var hits = t.Find(text, folders: text.Length > 0, MaxHits, out int total, cts.Token); Ui(() => { if (cts != _search || t != _tree) return; _hits = hits; _total = total; _flat = true; ShowRows(0); }); }
            catch (OperationCanceledException) { }
        });
    }

    private void ShowRows(int select)
    {
        _cache.Clear();
        int n = _flat ? _hits.Count : _rows.Count;
        _list.VirtualListSize = n; _list.Invalidate();
        if (n > 0) { select = Math.Clamp(select, 0, n - 1); _list.SelectedIndices.Clear(); _list.SelectedIndices.Add(select); _list.EnsureVisible(select); }
        _status.Text = _flat ? $"{TrayText.SpaceFound(_total, _hits.Count)} · {_done}" : _done;
    }

    private void Expand(int row)
    {
        if (_tree is not { } t || _flat) return;
        int e = _rows[row]; if (!t.IsDir(e) || !_open.Add(e)) return;
        var kids = t.Children(e).ToArray();
        _rows.InsertRange(row + 1, kids); _depth.InsertRange(row + 1, Enumerable.Repeat(_depth[row] + 1, kids.Length));
    }

    private void Collapse(int row)
    {
        if (_flat || !_open.Remove(_rows[row])) return;
        int end = row + 1, d = _depth[row];
        while (end < _rows.Count && _depth[end] > d) { _open.Remove(_rows[end]); end++; }
        _rows.RemoveRange(row + 1, end - row - 1); _depth.RemoveRange(row + 1, end - row - 1);
    }

    private int Selected => _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;
    private int EntryAt(int row) => row < 0 ? -1 : _flat ? (row < _hits.Count ? _hits[row] : -1) : (row < _rows.Count ? _rows[row] : -1);

    // a folder of the tree opens or closes; anything else is shown in Explorer
    private void Act()
    {
        int row = Selected, e = EntryAt(row);
        if (e < 0 || _tree is not { } t) return;
        if (!_flat && t.IsDir(e)) { if (_open.Contains(e)) Collapse(row); else Expand(row); ShowRows(row); }
        else Reveal();
    }

    private ListViewItem Row(int row)
    {
        if (_cache.TryGetValue(row, out var cached)) return cached;
        var t = _tree!; int e = EntryAt(row); bool dir = t.IsDir(e);
        string name = t.Name(e);
        if (!_flat) name = (dir ? (_open.Contains(e) ? "▾ " : "▸ ") : "   ") + name;
        string size = t.Unread(e) ? TrayText.SpaceUnread : (t.PartUnread(e) ? "≥ " : "") + Bytes(t.Size(e));
        long whole = _flat || e == t.RootEntry ? t.Size(t.RootEntry) : t.Size(t.Parent(e));
        string share = e == t.RootEntry || whole <= 0 ? "" : (100.0 * t.Size(e) / whole).ToString("0.0", CultureInfo.InvariantCulture) + " %";
        string files = dir ? t.Files(e).ToString("N0", CultureInfo.InvariantCulture) : "";
        string folder = _flat ? t.PathOf(t.Parent(e)) : "";
        var item = new ListViewItem([name, size, share, files, folder]) { ImageIndex = IconOf(t, e), IndentCount = _flat ? 0 : _depth[row], ForeColor = dir ? Folder : Text1 };
        if (_cache.Count > 2000) _cache.Clear();
        _cache[row] = item; return item;
    }

    // Windows' own small icon for a folder or for a file's kind (by its extension, not read from the file), made once per kind
    private int IconOf(DiskTree t, int e)
    {
        bool dir = t.IsDir(e); string ext = dir ? "" : Path.GetExtension(t.Name(e));
        string key = dir ? @"\dir" : ext.Length is > 1 and < 16 ? ext : @"\file";
        if (_iconOf.TryGetValue(key, out int i)) return i;
        var info = new ShFileInfo(); i = -1;
        if (SHGetFileInfo(dir ? "folder" : "file" + (key[0] == '.' ? key : ""), dir ? 0x10u : 0x80u, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), 0x100 | 0x1 | 0x10 /* ICON | SMALLICON | USEFILEATTRIBUTES */) != 0 && info.Icon != 0)
        {
            using (var icon = Icon.FromHandle(info.Icon)) _icons.Images.Add(icon);
            DestroyIcon(info.Icon); i = _icons.Images.Count - 1;
        }
        _iconOf[key] = i; return i;
    }

    private string? SelectedPath => EntryAt(Selected) is int e and >= 0 && _tree is { } t ? t.PathOf(e) : null;

    // Explorer shows it: as the signed-in user (the tray runs as administrator, its children would too)
    private void Reveal()
    {
        if (SelectedPath is not { } path) return;
        string arg = path.Length <= 3 ? $"\"{path}\"" : $"/select,\"{path}\"";
        try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), arg) { UseShellExecute = false })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private void CopyPath() { if (SelectedPath is { } p) try { Clipboard.SetText(p); } catch (ExternalException) { } }

    protected override bool ProcessCmdKey(ref Message msg, Keys keys)
    {
        bool inList = !_find.Focused && !_drive.Focused; int row = Selected;
        switch (keys)
        {
            case Keys.Escape: Close(); return true;
            case Keys.Enter when inList: Act(); return true;
            case Keys.Right when inList && !_flat && row >= 0: Expand(row); ShowRows(row); return true;
            case Keys.Left when inList && !_flat && row >= 0:
                if (_open.Contains(_rows[row])) { Collapse(row); ShowRows(row); }
                else { int up = row; while (up > 0 && _depth[up] >= _depth[row]) up--; ShowRows(up); }
                return true;
            case Keys.Control | Keys.C when inList: CopyPath(); return true;
            case Keys.Control | Keys.F: _find.Focus(); _find.SelectAll(); return true;
        }
        return base.ProcessCmdKey(ref msg, keys);
    }

    private void Ui(Action a) { try { if (!IsDisposed) BeginInvoke(a); } catch (InvalidOperationException) { } }

    private static string Bytes(long b) => b < 1024 ? FormattableString.Invariant($"{b} B") : b < 1 << 20 ? FormattableString.Invariant($"{b / 1024.0:0.#} KB") : b < 1L << 30 ? FormattableString.Invariant($"{b / 1048576.0:0.#} MB") : FormattableString.Invariant($"{b / 1073741824.0:0.##} GB");

    // closed: the scan stops and the tree is let go (the tray returns its memory)
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _scan?.Cancel(); _search?.Cancel(); _tree = null; _hits = []; _rows.Clear(); _cache.Clear(); _typing.Dispose(); _icons.Dispose(); }
        base.Dispose(disposing);
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
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(nint window, string app, string? id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, int message, nint w, nint l);
}
