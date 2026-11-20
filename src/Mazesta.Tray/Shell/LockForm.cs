using Mazesta.Core.Windows; using Mazesta.Diagnostics.Windows;
namespace Mazesta.Tray.Shell;

/// <summary>
/// "What keeps this file open", in a window of its own (Explorer's right-click entry): the programs Windows names, each with a way to close it.
/// Closing asks first and goes through <see cref="FileLocks.Close"/>, which refuses services, Windows' own processes and this program. A check
/// Windows could not answer is said so and is never shown as "nobody".
/// </summary>
internal sealed class LockForm : Form
{
    private static readonly Color Text1 = Theme.Paper, Dim = Theme.Paper2, Bad = Theme.Fail;
    private readonly List<string> _paths; private readonly ListView _list; private readonly Label _status; private readonly Button _closeOne, _closeAll, _again;
    private List<FileLockHolder> _holders = [];

    public LockForm(List<string> paths)
    {
        _paths = paths;
        Theme.Window(this); Text = TrayText.LockTitle; StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(700, 420); Size = new Size(880, 500);
        var what = new Label { Dock = DockStyle.Top, Height = 40, Padding = new Padding(14, 8, 14, 0), RightToLeft = RightToLeft.Yes, ForeColor = Dim,
            Text = $"{TrayText.LockPick}: {(paths.Count == 1 ? paths[0] : TrayText.Many(paths.Count))}", AutoEllipsis = true };
        _status = new Label { Dock = DockStyle.Top, Height = 32, Padding = new Padding(14, 0, 14, 0), Font = Theme.Semi(10.5f), RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.MiddleLeft };   // "left" is the reading start in a right-to-left label
        _list = new ListView { Dock = DockStyle.Fill, View = View.Details, HideSelection = false, MultiSelect = false };
        _list.Columns.Add(TrayText.LockName, 180); _list.Columns.Add(TrayText.LockKind, 110); _list.Columns.Add("PID", 70); _list.Columns.Add(TrayText.LockState, 150); _list.Columns.Add(TrayText.LockPath, 330);
        Theme.List(_list); Theme.DarkScroll(_list); _list.SelectedIndexChanged += (_, _) => Sync();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(10, 8, 10, 6), RightToLeft = RightToLeft.Yes, BackColor = Theme.Ink2 };
        _closeOne = Btn(TrayText.LockCloseOne, "", CloseOne, true); _closeAll = Btn(TrayText.LockCloseAll, "", CloseAll, false); _again = Btn(TrayText.LockAgain, "", Find, false);
        bar.Controls.AddRange([_closeOne, _closeAll, _again]);
        Controls.Add(_list); Controls.Add(_status); Controls.Add(what); Controls.Add(Theme.Banner(Theme.Lock, Theme.Warn, TrayText.LockTitle, TrayText.LockSub)); Controls.Add(bar);
        Shown += (_, _) => { Activate(); Find(); };
    }

    private static Button Btn(string text, string glyph, Action click, bool primary)
    {
        var b = Theme.Button(text, primary, glyph); b.Enabled = false; b.Click += (_, _) => click(); return b;
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
        _status.ForeColor = _holders.Count == 0 ? Theme.Pass : Text1;
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

}
