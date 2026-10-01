using System.Drawing; using System.IO; using System.Runtime.InteropServices; using System.Windows.Forms;
using Mazesta.Desktop.Composition; using Mazesta.Persistence; using Microsoft.Extensions.Logging; using Microsoft.Web.WebView2.Core; using Microsoft.Web.WebView2.WinForms;
namespace Mazesta.Web;

/// <summary>
/// The one window of the web edition: a dark-titled frame around a WebView2. The page comes only from the local wwwroot folder, mapped to a
/// private host name; every other navigation, new window, download and permission is refused, so the page can do nothing the bridge does not
/// offer. The browser profile lives in the portable Data folder. With the "software" render mode the browser draws without the GPU.
/// </summary>
public sealed class MainWindow : Form
{
    public const string Host = "mazesta.app";
    private static readonly Color InkColor = Color.FromArgb(0x0C, 0x0C, 0x0C);
    private readonly WebView2 _view = new() { DefaultBackgroundColor = InkColor, Dock = DockStyle.Fill, Visible = false };
    private readonly IServiceProvider _services; private readonly AppPaths _paths; private readonly AppConfig _config; private readonly JsonStore<AppConfig> _store;
    private readonly bool _configCorrupt; private readonly ILogger _log;
    private WebBridge? _bridge; private readonly LoadingPanel _loading = new() { Dock = DockStyle.Fill };
    private FormWindowState _lastState = FormWindowState.Normal;

    /// <summary>The UI thread, for the bridge and the engines' events.</summary>
    public UiDispatcher Dispatcher { get; }
    /// <summary>The browser environment, shared with the chart windows (one browser process for all of them).</summary>
    public CoreWebView2Environment? WebEnvironment { get; private set; }

    public MainWindow(IServiceProvider services, AppPaths paths, AppConfig config, JsonStore<AppConfig> store, bool configCorrupt, ILogger log, UiDispatcher dispatcher)
    {
        _services = services; _paths = paths; _config = config; _store = store; _configCorrupt = configCorrupt; _log = log; Dispatcher = dispatcher;
        Text = "Mazesta"; BackColor = InkColor; StartPosition = FormStartPosition.CenterScreen;
        // Sized here in device-independent pixels at the screen's scale (no automatic scaling later, so a restored placement is not scaled twice).
        AutoScaleMode = AutoScaleMode.None; float k = DeviceDpi / 96f;
        Size = new Size((int)(1360 * k), (int)(860 * k)); MinimumSize = new Size((int)(1024 * k), (int)(640 * k));
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        // Until the page has drawn, a native loading panel: starting the browser on an older machine takes seconds, and a blank window looks hung.
        Controls.Add(_view); Controls.Add(_loading);
        Load += async (_, _) => await StartAsync();
        Resize += (_, _) => { if (WindowState != _lastState) { _lastState = WindowState; _bridge?.SetVisible(WindowState != FormWindowState.Minimized); } };
        FormClosing += (_, _) => OnClosing();
    }

    /// <summary>Whether the window is the one in front and not minimised (a notification is then shown on the page instead).</summary>
    public bool InFront => ActiveForm == this && WindowState != FormWindowState.Minimized;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int on = 1; _ = DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE: the frame matches the ink page
    }

    private async Task StartAsync()
    {
        try
        {
            string args = _config.RenderMode == "software" ? "--disable-gpu --disable-gpu-compositing" : "";
            // For checking the pages in the real app (elevated, real sensors): DevTools on a local port, only when the variable is set.
            if (int.TryParse(Environment.GetEnvironmentVariable("MAZESTA_DEVTOOLS_PORT"), out int port) && port is > 1024 and < 65536)
                args = $"{args} --remote-debugging-port={port} --remote-allow-origins=http://127.0.0.1:{port}".Trim();
            var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = args };
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(_paths.CacheDir, "web-browser"), options: options);
            WebEnvironment = env;
            await _view.EnsureCoreWebView2Async(env);
            var core = _view.CoreWebView2;
            var s = core.Settings;
            s.AreDefaultContextMenusEnabled = false; s.IsStatusBarEnabled = false; s.AreBrowserAcceleratorKeysEnabled = false; s.IsPasswordAutosaveEnabled = false; s.IsGeneralAutofillEnabled = false;
