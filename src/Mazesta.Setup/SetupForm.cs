using System.Diagnostics;
namespace Mazesta.Setup;

/// <summary>The installer's window, drawn with the app's own palette (graphite faceplate, brand yellow marking, flat panels): agreement, folder, progress and finish.
/// Persian by default, English by the button in the header; switching rebuilds the whole window, so the layout direction follows the language.</summary>
internal sealed class SetupForm : Form
{
    private static readonly Color Ink = Color.FromArgb(0x15, 0x17, 0x1A), Ink2 = Color.FromArgb(0x1C, 0x1F, 0x23), Ink3 = Color.FromArgb(0x25, 0x29, 0x30), Ink4 = Color.FromArgb(0x2F, 0x34, 0x3B),
        Well = Color.FromArgb(0x0E, 0x10, 0x12), Paper = Color.FromArgb(0xEC, 0xEB, 0xE6), Paper2 = Color.FromArgb(0xA9, 0xAC, 0xB0), Rule = Color.FromArgb(0x2A, 0x2E, 0x34),
        Yellow = Color.FromArgb(0xFD, 0xD4, 0x00), OnYellow = Color.FromArgb(0x0C, 0x0C, 0x0C), Fail = Color.FromArgb(0xEE, 0x4B, 0x3D);

    private int _page;   // 0 agreement, 1 folder, 2 progress / finish
    private bool _busy, _done, _accepted, _desktop = true, _launch = true;
    private string _folder = Installer.DefaultFolder, _status = "", _statusKind = "";
    private int _percent;
    private string? _earlier; private bool _earlierPerUser;

    public SetupForm()
    {
        AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(700, 560); StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        BackColor = Ink; ForeColor = Paper; Font = new Font("Segoe UI", 9.5f);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        _earlier = Installer.InstalledFolder(out _earlierPerUser);
        if (_earlier is not null && !_earlierPerUser) _folder = _earlier;
        FormClosing += (_, e) => { if (_busy) e.Cancel = true; };
        Rebuild();
    }

