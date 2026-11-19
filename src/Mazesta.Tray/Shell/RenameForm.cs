using System.Text.RegularExpressions; using Mazesta.Core.Windows; using Mazesta.Diagnostics.Windows;
namespace Mazesta.Tray.Shell;

/// <summary>
/// "Rename these together", in a window of its own (Explorer's right-click entry), with the steps the well-known renamers share (PowerRename,
/// Bulk Rename Utility, Advanced Renamer): find and replace (plain or regular expression), cut and add text, numbering, case, spaces, Persian
/// digits and letters, the file's date, the extension, and the order. The plan is made by <see cref="RenamePlan"/> and shown before anything is
/// touched; the batch runs only when no name clashes, and the last one can be put back (<see cref="BatchRename"/>).
/// </summary>
internal sealed class RenameForm : Form
{
    private static readonly Color Text1 = Theme.Paper, Dim = Theme.Paper2, Bad = Theme.Fail, Good = Theme.Pass;
    private const int ShowMax = 1000;
    private readonly string _log; private List<string> _paths; private IReadOnlyList<RenameItem> _items = []; private bool _ready;
    private readonly FlowLayoutPanel _opts = new() { Dock = DockStyle.Right, Width = 410, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8, 4, 8, 8) };
    private readonly TextBox _find, _replace, _prefix, _suffix, _base, _sep, _ext;
    private readonly CheckBox _regex, _matchCase, _first, _latin, _persian, _time, _shamsi, _extLower, _extChange;
    private readonly NumericUpDown _cutFirst, _cutLast, _start, _step, _digits;
    private readonly ComboBox _number, _case, _spaces, _date, _order;
    private readonly ListView _list; private readonly Label _status, _result; private readonly Button _apply, _undo;

    public RenameForm(List<string> paths, string undoLog)
    {
        _paths = paths; _log = undoLog;
        Theme.Window(this); Text = TrayText.RenameTitle; StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(960, 600); Size = new Size(1240, 760);

        Section(TrayText.RenameSecText);
        _find = Txt(TrayText.RenameFind); _replace = Txt(TrayText.RenameReplace);
        _regex = Chk(TrayText.RenameRegex); _matchCase = Chk(TrayText.RenameCase); _first = Chk(TrayText.RenameFirst);
        Section(TrayText.RenameSecAdd);
        _prefix = Txt(TrayText.RenamePrefix); _suffix = Txt(TrayText.RenameSuffix); _cutFirst = Num(TrayText.RenameCutFirst, 0, 200, 0); _cutLast = Num(TrayText.RenameCutLast, 0, 200, 0);
        Section(TrayText.RenameSecNumber);
        _number = Combo(TrayText.RenameNumberModes); _base = Txt(TrayText.RenameBase); _start = Num(TrayText.RenameStart, 0, 99_999_999, 1); _step = Num(TrayText.RenameStep, 1, 9999, 1);
        _digits = Num(TrayText.RenameDigits, 0, 12, 0); _sep = Txt(TrayText.RenameSep); _sep.Text = " ";
        Section(TrayText.RenameSecStyle);
        _case = Combo(TrayText.RenameCases); _spaces = Combo(TrayText.RenameSpaces); _latin = Chk(TrayText.RenameLatin); _persian = Chk(TrayText.RenamePersian);
        Section(TrayText.RenameSecDate);
        _date = Combo(TrayText.RenameDates); _time = Chk(TrayText.RenameTime); _shamsi = Chk(TrayText.RenameShamsi);
        Section(TrayText.RenameSecExt);
        _extLower = Chk(TrayText.RenameExtLower); _extChange = Chk(TrayText.RenameExtNew); _ext = Txt(null);
        Section(null);
        _order = Combo(TrayText.RenameOrders);

        _list = new ListView { Dock = DockStyle.Fill, View = View.Details, HideSelection = false }; Theme.List(_list); Theme.DarkScroll(_list); Theme.DarkScroll(_opts);
        _list.Columns.Add(TrayText.RenameOld, 260); _list.Columns.Add(TrayText.RenameNew, 260); _list.Columns.Add(TrayText.RenameStatus, 90);
        _status = new Label { Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 0, 8, 0), RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.MiddleLeft };
        _result = new Label { Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 0, 8, 0), RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.MiddleLeft };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(10, 8, 10, 6), RightToLeft = RightToLeft.Yes, BackColor = Theme.Ink2 };
        _apply = Btn(TrayText.RenameApply, Apply, true); _undo = Btn(TrayText.RenameUndo, Undo, false); _undo.Visible = false;
        bar.Controls.AddRange([_apply, _undo, Btn(TrayText.RenameClose, Close, false)]);
        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_list); left.Controls.Add(_result); left.Controls.Add(_status);
        Controls.Add(left); Controls.Add(_opts); Controls.Add(Theme.Banner(Theme.Edit, Theme.Cpu, TrayText.RenameTitle, TrayText.RenameSub)); Controls.Add(bar);
        Shown += (_, _) => { Activate(); _ready = true; Sync(); Preview(); RefreshUndo(); };
    }

    // ——— the options column, built top to bottom ———
    private void Section(string? title)
    {
        if (title is null) return;
        _opts.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(0, 20, 0, 4), Font = new Font("Segoe UI Semibold", 11f), ForeColor = Theme.Yellow, RightToLeft = RightToLeft.Yes });
    }
    private TextBox Txt(string? label)
    {
        if (label is not null) _opts.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Dim, RightToLeft = RightToLeft.Yes, Margin = new Padding(0, 6, 0, 2) });
        var t = new TextBox { Width = 370 }; Theme.Field(t); t.TextChanged += (_, _) => Changed(); _opts.Controls.Add(t); return t;
    }
    private NumericUpDown Num(string label, int min, int max, int value)
    {
        _opts.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Dim, RightToLeft = RightToLeft.Yes, Margin = new Padding(0, 6, 0, 2) });
        var n = new NumericUpDown { Width = 140, Minimum = min, Maximum = max, Value = value, BackColor = Theme.Ink3, ForeColor = Text1, BorderStyle = BorderStyle.FixedSingle }; n.ValueChanged += (_, _) => Changed(); _opts.Controls.Add(n); return n;
    }
    private CheckBox Chk(string label)
    {
        var c = Theme.Chip(label); c.CheckedChanged += (_, _) => Changed(); _opts.Controls.Add(c); return c;
    }
    private ComboBox Combo(string[] items)
    {
        var c = new ComboBox { Width = 370, Margin = new Padding(0, 6, 0, 2) }; Theme.Combo(c);
        c.Items.AddRange(items); c.SelectedIndex = 0; c.SelectedIndexChanged += (_, _) => Changed(); _opts.Controls.Add(c); return c;
    }
    private static Button Btn(string text, Action click, bool primary)
    {
        var b = Theme.Button(text, primary); b.Click += (_, _) => click(); return b;
    }

    // ——— the rule from the controls, the plan from the rule ———
    private void Changed() { if (!_ready) return; Sync(); Preview(); }

    // what does not apply to the chosen numbering is dimmed, so the column shows what each choice uses
    private void Sync()
    {
        int n = _number.SelectedIndex; bool numbered = n > 0;
        _base.Enabled = n == 1; _start.Enabled = _step.Enabled = _digits.Enabled = _sep.Enabled = numbered;
        _find.Enabled = _regex.Enabled = _matchCase.Enabled = _first.Enabled = n != 1;
        _time.Enabled = _shamsi.Enabled = _date.SelectedIndex > 0; _ext.Enabled = _extChange.Checked;
    }

    private RenameRule Rule()
    {
        int n = _number.SelectedIndex, sp = _spaces.SelectedIndex;
        return new RenameRule(_find.Text, n == 1 ? _base.Text : _replace.Text, _regex.Checked, _matchCase.Checked, n > 0, (int)_start.Value, _first.Checked, (int)_cutFirst.Value, (int)_cutLast.Value, _prefix.Text, _suffix.Text,
            KeepName: n >= 2, NumberFirst: n == 3, Step: (int)_step.Value, Digits: (int)_digits.Value, Separator: _sep.Text, Case: (RenameCase)_case.SelectedIndex, TrimSpaces: sp >= 1,
            SpaceTo: sp switch { 2 => '_', 3 => '-', 4 => '.', _ => null }, LatinDigits: _latin.Checked, PersianLetters: _persian.Checked, Date: (RenameDate)_date.SelectedIndex, WithTime: _time.Checked,
            Shamsi: _shamsi.Checked, NewExtension: _extChange.Checked ? _ext.Text : null, LowerExtension: _extLower.Checked);
    }

    // the files in the chosen order, which is what numbering follows
    private List<string> Ordered()
    {
        var files = _paths.Select(p => new FileInfo(p)).ToList();
        IEnumerable<FileInfo> sorted = _order.SelectedIndex switch
        {
            1 => files.OrderBy(f => f.LastWriteTimeUtc), 2 => files.OrderByDescending(f => f.LastWriteTimeUtc),
            3 => files.OrderBy(f => f.Exists ? f.Length : 0), 4 => files.OrderByDescending(f => f.Exists ? f.Length : 0),
            _ => files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        return [.. sorted.Select(f => f.FullName)];
    }

    private void Preview()
    {
        _list.BeginUpdate(); _list.Items.Clear(); _items = []; _apply.Enabled = false;
        try
        {
            var rule = Rule();
            if (RenamePlan.Check(rule) is { } bad) { Say(_status, TrayText.RenameBadRegex(bad), Bad); return; }
            _items = RenamePlan.Make(Ordered(), rule, File.Exists);
            foreach (var i in _items.Take(ShowMax))
                _list.Items.Add(new ListViewItem([Path.GetFileName(i.Path), i.State == RenameState.Same ? "" : i.NewName, i.State switch { RenameState.Same => TrayText.RenameSame, RenameState.Clash => TrayText.RenameClash, RenameState.Bad => TrayText.RenameBad, _ => "✓" }])
                { ForeColor = i.State is RenameState.Clash or RenameState.Bad ? Bad : i.State == RenameState.Same ? Dim : Text1 });
            int ready = _items.Count(i => i.State == RenameState.Ready), blocked = _items.Count(i => i.State is RenameState.Clash or RenameState.Bad);
            _apply.Enabled = ready > 0 && blocked == 0;
            if (blocked > 0) Say(_status, TrayText.RenameBlockedN(blocked), Bad); else Say(_status, TrayText.RenameReadyN(ready, _items.Count), ready > 0 ? Good : Dim);
        }
        catch (RegexMatchTimeoutException) { Say(_status, TrayText.RenameSlow, Bad); }
        finally { _list.EndUpdate(); }
    }

    private static void Say(Label l, string text, Color c) { l.Text = text; l.ForeColor = c; }

    private async void Apply()
    {
        var items = _items; if (items.Count == 0 || items.Any(i => i.State is RenameState.Clash or RenameState.Bad)) return;
        _apply.Enabled = false;
        BatchRename.Outcome r;
        try { r = await Task.Run(() => BatchRename.Apply(items, _log)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { if (!IsDisposed) { Say(_result, e.Message, Bad); Preview(); } return; }
        if (IsDisposed) return;
        var failed = r.Failed.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // the files keep their new names for the next round of changes
        _paths = [.. items.Select(i => i.State == RenameState.Ready && !failed.Contains(i.Path) ? Path.Combine(Path.GetDirectoryName(i.Path)!, i.NewName) : i.Path)];
        Say(_result, TrayText.RenameDone(r.Renamed) + (r.Failed.Count > 0 ? TrayText.RenameFailed(string.Join("، ", r.Failed.Select(f => Path.GetFileName(f.Path)))) : ""), r.Failed.Count > 0 ? Bad : Good);
        RefreshUndo(); Preview();
    }

    private async void Undo()
    {
        var steps = BatchRename.Read(_log); _undo.Enabled = false;
        BatchRename.Outcome r;
        try { r = await Task.Run(() => BatchRename.Undo(_log)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { if (!IsDisposed) { Say(_result, e.Message, Bad); RefreshUndo(); } return; }
        if (IsDisposed) return;
        var back = steps.ToDictionary(s => s.To, s => s.From, StringComparer.OrdinalIgnoreCase);
        var failed = r.Failed.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _paths = [.. _paths.Select(p => back.TryGetValue(p, out var from) && !failed.Contains(p) ? from : p)];
        Say(_result, TrayText.RenameUndone(r.Renamed) + (r.Failed.Count > 0 ? TrayText.RenameFailed(string.Join("، ", r.Failed.Select(f => Path.GetFileName(f.Path)))) : ""), r.Failed.Count > 0 ? Bad : Good);
        RefreshUndo(); Preview();
    }

    private void RefreshUndo() { int n = BatchRename.Read(_log).Count; _undo.Visible = n > 0; _undo.Enabled = n > 0; _undo.Text = $"{TrayText.RenameUndo} ({n})"; }

}
