using System.Globalization; using Mazesta.Desktop.Services;
namespace Mazesta.Tray.Shell;

/// <summary>
/// "Make these pictures smaller", in a window of its own (Explorer's right-click entry): the longest side and the quality are chosen here, and a
/// new file is written beside each picture (<see cref="ImageResizer"/>, shared with the app); the originals are never touched. Each picture's
/// result is listed as it is done.
/// </summary>
internal sealed class ResizeForm : Form
{
    private readonly List<string> _paths; private readonly ComboBox _size, _quality; private readonly TextBox _custom; private readonly ListView _list; private readonly Label _status; private readonly Button _run;

    public ResizeForm(List<string> paths)
    {
        _paths = paths;
        Theme.Window(this); Text = TrayText.ResizeTitle; StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(760, 480); Size = new Size(940, 580);
        var opts = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(12, 10, 12, 0), RightToLeft = RightToLeft.Yes, BackColor = Theme.Ink2 };
        _size = new ComboBox { Width = 150, Margin = new Padding(4, 4, 12, 0) }; Theme.Combo(_size);
        foreach (int n in new[] { 800, 1280, 1920, 2560 }) _size.Items.Add($"{n} px"); _size.Items.Add(TrayText.ResizeCustom); _size.SelectedIndex = 1;
        _custom = new TextBox { Width = 90, Visible = false, PlaceholderText = "1600", Margin = new Padding(4, 6, 12, 0) }; Theme.Field(_custom);
        _size.SelectedIndexChanged += (_, _) => _custom.Visible = _size.SelectedIndex == _size.Items.Count - 1;
        _quality = new ComboBox { Width = 170, Margin = new Padding(4, 4, 12, 0) }; Theme.Combo(_quality); _quality.Items.AddRange(TrayText.ResizeQualities); _quality.SelectedIndex = 1;
        _run = Theme.Button(TrayText.ResizeRun, true, ""); _run.Click += (_, _) => Run();
        opts.Controls.AddRange([Label(TrayText.ResizeSize), _size, _custom, Label(TrayText.ResizeQuality), _quality, _run]);
        _list = new ListView { Dock = DockStyle.Fill, View = View.Details, HideSelection = false };
        _list.Columns.Add(TrayText.ResizeFile, 300); _list.Columns.Add(TrayText.ResizeResult, 560); Theme.List(_list); Theme.DarkScroll(_list);
        _status = new Label { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(14, 0, 14, 0), Font = Theme.Semi(10.5f), RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Ink2, ForeColor = Theme.Paper2, Text = TrayText.Many(paths.Count) };
        foreach (var p in paths) _list.Items.Add(new ListViewItem([Path.GetFileName(p), ""]) { ForeColor = Theme.Paper });
        Controls.Add(_list); Controls.Add(opts); Controls.Add(Theme.Banner(Theme.Picture, Theme.Ram, TrayText.ResizeTitle, TrayText.ResizeSub)); Controls.Add(_status);
    }

    private static Label Label(string text) => new() { Text = text, AutoSize = true, ForeColor = Theme.Paper2, Margin = new Padding(0, 8, 6, 0), RightToLeft = RightToLeft.Yes };

    private async void Run()
    {
        int longest = 0;
        bool ok = _size.SelectedIndex < _size.Items.Count - 1 ? int.TryParse(_size.Text.Replace(" px", ""), out longest) : int.TryParse(_custom.Text.Trim(), out longest);
        if (!ok || longest is < 16 or > 20000) { _status.ForeColor = Theme.Fail; _status.Text = TrayText.ResizeBadSize; return; }
        int quality = _quality.SelectedIndex switch { 0 => 95, 2 => 70, _ => 85 }, done = 0;
        _run.Enabled = false; _status.ForeColor = Theme.Paper2;
        for (int i = 0; i < _paths.Count; i++)
        {
            _status.Text = TrayText.ResizeProgress(i + 1, _paths.Count); string path = _paths[i];
            var r = await Task.Run(() => ImageResizer.Resize(path, longest, quality));
            if (IsDisposed) return;
            var row = _list.Items[i];
            if (r.Output is not null) { done++; row.SubItems[1].Text = $"{r.Width}×{r.Height} → {r.NewWidth}×{r.NewHeight} · {Bytes(r.OldBytes)} → {Bytes(r.NewBytes)} · {Path.GetFileName(r.Output)}"; row.ForeColor = Theme.Pass; }
            else { row.SubItems[1].Text = r.Error == "format" ? TrayText.ResizeFormat : r.Error ?? ""; row.ForeColor = Theme.Fail; }
            _list.Invalidate();
        }
        if (IsDisposed) return;
        _status.ForeColor = done == _paths.Count ? Theme.Pass : Theme.Fail; _status.Text = TrayText.ResizeDone(done, _paths.Count); _run.Enabled = true;
    }

    private static string Bytes(long b) => b < 1024 ? FormattableString.Invariant($"{b} B") : b < 1 << 20 ? FormattableString.Invariant($"{b / 1024.0:0.#} KB") : FormattableString.Invariant($"{b / 1048576.0:0.#} MB");
}
