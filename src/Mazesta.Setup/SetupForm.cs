using System.Diagnostics;
namespace Mazesta.Setup;

internal sealed class SetupForm : Form
{
    private readonly TextBox _folder = new() { Dock = DockStyle.Fill, RightToLeft = RightToLeft.No };
    private readonly CheckBox _desktop = new() { Text = SetupText.Desktop, Checked = true, AutoSize = true }, _launch = new() { Text = SetupText.Launch, Checked = true, AutoSize = true };
    private readonly Button _install = new() { AutoSize = true, MinimumSize = new Size(120, 34) }, _close = new() { Text = SetupText.Cancel, AutoSize = true, MinimumSize = new Size(100, 34) };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Minimum = 0, Maximum = 100, Visible = false };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = false };
    private bool _busy, _done;

    public SetupForm()
    {
        Text = SetupText.Title; ClientSize = new Size(560, 360); StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); Font = new Font("Segoe UI", 9.5f);
        if (!SetupText.English) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
        string? installed = Installer.InstalledFolder();
        _folder.Text = installed ?? Installer.DefaultFolder; _install.Text = installed is null ? SetupText.Install : SetupText.Update;
        var title = new Label { Text = SetupText.Title, Font = new Font("Segoe UI Semibold", 15f), AutoSize = false, Height = 36, Dock = DockStyle.Top };
        var lede = new Label { Text = SetupText.Lede + (installed is null ? "" : "\n" + SetupText.UpdateNote), AutoSize = false, Height = installed is null ? 50 : 74, Dock = DockStyle.Top };
        var browse = new Button { Text = SetupText.Browse, AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = _folder.Text, ShowNewFolderButton = true };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            // A chosen folder that already holds other things gets its own subfolder, so nothing foreign is ever cleared.
            bool foreign = Directory.EnumerateFileSystemEntries(d.SelectedPath).Any() && !File.Exists(Path.Combine(d.SelectedPath, Installer.AppExe));
            _folder.Text = foreign ? Path.Combine(d.SelectedPath, "Mazesta Test") : d.SelectedPath;
        };
        var folderRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, ColumnCount = 3 };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.Controls.Add(new Label { Text = SetupText.Folder, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right }, 0, 0); folderRow.Controls.Add(_folder, 1, 0); folderRow.Controls.Add(browse, 2, 0);
        var checks = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        checks.Controls.AddRange([_desktop, _launch]);
        var note = new Label { Text = SetupText.Admin, AutoSize = false, Height = 40, Dock = DockStyle.Top, ForeColor = Color.FromArgb(0x55, 0x55, 0x5F) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.AddRange([_install, _close]);
        var progress = new Panel { Dock = DockStyle.Bottom, Height = 56 };
        progress.Controls.Add(_status); progress.Controls.Add(_bar);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 8) };
        // Docked to the top in reverse order of appearance; the bottom panels first.
        body.Controls.Add(note); body.Controls.Add(checks); body.Controls.Add(folderRow); body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 }); body.Controls.Add(lede); body.Controls.Add(title);
        body.Controls.Add(buttons); body.Controls.Add(progress);
        Controls.Add(body);
        _install.Click += async (_, _) => await RunAsync();
        _close.Click += (_, _) => Close();
        AcceptButton = _install;
        FormClosing += (_, e) => { if (_busy) e.Cancel = true; };
    }

    private async Task RunAsync()
    {
        if (_done) { Close(); return; }
        if (!Installer.HasPayload) { _status.Text = SetupText.Failed(SetupText.NoPayload); return; }
        if (Installer.Problem(_folder.Text) is { } problem) { _status.Text = problem; return; }
        if (Installer.IsRunning()) { _status.Text = SetupText.Running; return; }
        _busy = true; _install.Enabled = _close.Enabled = _folder.Enabled = false; _bar.Visible = true; _status.Text = SetupText.Replacing;
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