    private static Button Btn(string text, bool primary, EventHandler click, bool enabled = true)
    {
        var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(110, 36), Padding = new Padding(10, 0, 10, 0), Enabled = enabled,
            BackColor = primary ? Yellow : Ink4, ForeColor = primary ? OnYellow : Paper, Cursor = Cursors.Hand, UseVisualStyleBackColor = false, Margin = new Padding(6, 0, 0, 0) };
        b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0xFF, 0xE0, 0x3A) : Color.FromArgb(0x3A, 0x40, 0x48);
        if (!enabled) { b.BackColor = Ink3; b.ForeColor = Color.FromArgb(0x73, 0x7A, 0x82); }
        b.Click += click; return b;
    }

    private Label Text_(string text, Font? font = null, Color? color = null, int below = 8)
        => new() { Text = text, AutoSize = false, Dock = DockStyle.Top, ForeColor = color ?? Paper, Font = font ?? Font, Padding = new Padding(0, 0, 0, below), BackColor = Color.Transparent,
            Height = TextRenderer.MeasureText(text, font ?? Font, new Size(620, 0), TextFormatFlags.WordBreak).Height + below + 4 };

    /// <summary>Rebuilds the window for the current page and language. Nothing is positioned by pixel: docked panels, and rows as tall as their text.</summary>
    private void Rebuild()
    {
        SuspendLayout();
        foreach (Control c in Controls.Cast<Control>().ToList()) { Controls.Remove(c); c.Dispose(); }
        bool rtl = !SetupText.English;
        RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No; RightToLeftLayout = rtl;
        Text = SetupText.Title;

        // Footer: the buttons, on the rule line.
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, BackColor = Ink2, Padding = new Padding(18, 13, 18, 0), FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 18, 28, 12), BackColor = Ink };
        var header = BuildHeader();
        Controls.Add(body); Controls.Add(footer); Controls.Add(header);

        // Buttons sit in reading order: the primary one at the end (left in Persian, right in English). FlowDirection LeftToRight with RightToLeft=Yes mirrors by itself.
        if (_page == 0)
        {
            footer.Controls.Add(Btn(SetupText.Cancel, false, (_, _) => Close()));
            footer.Controls.Add(Btn(SetupText.Next, true, (_, _) => { _page = 1; Rebuild(); }, _accepted));
            BuildLicense(body);
        }
        else if (_page == 1)
        {
            footer.Controls.Add(Btn(SetupText.Cancel, false, (_, _) => Close()));
            footer.Controls.Add(Btn(SetupText.Back, false, (_, _) => { _page = 0; Rebuild(); }));
            footer.Controls.Add(Btn(_earlier is null ? SetupText.Install : SetupText.Update, true, async (_, _) => await RunAsync()));
            BuildOptions(body);
        }
        else
        {
            if (_done) footer.Controls.Add(Btn(SetupText.Finish, true, (_, _) => Finish()));
            else if (!_busy) { footer.Controls.Add(Btn(SetupText.Close, false, (_, _) => Close())); footer.Controls.Add(Btn(SetupText.Back, true, (_, _) => { _page = 1; Rebuild(); })); }
            BuildProgress(body);
        }
        ResumeLayout(true);
    }

    private Control BuildHeader()
    {
        var p = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Ink2 };
        var line = new Panel { Dock = DockStyle.Bottom, Height = 3, BackColor = Yellow };
        var logo = new PictureBox { Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Image = Icon?.ToBitmap(), Dock = DockStyle.None };
        var title = new Label { Text = "Mazesta Test", Font = new Font("Segoe UI Semibold", 17f), ForeColor = Paper, AutoSize = true, BackColor = Color.Transparent };
        var step = new Label { Text = string.Join("  ›  ", new[] { SetupText.StepLicense, SetupText.StepOptions, SetupText.StepFinish }.Select((s, i) => i == _page ? "● " + s : s)), ForeColor = Paper2, AutoSize = true, BackColor = Color.Transparent };
        var lang = new Button { Text = SetupText.Language, FlatStyle = FlatStyle.Flat, ForeColor = Paper2, BackColor = Ink3, AutoSize = true, MinimumSize = new Size(86, 30), Cursor = Cursors.Hand, UseVisualStyleBackColor = false, Enabled = !_busy };
        lang.FlatAppearance.BorderSize = 0;
        lang.Click += (_, _) => { SetupText.English = !SetupText.English; Rebuild(); };
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(22, 0, 22, 0), BackColor = Ink2 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var names = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.None, BackColor = Ink2, Margin = new Padding(12, 0, 12, 0) };
        names.Controls.Add(title); names.Controls.Add(step);
        logo.Anchor = AnchorStyles.None; lang.Anchor = AnchorStyles.None;
        row.Controls.Add(logo, 0, 0); row.Controls.Add(names, 1, 0); row.Controls.Add(lang, 2, 0);
        p.Controls.Add(row); p.Controls.Add(line);
        return p;
    }

    private void BuildLicense(Panel body)
    {
        var accept = new CheckBox { Text = SetupText.Accept, Checked = _accepted, AutoSize = true, ForeColor = Paper, Dock = DockStyle.Bottom, Padding = new Padding(0, 8, 0, 0), FlatStyle = FlatStyle.Flat, BackColor = Ink };
        accept.CheckedChanged += (_, _) => { _accepted = accept.Checked; Rebuild(); };
        var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Well, ForeColor = Paper, BorderStyle = BorderStyle.FixedSingle, Text = SetupText.License.Replace("\r\n", "\n").Replace("\n", "\r\n"),
            Font = new Font("Segoe UI", 9.5f), RightToLeft = SetupText.English ? RightToLeft.No : RightToLeft.Yes, TabStop = true };
        var lede = Text_(SetupText.LicenseLede, color: Paper2);
        body.Controls.Add(box); body.Controls.Add(accept); body.Controls.Add(lede);
        box.Select(0, 0); accept.Focus();
    }

    private void BuildOptions(Panel body)
    {
        var folder = new TextBox { Text = _folder, Dock = DockStyle.Fill, BackColor = Well, ForeColor = Paper, BorderStyle = BorderStyle.FixedSingle, RightToLeft = RightToLeft.No, Margin = new Padding(8, 3, 8, 3) };
        folder.TextChanged += (_, _) => _folder = folder.Text;
        var browse = Btn(SetupText.Browse, false, (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = _folder, ShowNewFolderButton = true };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            _folder = d.SelectedPath; Rebuild();
        });
        browse.Margin = new Padding(0); browse.MinimumSize = new Size(90, 30);
        var rowFolder = new TableLayoutPanel { Dock = DockStyle.Top, Height = 40, ColumnCount = 3, RowCount = 1, BackColor = Ink };
        rowFolder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); rowFolder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); rowFolder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rowFolder.Controls.Add(new Label { Text = SetupText.Folder, AutoSize = true, Anchor = AnchorStyles.None, ForeColor = Paper2 }, 0, 0); rowFolder.Controls.Add(folder, 1, 0); rowFolder.Controls.Add(browse, 2, 0);
        folder.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        var desktop = new CheckBox { Text = SetupText.Desktop, Checked = _desktop, AutoSize = true, ForeColor = Paper, Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat, Padding = new Padding(0, 6, 0, 0) };
        desktop.CheckedChanged += (_, _) => _desktop = desktop.Checked;
        var launch = new CheckBox { Text = SetupText.Launch, Checked = _launch, AutoSize = true, ForeColor = Paper, Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat, Padding = new Padding(0, 6, 0, 0) };
        launch.CheckedChanged += (_, _) => _launch = launch.Checked;
        var status = Text_(_status, color: _statusKind == "error" ? Fail : Paper2, below: 4);
        // Added bottom-up: Dock=Top stacks in the reverse order of adding.
        body.Controls.Add(status);
        body.Controls.Add(Text_(SetupText.Admin, color: Paper2, below: 4));
        body.Controls.Add(launch); body.Controls.Add(desktop);
        body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        if (_earlier is not null) body.Controls.Add(Text_(_earlierPerUser ? SetupText.MovedNote : SetupText.UpdateNote, color: Yellow));
        body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        body.Controls.Add(rowFolder);
        body.Controls.Add(Text_(SetupText.Lede, color: Paper2, below: 14));
        body.Controls.Add(Text_(SetupText.OptionsHead, new Font("Segoe UI Semibold", 14f), below: 8));
    }

    private void BuildProgress(Panel body)
    {
        body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        body.Controls.Add(Text_(_status, color: _statusKind == "error" ? Fail : Paper, below: 4));
        var bar = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Well };
        bar.Paint += (_, e) => { using var br = new SolidBrush(Yellow); int w = bar.ClientSize.Width * Math.Max(0, Math.Min(100, _percent)) / 100; if (SetupText.English) e.Graphics.FillRectangle(br, 0, 0, w, bar.Height); else e.Graphics.FillRectangle(br, bar.Width - w, 0, w, bar.Height); };
        bar.Tag = "bar";
        body.Controls.Add(bar);
        body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 14 });
        body.Controls.Add(Text_(SetupText.Title, new Font("Segoe UI Semibold", 14f), below: 8));
    }

    private void Progress((int Percent, string Text) p)
    {
        _percent = p.Percent; _status = p.Text; _statusKind = "";
        foreach (Control c in Controls) UpdateProgress(c);
    }

    private void UpdateProgress(Control c)
    {
        foreach (Control child in c.Controls)
        {
            if (child is Panel { Tag: "bar" } bar) bar.Invalidate();
            else if (child is Label l && l.Dock == DockStyle.Top && l.Padding.Bottom == 4) l.Text = _status;
            UpdateProgress(child);
        }
    }

    private async Task RunAsync()
    {
        if (!Installer.HasPayload) { Fail_(SetupText.NoPayload); return; }
        var state = Installer.Check(_folder);
        if (state == Installer.FolderState.Bad) { Fail_(SetupText.BadFolder); return; }
        if (Installer.IsRunning()) { Fail_(SetupText.Running); return; }
        bool foreign = state == Installer.FolderState.Foreign;
        if (foreign && MessageBox.Show(this, SetupText.NotEmptyAsk, SetupText.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2,
                SetupText.English ? 0 : MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) != DialogResult.Yes) return;
        _busy = true; _done = false; _page = 2; _percent = 0; _status = SetupText.Preparing; _statusKind = ""; Rebuild();
        string folder = _folder; bool desktop = _desktop;
        var progress = new Progress<(int Percent, string Text)>(Progress);
        try
        {
            await Task.Run(() => Installer.Install(folder, desktop, foreign, progress));
            _done = true; _busy = false; _percent = 100; _status = SetupText.Done; _statusKind = "";
            Rebuild();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or PlatformNotSupportedException or System.Runtime.InteropServices.COMException or System.Security.SecurityException)
        {
            _busy = false; _status = SetupText.Failed(e.Message); _statusKind = "error"; Rebuild();
        }
    }

    private void Fail_(string text) { _status = text; _statusKind = "error"; Rebuild(); }

    private void Finish()
    {
        if (_launch)
        {
            try { Process.Start(new ProcessStartInfo(Path.Combine(Path.GetFullPath(_folder), Installer.AppExe)) { UseShellExecute = true, WorkingDirectory = Path.GetFullPath(_folder) }); }
            catch (System.ComponentModel.Win32Exception) { }
        }
        Close();
    }
}