#if !DEBUG
            s.AreDevToolsEnabled = false;
#endif
            string root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            core.SetVirtualHostNameToFolderMapping(Host, root, CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, a) => { if (!a.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)) a.Cancel = true; };
            core.NewWindowRequested += (_, a) => a.Handled = true;
            core.DownloadStarting += (_, a) => a.Cancel = true;
            // Only the hands-on checks page asks for anything, and only for the microphone (its level meter); every other request is refused.
            core.PermissionRequested += (_, a) => a.State = a.PermissionKind == CoreWebView2PermissionKind.Microphone && a.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)
                ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            // The dead-pixel check puts one element in full screen: the window follows, so the colour covers the whole display.
            FormWindowState? before = null; FormBorderStyle style = FormBorderStyle;
            core.ContainsFullScreenElementChanged += (_, _) =>
            {
                if (core.ContainsFullScreenElement) { before = WindowState; style = FormBorderStyle; FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Normal; WindowState = FormWindowState.Maximized; }
                else { FormBorderStyle = style; WindowState = before ?? FormWindowState.Normal; }
            };
            // The page shows its own loading card from its first paint, so the native one goes as soon as the page has drawn.
            core.DOMContentLoaded += (_, _) => { _view.Visible = true; _loading.Stop(); _loading.Visible = false; };
            _bridge = new WebBridge(core, _services, _paths, _config, _store, _configCorrupt, this, _log);
            core.Navigate($"https://{Host}/index.html");
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or COMException or IOException)
        {
            _log.LogError(e, "WebView2 could not start");
            MessageBox.Show(this, Desktop.Localization.Loc.Get("Web_NoRuntime") + "\n\n" + e.Message, "Mazesta", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    /// <summary>A second start or the tray asked for the window: restore it if minimised and put it in front.</summary>
    public void BringForward()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show(); Activate(); TopMost = true; TopMost = false; Focus();
    }

    private void OnClosing()
    {
        _config.MainWindow = WindowPlacementRestore.Capture(this);
        _store.Save(_config);
        _bridge?.Dispose();
    }

    /// <summary>The window's native loading panel: the mark, a line of text, and a thin bar running across while the browser starts.</summary>
    private sealed class LoadingPanel : Control
    {
        private readonly System.Windows.Forms.Timer _tick = new() { Interval = 30 };
        private int _phase;
        public LoadingPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = InkColor;
            _tick.Tick += (_, _) => { _phase = (_phase + 4) % 440; Invalidate(); }; _tick.Start();
        }
        public void Stop() => _tick.Stop();
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float k = DeviceDpi / 96f, cx = Width / 2f, cy = Height / 2f;
            using var title = new Font("Segoe UI", 28 * k, FontStyle.Bold, GraphicsUnit.Pixel); using var body = new Font("Segoe UI", 13 * k, GraphicsUnit.Pixel);
            using var yellow = new SolidBrush(Color.FromArgb(0xFD, 0xD4, 0x00)); using var muted = new SolidBrush(Color.FromArgb(0x8F, 0x8C, 0x82));
            using var center = new StringFormat { Alignment = StringAlignment.Center };
            if (Desktop.Localization.Loc.IsRtl) center.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            g.DrawString("MAZESTA", title, yellow, cx, cy - 40 * k, center);
            g.DrawString(Desktop.Localization.Loc.Get("Web_Boot_Window"), body, muted, cx, cy + 4 * k, center);
            float w = 220 * k, x = cx - w / 2, y = cy + 34 * k;
            using (var track = new SolidBrush(Color.FromArgb(0x1C, 0x1C, 0x19))) g.FillRectangle(track, x, y, w, 3 * k);
            float seg = w * 0.3f, pos = x - seg + (_phase / 440f) * (w + seg);
            g.SetClip(new RectangleF(x, y, w, 3 * k)); g.FillRectangle(yellow, pos, y, seg, 3 * k); g.ResetClip();
        }
        protected override void Dispose(bool disposing) { if (disposing) _tick.Dispose(); base.Dispose(disposing); }
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
