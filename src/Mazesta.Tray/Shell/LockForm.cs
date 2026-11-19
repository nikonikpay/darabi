using Mazesta.Core.Windows; using Mazesta.Diagnostics.Windows;
namespace Mazesta.Tray.Shell;

/// <summary>
/// "What keeps this file open", in a window of its own (Explorer's right-click entry): the programs Windows names, each with a way to close it.
/// Closing asks first and goes through <see cref="FileLocks.Close"/>, which refuses services, Windows' own processes and this program. A check
/// Windows could not answer is said so and is never shown as "nobody".
/// </summary>
internal sealed class LockForm : Form
{
    private static readonly Color Back = Color.FromArgb(24, 24, 27), Field = Color.FromArgb(36, 36, 40), Text1 = Color.FromArgb(235, 235, 240), Dim = Color.FromArgb(150, 150, 160), Bad = Color.FromArgb(240, 120, 110);
    private readonly List<string> _paths; private readonly ListView _list; private readonly Label _status; private readonly Button _closeOne, _closeAll, _again;
    private List<FileLockHolder> _holders = [];

    public LockForm(List<string> paths)
    {
        _paths = paths;
        Text = TrayText.LockTitle; StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10f);
        BackColor = Back; ForeColor = Text1; MinimumSize = new Size(640, 360); Size = new Size(820, 460);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        var what = new Label { Dock = DockStyle.Top, Height = 52, Padding = new Padding(10, 8, 10, 0), RightToLeft = RightToLeft.Yes, ForeColor = Dim,
            Text = $"{TrayText.LockPick}: {(paths.Count == 1 ? paths[0] : TrayText.Many(paths.Count))}", AutoEllipsis = true };
        _status = new Label { Dock = DockStyle.Top, Height = 30, Padding = new Padding(10, 0, 10, 0), RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.MiddleLeft };   // "left" is the reading start in a right-to-left label
        _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None, BackColor = Field, ForeColor = Text1, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        _list.Columns.Add(TrayText.LockName, 180); _list.Columns.Add(TrayText.LockKind, 110); _list.Columns.Add("PID", 70); _list.Columns.Add(TrayText.LockState, 150); _list.Columns.Add(TrayText.LockPath, 330);
        _list.SelectedIndexChanged += (_, _) => Sync();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8, 8, 8, 6), RightToLeft = RightToLeft.Yes };
        _closeOne = Btn(TrayText.LockCloseOne, CloseOne); _closeAll = Btn(TrayText.LockCloseAll, CloseAll); _again = Btn(TrayText.LockAgain, Find);
        bar.Controls.AddRange([_closeOne, _closeAll, _again]);
        Controls.Add(_list); Controls.Add(_status); Controls.Add(what); Controls.Add(bar);
        HandleCreated += (_, _) => { int dark = 1; _ = DwmSetWindowAttribute(Handle, 20, ref dark, 4); };
        Shown += (_, _) => { Activate(); Find(); };
    }

    private Button Btn(string text, Action click)
    {
        var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = Text1, Padding = new Padding(8, 2, 8, 2), Enabled = false };
        b.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 80); b.Click += (_, _) => click(); return b;
    }

    private async void Find()
    {
        _again.Enabled = _closeOne.Enabled = _closeAll.Enabled = false; _status.ForeColor = Dim; _status.Text = TrayText.LockWorking; _list.Items.Clear();
        FileLockReport r;
        try { r = await Task.Run(() => FileLocks.Find(_paths)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { r = new([], 0, false, e.Message); }
        if (IsDisposed) return;
        _again.Enabled = true; _holders = [.. r.Holders];
        if (r.Error is not null) { _status.ForeColor = Bad; _status.Text = TrayText.LockFailed(r.Error); return; }
        _status.ForeColor = _holders.Count == 0 ? Color.FromArgb(120, 210, 140) : Text1;
        _status.Text = (_holders.Count == 0 ? TrayText.LockNone : TrayText.LockFound(_holders.Count)) + (r.Truncated ? " " + TrayText.LockTruncated : "");
        foreach (var h in _holders)
            _list.Items.Add(new ListViewItem([h.Name, Kind(h.Kind), h.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture), h.CanClose ? TrayText.LockCan : h.Kind == FileLockKind.Service ? TrayText.LockService : TrayText.LockSystem, h.Path ?? ""]));
        if (_list.Items.Count > 0) _list.Items[0].Selected = true;
        Sync();
    }

    // Windows' "critical" flag is not shown: it also marks ordinary elevated programs, and what may not be closed is said in the state column
    private static string Kind(FileLockKind k) => k switch { FileLockKind.Service => "سرویس", FileLockKind.Explorer => "Explorer", FileLockKind.Console => "کنسول", _ => "برنامه" };

    private void Sync() { _closeOne.Enabled = _list.SelectedIndices.Count == 1 && _holders[_list.SelectedIndices[0]].CanClose; _closeAll.Enabled = _holders.Any(h => h.CanClose); }

    private bool Ask(string text) => MessageBox.Show(this, text, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) == DialogResult.Yes;

    private void CloseOne() { if (_list.SelectedIndices.Count == 1 && _holders[_list.SelectedIndices[0]] is { CanClose: true } h && Ask(TrayText.LockAsk(h.Name))) End([h]); }
    private void CloseAll() { var all = _holders.Where(h => h.CanClose).ToList(); if (all.Count > 0 && Ask(all.Count == 1 ? TrayText.LockAsk(all[0].Name) : TrayText.LockAskAll(all.Count))) End(all); }

    private async void End(List<FileLockHolder> holders)
    {
        _closeOne.Enabled = _closeAll.Enabled = _again.Enabled = false; var failed = new List<string>();
        foreach (var h in holders)
        {
            string? error = await Task.Run(() => FileLocks.Close(h));
            if (error is not null) failed.Add(TrayText.LockCloseFailed(h.Name, error));
        }
        if (IsDisposed) return;
        Find();
        if (failed.Count > 0) MessageBox.Show(this, string.Join("\n", failed), Text, MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
