using System.IO; using System.Runtime.InteropServices; using System.Windows; using System.Windows.Interop; using System.Windows.Media;
using Mazesta.Persistence; using Microsoft.Extensions.Logging; using Microsoft.Web.WebView2.Core; using Microsoft.Web.WebView2.Wpf;
namespace Mazesta.Web;

/// <summary>
/// The one window of the web edition: a dark-titled frame around a WebView2. The page comes only from the local wwwroot folder, mapped to a
/// private host name; every other navigation, new window, download and permission is refused, so the page can do nothing the bridge does not
/// offer. The browser profile lives in the portable Data folder. With the "software" render mode the browser draws without the GPU as well.
/// </summary>
public sealed class MainWindow : Window
{
    public const string Host = "mazesta.app";
    private readonly WebView2 _view = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x0C, 0x0C, 0x0C) };
    private readonly IServiceProvider _services; private readonly AppPaths _paths; private readonly AppConfig _config; private readonly JsonStore<AppConfig> _store;
    private readonly bool _configCorrupt; private readonly ILogger _log;
    private WebBridge? _bridge;

    public MainWindow(IServiceProvider services, AppPaths paths, AppConfig config, JsonStore<AppConfig> store, bool configCorrupt, ILogger log)
    {
        _services = services; _paths = paths; _config = config; _store = store; _configCorrupt = configCorrupt; _log = log;
        Title = "Mazesta"; Width = 1360; Height = 860; MinWidth = 1024; MinHeight = 640;
        Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0C));
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/MazestaWeb;component/Assets/mazesta.ico"));
        Content = _view;
        SourceInitialized += (_, _) => DarkTitleBar();
        Loaded += async (_, _) => await StartAsync();
        StateChanged += (_, _) => _bridge?.SetVisible(WindowState != WindowState.Minimized);
        Closing += (_, _) => OnClosing();
    }

    private async Task StartAsync()
    {
        try
        {
            var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = _config.RenderMode == "software" ? "--disable-gpu --disable-gpu-compositing" : "" };
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(_paths.CacheDir, "web-browser"), options: options);
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
            core.PermissionRequested += (_, a) => a.State = CoreWebView2PermissionState.Deny;
            _bridge = new WebBridge(core, _services, _paths, _config, _store, _configCorrupt, this, _log);
            core.Navigate($"https://{Host}/index.html");
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or COMException or IOException)
        {
            _log.LogError(e, "WebView2 could not start");
            MessageBox.Show(Desktop.Localization.Loc.Get("Web_NoRuntime") + "\n\n" + e.Message, "Mazesta", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    /// <summary>A second start or the tray asked for the window: restore it if minimised and put it in front.</summary>
    public void BringForward()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate(); Topmost = true; Topmost = false; Focus();
    }

    private void OnClosing()
    {
        bool maximized = WindowState == WindowState.Maximized;
        var b = maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        _config.MainWindow = new WindowPlacement(b.Left, b.Top, b.Width, b.Height, maximized);
        _store.Save(_config);
        _bridge?.Dispose();
    }

    private void DarkTitleBar()
    {
        int on = 1; var hwnd = new WindowInteropHelper(this).Handle;
        _ = DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE: the frame matches the ink page
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
