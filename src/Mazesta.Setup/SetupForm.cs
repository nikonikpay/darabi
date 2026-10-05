using System.Diagnostics;
namespace Mazesta.Setup;

internal sealed class SetupForm : Form
{
    private const int ContentWidth = 540;   // in 96-dpi pixels; the form scales it with the screen
    private readonly TextBox _folder = new() { Dock = DockStyle.Fill, RightToLeft = RightToLeft.No, Margin = new Padding(6, 3, 6, 3) };
    private readonly CheckBox _desktop = new() { Text = SetupText.Desktop, Checked = true, AutoSize = true }, _launch = new() { Text = SetupText.Launch, Checked = true, AutoSize = true };
    private readonly Button _install = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(120, 34) }, _close = new() { Text = SetupText.Cancel, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(100, 34) };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Visible = false, Margin = new Padding(0, 6, 0, 6) };
    private readonly Label _status = new() { AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
    private readonly List<Label> _wrapping = [];
    private readonly TableLayoutPanel _root = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(18, 14, 18, 14) };
    private bool _busy, _done;

    public SetupForm()
    {
        AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
        Text = SetupText.Title; ClientSize = new Size(ContentWidth + 36, 300); StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); Font = new Font("Segoe UI", 9.5f);
        if (!SetupText.English) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
        string? installed = Installer.InstalledFolder();
        _folder.Text = installed ?? Installer.DefaultFolder; _install.Text = installed is null ? SetupText.Install : SetupText.Update;
        Label Wrap(string text, Font? font = null, Color? color = null, int below = 8)
        {
            var l = new Label { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, below) };
            if (font is not null) l.Font = font; if (color is { } c) l.ForeColor = c; _wrapping.Add(l); return l;
        }
        var browse = new Button { Text = SetupText.Browse, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(90, 30), Margin = new Padding(0, 1, 0, 1) };
        browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = _folder.Text, ShowNewFolderButton = true };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            // A chosen folder that already holds other things gets its own subfolder, so nothing foreign is ever cleared.
            bool foreign = Directory.EnumerateFileSystemEntries(d.SelectedPath).Any() && !File.Exists(Path.Combine(d.SelectedPath, Installer.AppExe));
            _folder.Text = foreign ? Path.Combine(d.SelectedPath, "Mazesta Test") : d.SelectedPath;
        };
        var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.Controls.Add(new Label { Text = SetupText.Folder, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, TextAlign = ContentAlignment.MiddleLeft }, 0, 0); folderRow.Controls.Add(_folder, 1, 0); folderRow.Controls.Add(browse, 2, 0);
        var checks = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        checks.Controls.AddRange([_desktop, _launch]);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.AddRange([_install, _close]);
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // One row per block, each as tall as its text needs at the screen's scale and font; nothing is placed by a fixed height.
        _root.Controls.Add(Wrap(SetupText.Title, new Font("Segoe UI Semibold", 15f), below: 10));
        _root.Controls.Add(Wrap(SetupText.Lede + (installed is null ? "" : " " + SetupText.UpdateNote)));
        _root.Controls.Add(folderRow); _root.Controls.Add(checks);
        _root.Controls.Add(Wrap(SetupText.Admin, color: Color.FromArgb(0x55, 0x55, 0x5F)));
        _root.Controls.Add(_bar); _status.Margin = new Padding(0, 4, 0, 4); _wrapping.Add(_status); _root.Controls.Add(_status); _root.Controls.Add(buttons);
        Controls.Add(_root);
        _install.Click += async (_, _) => await RunAsync();
        _close.Click += (_, _) => Close();
        AcceptButton = _install;
        FormClosing += (_, e) => { if (_busy) e.Cancel = true; };
        _status.TextChanged += (_, _) => Fit();
    }

    protected override void OnLoad(EventArgs e) { base.OnLoad(e); Fit(); }

    /// <summary>Lets the text wrap at the form's width, then makes the form exactly as tall as its content, so no control is ever cut off or overlaps at any scale.</summary>
    private void Fit()
    {
        int width = _root.ClientSize.Width - _root.Padding.Horizontal;
        foreach (var l in _wrapping) l.MaximumSize = new Size(width, 0);
        int height = _root.GetPreferredSize(new Size(_root.ClientSize.Width, 0)).Height;
        if (height != ClientSize.Height) ClientSize = new Size(ClientSize.Width, height);
    }

    private async Task RunAsync()
    {
        if (_done) { Close(); return; }
        if (!Installer.HasPayload) { _status.Text = SetupText.Failed(SetupText.NoPayload); return; }
        if (Installer.Problem(_folder.Text) is { } problem) { _status.Text = problem; return; }
        if (Installer.IsRunning()) { _status.Text = SetupText.Running; return; }
        _busy = true; _install.Enabled = _close.Enabled = _folder.Enabled = false; _bar.Visible = true; _status.Text = SetupText.Replacing; Fit();
        string folder = _folder.Text; bool desktop = _desktop.Checked, launch = _launch.Checked;
        var progress = new Progress<(int Percent, string Text)>(p => { _bar.Value = Math.Clamp(p.Percent, 0, 100); _status.Text = p.Text; });
        try
        {
            await Task.Run(() => Installer.Install(folder, desktop, progress));
            _done = true; _install.Text = SetupText.Close; _install.Enabled = true; _status.Text = SetupText.Done;
            if (launch)
            {
                try { Process.Start(new ProcessStartInfo(Path.Combine(Path.GetFullPath(folder), Installer.AppExe)) { UseShellExecute = true, WorkingDirectory = Path.GetFullPath(folder) }); }
                catch (System.ComponentModel.Win32Exception) { }
                _busy = false; Close();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or PlatformNotSupportedException or System.Runtime.InteropServices.COMException)
        {
            _status.Text = SetupText.Failed(e.Message); _install.Enabled = _folder.Enabled = true;
        }
        finally { _busy = false; _close.Enabled = true; _close.Text = SetupText.Close; }
    }
}
