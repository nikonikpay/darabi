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
    private static readonly Color Back = Color.FromArgb(24, 24, 27), Field = Color.FromArgb(36, 36, 40), Text1 = Color.FromArgb(235, 235, 240), Dim = Color.FromArgb(150, 150, 160);
    private readonly DriveSet _drives; private readonly string _shortcut;
    private readonly TextBox _box; private readonly ListView _list; private readonly Label _status;
    private List<(FileIndex Index, int Entry)> _hits = []; private readonly Dictionary<int, ListViewItem> _rows = [];
    private CancellationTokenSource? _cts; private int _total; private long _ms;

    public SearchForm(DriveSet drives, string shortcut)
    {
        _drives = drives; _shortcut = shortcut;
        Text = TrayText.SearchTitle; ShowInTaskbar = true; StartPosition = FormStartPosition.Manual; KeyPreview = true; TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10f); BackColor = Back; ForeColor = Text1; MinimumSize = new Size(640, 360);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        _box = new TextBox { Dock = DockStyle.Top, Font = new Font("Segoe UI", 14f), BorderStyle = BorderStyle.FixedSingle, BackColor = Field, ForeColor = Text1, PlaceholderText = TrayText.SearchHint };
        _list = new ListView { Dock = DockStyle.Fill, View = View.Details, VirtualMode = true, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None, BackColor = Back, ForeColor = Text1, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        _list.Columns.Add(TrayText.SearchName, 300); _list.Columns.Add(TrayText.SearchFolder, 460); _list.Columns.Add(TrayText.SearchSize, 90, HorizontalAlignment.Right); _list.Columns.Add(TrayText.SearchDate, 140);
        _status = new Label { Dock = DockStyle.Bottom, Height = 26, ForeColor = Dim, TextAlign = ContentAlignment.MiddleRight, RightToLeft = RightToLeft.Yes, Padding = new Padding(8, 0, 8, 0) };
        Controls.Add(_list); Controls.Add(_status); Controls.Add(_box);
        var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        menu.Items.Add(TrayText.SearchOpen, null, (_, _) => Open(false)); menu.Items.Add(TrayText.SearchOpenFolder, null, (_, _) => Open(true)); menu.Items.Add(TrayText.SearchCopyPath, null, (_, _) => CopyPath());
        _list.ContextMenuStrip = menu;

        _box.TextChanged += (_, _) => Find();
        _list.RetrieveVirtualItem += (_, e) => e.Item = Row(e.ItemIndex);
        _list.DoubleClick += (_, _) => Open(false);
        Deactivate += (_, _) => Hide();
        // dark like the app: the title bar, the list's scroll bars, and its header (a window of its own inside the list)
        HandleCreated += (_, _) => { int on = 1; DwmSetWindowAttribute(Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, 4); };
        _list.HandleCreated += (_, _) => { SetWindowTheme(_list.Handle, "DarkMode_Explorer", null); SetWindowTheme(SendMessage(_list.Handle, 0x101F /* LVM_GETHEADER */, 0, 0), "DarkMode_ItemsView", null); };
        _drives.Changed += () => { try { BeginInvoke(() => { if (Visible) Find(); else ShowState(); }); } catch (InvalidOperationException) { } };
    }

    /// <summary>Opened by the shortcut or the menu: in the middle of the screen the mouse is on, in front, the drives brought up to date first.</summary>
    public void Summon()
    {
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        if (!Visible) { var size = new Size(Math.Max(MinimumSize.Width, screen.Width * 55 / 100), Math.Max(MinimumSize.Height, screen.Height * 60 / 100)); Bounds = new Rectangle(screen.X + (screen.Width - size.Width) / 2, screen.Y + (screen.Height - size.Height) / 3, size.Width, size.Height); }
        Show(); Front(); _box.Focus(); _box.SelectAll();
        Task.Run(() => { _drives.Refresh(); try { BeginInvoke(Find); } catch (InvalidOperationException) { } });
    }

    private void Find()
    {
        _cts?.Cancel(); var cts = _cts = new CancellationTokenSource();
        var query = FileQuery.Parse(_box.Text);
        if (query is null) { ShowHits([], 0, 0); return; }
        Task.Run(() =>
        {
            var clock = Stopwatch.StartNew();
            try { var (hits, total) = _drives.Search(query, Max, cts.Token); long ms = clock.ElapsedMilliseconds; BeginInvoke(() => { if (!cts.IsCancellationRequested) ShowHits(hits, total, ms); }); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { }   // the window closed meanwhile
        });
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
        var (x, e) = _hits[i]; string path = x.PathOf(e), name = x.Name(e), folder = Path.GetDirectoryName(path) ?? x.Root;
        string size = "", date = "";
        try
        {
            if (x.IsDir(e)) { var d = new DirectoryInfo(path); if (d.Exists) date = d.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }
            else { var f = new FileInfo(path); if (f.Exists) { size = Bytes(f.Length); date = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); } }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        var item = new ListViewItem([name, folder, size, date]) { Tag = path, ForeColor = x.IsDir(e) ? Color.FromArgb(240, 200, 90) : Text1 };
        if (_rows.Count > 2000) _rows.Clear();
        _rows[i] = item; return item;
    }

    private static string Bytes(long b) => b < 1024 ? FormattableString.Invariant($"{b} B") : b < 1 << 20 ? FormattableString.Invariant($"{b / 1024.0:0.#} KB") : b < 1L << 30 ? FormattableString.Invariant($"{b / 1048576.0:0.#} MB") : FormattableString.Invariant($"{b / 1073741824.0:0.##} GB");

    private string? Selected => _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _hits.Count ? Row(_list.SelectedIndices[0]).Tag as string : null;

    // Explorer opens it: as the signed-in user, whatever program the file belongs to (the tray runs as administrator, its children would too).
    private void Open(bool folder)
    {
        if (Selected is not { } path) return;
        string arg = folder ? $"/select,\"{path}\"" : $"\"{path}\"";
        try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), arg) { UseShellExecute = false })?.Dispose(); Hide(); }
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
        if (disposing) { _cts?.Cancel(); _hits = []; _rows.Clear(); }
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

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(nint window, string app, string? id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, int message, nint w, nint l);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool on);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
