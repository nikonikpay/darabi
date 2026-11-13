using System.Runtime.InteropServices; using Mazesta.Hardware.Audio;
namespace Mazesta.Tray.Volume;

/// <summary>
/// Each program's volume beside the tray, like Windows' volume mixer but one click away: the output device's own slider, then a slider and a
/// mute button for every program with sound on it (Windows' own sounds last). Opened from the tray menu or the app; it hides when another
/// window is clicked or Esc is pressed. While open it reads Windows' list once a second (a program that starts playing appears); closed, nothing runs.
/// </summary>
internal sealed class VolumeForm : Form
{
    private static readonly Color Back = Color.FromArgb(28, 28, 32), Line = Color.FromArgb(48, 48, 54), Text1 = Color.FromArgb(235, 235, 240), Dim = Color.FromArgb(150, 150, 160);
    private readonly ComboBox _device; private readonly FlowLayoutPanel _rows; private readonly System.Windows.Forms.Timer _poll = new() { Interval = 1000 };
    private readonly Dictionary<string, Row> _byKey = []; private IReadOnlyList<AudioDevice> _devices = []; private string? _deviceId;
    private Row? _master;

    public VolumeForm()
    {
        Text = TrayText.Mixer; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9.5f); BackColor = Back; ForeColor = Text1; Padding = new Padding(12, 10, 12, 10);
        var title = new Label { Text = TrayText.Mixer, Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI Semibold", 11f), RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.MiddleRight };
        _device = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Line, ForeColor = Text1 };
        _rows = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 6, 0, 0) };
        Controls.Add(_rows); Controls.Add(_device); Controls.Add(title);
        _device.SelectedIndexChanged += (_, _) => { if (_device.SelectedIndex >= 0 && _device.SelectedIndex < _devices.Count && _devices[_device.SelectedIndex].Id != _deviceId) { _deviceId = _devices[_device.SelectedIndex].Id; Rebuild(); } };
        _poll.Tick += (_, _) => Poll();
        Deactivate += (_, _) => { if (!_device.DroppedDown) Hide(); };
        VisibleChanged += (_, _) => _poll.Enabled = Visible;
        HandleCreated += (_, _) => { int dark = 1, round = 2; DwmSetWindowAttribute(Handle, 20, ref dark, 4); DwmSetWindowAttribute(Handle, 33 /* corner preference: round */, ref round, 4); };
    }

    /// <summary>Shown above the tray, at the corner of the screen the mouse is on, with the default device's programs.</summary>
    public void Summon()
    {
        _deviceId = null; Rebuild();
        var area = Screen.FromPoint(Cursor.Position).WorkingArea; int w = LogicalToDeviceUnits(380);
        int h = Math.Min(area.Height * 7 / 10, LogicalToDeviceUnits(110) + _rows.Controls.Count * LogicalToDeviceUnits(58));
        Bounds = new Rectangle(area.Right - w - LogicalToDeviceUnits(12), area.Bottom - h - LogicalToDeviceUnits(12), w, h);
        Show(); Front();
    }

    private void Rebuild()
    {
        try { _devices = AppVolumes.Devices(); }
        catch (Exception e) when (e is COMException or InvalidOperationException) { _devices = []; }
        _deviceId ??= _devices.FirstOrDefault(d => d.Default)?.Id;
        _device.BeginUpdate(); _device.Items.Clear();
        foreach (var d in _devices) _device.Items.Add(d.Default ? $"{d.Name}  ({TrayText.MixerDefault})" : d.Name);
        _device.SelectedIndex = _devices.ToList().FindIndex(d => d.Id == _deviceId); _device.EndUpdate();

        _rows.SuspendLayout(); foreach (Control c in _rows.Controls) c.Dispose(); _rows.Controls.Clear(); _byKey.Clear();
        var device = _devices.FirstOrDefault(d => d.Id == _deviceId);
        if (device is null) { _rows.Controls.Add(Note(TrayText.MixerNoDevice)); _rows.ResumeLayout(); return; }
        _master = new Row(TrayText.MixerDevice, SystemIcons.Information.ToBitmap(), true, v => Try(() => AppVolumes.SetDevice(_deviceId, v)), m => Try(() => AppVolumes.MuteDevice(_deviceId, m)));
        _master.Set(device.Volume, device.Muted); _rows.Controls.Add(_master);
        foreach (var a in Apps()) Add(a);
        if (_byKey.Count == 0) _rows.Controls.Add(Note(TrayText.MixerNone));
        _rows.ResumeLayout(); Fit();
    }

    // Once a second while open: values follow changes made elsewhere (not under the mouse), a program that came or went rebuilds the list.
    private void Poll()
    {
        var apps = Apps();
        if (!apps.Select(a => a.Key).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(_byKey.Keys)) { Rebuild(); return; }
        foreach (var a in apps) _byKey[a.Key].Set(a.Volume, a.Muted, a.Playing);
        try { if (AppVolumes.Devices().FirstOrDefault(d => d.Id == _deviceId) is { } d) _master?.Set(d.Volume, d.Muted); } catch (COMException) { }
    }

    private IReadOnlyList<AudioApp> Apps()
    {
        try { return AppVolumes.Apps(_deviceId); }
        catch (Exception e) when (e is COMException or InvalidOperationException) { return []; }   // the device was unplugged meanwhile
    }

    private void Add(AudioApp a)
    {
        Image? icon = null;
        try { if (a.Path is { } p) icon = Icon.ExtractAssociatedIcon(p)?.ToBitmap(); } catch (Exception e) when (e is IOException or ArgumentException) { }
        string key = a.Key;
        var row = new Row(a.System ? TrayText.MixerWindows : a.Name, icon ?? SystemIcons.Application.ToBitmap(), false, v => Try(() => AppVolumes.SetApp(key, v, _deviceId)), m => Try(() => AppVolumes.MuteApp(key, m, _deviceId)));
        row.Set(a.Volume, a.Muted, a.Playing); _rows.Controls.Add(row); _byKey[key] = row;
    }

    private void Fit() { foreach (Control c in _rows.Controls) c.Width = _rows.ClientSize.Width - (_rows.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0) - 2; }
    protected override void OnResize(EventArgs e) { base.OnResize(e); if (_rows is not null) Fit(); }

    private static Label Note(string text) => new() { Text = text, AutoSize = false, Height = 40, Width = 340, ForeColor = Dim, RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.MiddleCenter };
    private static void Try(Action a) { try { a(); } catch (Exception e) when (e is COMException or InvalidOperationException) { } }   // a program that ended between reading and setting

    protected override bool ProcessCmdKey(ref Message msg, Keys keys) { if (keys == Keys.Escape) { Hide(); return true; } return base.ProcessCmdKey(ref msg, keys); }
    protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } base.OnFormClosing(e); }
    protected override void Dispose(bool disposing) { if (disposing) _poll.Dispose(); base.Dispose(disposing); }

    // opened from the tray menu or the app's signal, while another program is in front: joining its input for a moment brings this one forward
    private void Front()
    {
        uint front = GetWindowThreadProcessId(GetForegroundWindow(), out _), me = GetCurrentThreadId();
        bool joined = front != 0 && front != me && AttachThreadInput(me, front, true);
        try { SetForegroundWindow(Handle); Activate(); } finally { if (joined) AttachThreadInput(me, front, false); }
    }

    /// <summary>One program (or the device): its icon and name, then the slider, the percentage and the mute button.</summary>
    private sealed class Row : Panel
    {
        private readonly Label _name, _value; private readonly Slider _slider; private readonly Button _mute; private bool _muted;
        public Row(string name, Image icon, bool device, Action<float> set, Action<bool> mute)
        {
            Height = 54; Margin = new Padding(0, 0, 0, 4); BackColor = device ? Line : Back;
            var pic = new PictureBox { Image = icon, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(6, 6, 20, 20) };
            _name = new Label { Text = name, AutoEllipsis = true, Bounds = new Rectangle(32, 6, 250, 20), Font = new Font("Segoe UI", 9.5f, device ? FontStyle.Bold : FontStyle.Regular) };
            _value = new Label { Bounds = new Rectangle(0, 30, 44, 20), TextAlign = ContentAlignment.MiddleRight, ForeColor = Dim };
            _slider = new Slider { Bounds = new Rectangle(32, 30, 200, 20) };
            _mute = new Button { Bounds = new Rectangle(0, 4, 30, 24), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe MDL2 Assets", 10f), ForeColor = Text1, TabStop = false };
            _mute.FlatAppearance.BorderSize = 0;
            _slider.Changed += v => { _value.Text = $"{(int)Math.Round(v * 100)}"; set(v); };
            _mute.Click += (_, _) => { _muted = !_muted; Glyph(); mute(_muted); };
            Controls.AddRange([pic, _name, _value, _slider, _mute]);
            Resize += (_, _) => { _mute.Left = Width - 36; _value.Left = Width - 84; _slider.Width = Math.Max(40, Width - 32 - 92); _name.Width = Math.Max(40, Width - 32 - 42); };
        }

        public void Set(float volume, bool muted, bool playing = true)
        {
            if (!_slider.Dragging) { _slider.Value = volume; _value.Text = $"{(int)Math.Round(volume * 100)}"; }
            _muted = muted; Glyph(); _name.ForeColor = playing ? Text1 : Dim;
        }

        private void Glyph() { _mute.Text = _muted ? "" : ""; _mute.ForeColor = _muted ? Color.FromArgb(240, 110, 100) : Text1; _slider.Dim = _muted; }
    }

    /// <summary>A flat slider in the app's yellow: drag or click, the wheel or the arrows move it by 2, Page Up/Down by 10.</summary>
    private sealed class Slider : Control
    {
        private static readonly Color Fill = Color.FromArgb(253, 212, 0), Track = Color.FromArgb(70, 70, 78);
        private float _value; private bool _dim;
        public bool Dragging { get; private set; }
        public event Action<float>? Changed;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public float Value { get => _value; set { _value = Math.Clamp(value, 0f, 1f); Invalidate(); } }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public bool Dim { get => _dim; set { _dim = value; Invalidate(); } }

        public Slider() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true); Cursor = Cursors.Hand; TabStop = true; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? BackColor);
            int r = Height / 4, y = Height / 2, x0 = r, x1 = Width - r, at = x0 + (int)((x1 - x0) * _value);
            using (var p = new Pen(Track, 4) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round }) g.DrawLine(p, x0, y, x1, y);
            var fill = _dim ? Color.FromArgb(120, 120, 128) : Fill;
            using (var p = new Pen(fill, 4) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round }) g.DrawLine(p, x0, y, Math.Max(x0, at), y);
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, at - r, y - r, r * 2, r * 2);
            if (Focused) using (var p = new Pen(Color.FromArgb(200, 200, 210), 1)) g.DrawEllipse(p, at - r - 2, y - r - 2, r * 2 + 4, r * 2 + 4);
        }

        private void Slide(float v) { v = Math.Clamp(v, 0f, 1f); if (Math.Abs(v - _value) < 0.001f) return; Value = v; Changed?.Invoke(v); }
        private float At(int x) { int r = Height / 4; return (x - r) / (float)Math.Max(1, Width - 2 * r); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); Dragging = true; Capture = true; Slide(At(e.X)); }
        protected override void OnMouseMove(MouseEventArgs e) { if (Dragging) Slide(At(e.X)); }
        protected override void OnMouseUp(MouseEventArgs e) { Dragging = false; Capture = false; }
        protected override void OnMouseWheel(MouseEventArgs e) => Slide(_value + Math.Sign(e.Delta) * 0.02f);
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(k);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            float step = e.KeyCode switch { Keys.Right or Keys.Up => 0.02f, Keys.Left or Keys.Down => -0.02f, Keys.PageUp => 0.1f, Keys.PageDown => -0.1f, _ => 0f };
            if (step != 0) { Slide(_value + step); e.Handled = true; }
        }
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool on);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
