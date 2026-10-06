using System.Drawing; using System.IO; using System.Runtime.InteropServices; using System.Text.Json; using System.Windows.Forms;
using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization; using Mazesta.Monitoring; using Microsoft.Extensions.Logging; using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
namespace Mazesta.App;

/// <summary>
/// One sensor's chart popped out of the monitoring page into a window of its own, to keep beside a game or a render. It shares the main
/// window's browser environment and serves the same local page folder (chart.html), under the same rules: no other address, no new windows,
/// no downloads. Its bridge knows three things only, all about its own sensor: the sensor's description, its history, and its readings as they
/// arrive (in the main page's snapshot shape, so the page reuses the same store). Readings are not sent while the window is minimised.
/// </summary>
public sealed class ChartWindow : Form
{
    private static readonly Dictionary<string, ChartWindow> Open = [];
    private readonly WebView2 _view = new() { DefaultBackgroundColor = Color.FromArgb(0x0C, 0x0C, 0x0C), Dock = DockStyle.Fill };
    private readonly CoreWebView2Environment _env; private readonly PollingEngine _engine; private readonly HardwareNode _node; private readonly SensorDefinition _sensor;
    private readonly ILogger _log;
    private CoreWebView2? _core;

    /// <summary>Opens the chart of a sensor the monitor knows, or brings its window forward if it is already open. Unknown ids do nothing.</summary>
    public static void Show(CoreWebView2Environment env, PollingEngine engine, string id, ILogger log)
    {
        if (Open.TryGetValue(id, out var existing)) { if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal; existing.Activate(); return; }
        var node = engine.Hardware.FirstOrDefault(n => n.Sensors.Any(s => s.Id.Value == id));
        if (node is null) return;
        var w = new ChartWindow(env, engine, node, node.Sensors.First(s => s.Id.Value == id), log);
        Open[id] = w; w.FormClosed += (_, _) => { Open.Remove(id); w.Dispose(); };
        w.Show();
    }

    private ChartWindow(CoreWebView2Environment env, PollingEngine engine, HardwareNode node, SensorDefinition sensor, ILogger log)
    {
        _env = env; _engine = engine; _node = node; _sensor = sensor; _log = log;
        Text = $"{sensor.Name} — {node.Name}"; BackColor = Color.FromArgb(0x0C, 0x0C, 0x0C); AutoScaleMode = AutoScaleMode.None;
        float k = DeviceDpi / 96f; Size = new Size((int)(720 * k), (int)(440 * k)); MinimumSize = new Size((int)(380 * k), (int)(260 * k));
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        Controls.Add(_view);
        Load += async (_, _) => await StartAsync();
        FormClosed += (_, _) => { _engine.SnapshotPublished -= OnSnapshot; if (_core is not null) _core.WebMessageReceived -= OnMessage; };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int on = 1; _ = DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
    }

    private async Task StartAsync()
    {
        try
        {
            await _view.EnsureCoreWebView2Async(_env);
            var core = _core = _view.CoreWebView2;
            var s = core.Settings;
            s.AreDefaultContextMenusEnabled = false; s.IsStatusBarEnabled = false; s.AreBrowserAcceleratorKeysEnabled = false; s.IsPasswordAutosaveEnabled = false; s.IsGeneralAutofillEnabled = false;
#if !DEBUG
            s.AreDevToolsEnabled = false;
#endif
            core.SetVirtualHostNameToFolderMapping(MainWindow.Host, Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, a) => { if (!a.Uri.StartsWith($"https://{MainWindow.Host}/", StringComparison.OrdinalIgnoreCase)) a.Cancel = true; };
            core.NewWindowRequested += (_, a) => a.Handled = true;
            core.DownloadStarting += (_, a) => a.Cancel = true;
            core.PermissionRequested += (_, a) => a.State = CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += OnMessage;
            _engine.SnapshotPublished += OnSnapshot;
            core.Navigate($"https://{MainWindow.Host}/chart.html");
        }
        catch (Exception e) when (e is COMException or IOException or InvalidOperationException) { _log.LogWarning(e, "Chart window could not start"); Close(); }
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        long id = 0;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            id = doc.RootElement.GetProperty("id").GetInt64();
            object? result = doc.RootElement.GetProperty("m").GetString() switch
            {
                "chart.boot" => new
                {
                    language = Loc.Culture.TwoLetterISOLanguageName, rtl = Loc.IsRtl, strings = WebBridge.Strings(), units = Enum.GetValues<Unit>().ToDictionary(u => u.ToString(), Units.Symbol),
                    sensor = new { id = _sensor.Id.Value, name = _sensor.Name, kind = _sensor.Kind.ToString(), unit = _sensor.Unit.ToString(), node = _node.Name, part = _node.Kind.ToString() },
                },
                "history.get" => History(),
                _ => throw new InvalidOperationException("unknown method"),
            };
            Post(new { id, ok = true, r = result });
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        { Post(new { id, ok = false, r = (object?)null, e = ex.Message }); }
    }

    private object History()
    {
        var raw = _engine.History.GetRaw(_sensor.Id);
        return new { sec = raw.Seconds, val = raw.Values.Select(v => float.IsNaN(v) ? (float?)null : v), now = _engine.History.SecondsSinceEpoch(DateTimeOffset.UtcNow) };
    }

    private void OnSnapshot(SensorSnapshot s)
    {
        int i = -1;
        for (int k = 0; k < s.Readings.Count; k++) if (s.Readings[k].Id == _sensor.Id) { i = k; break; }
        if (i < 0) return;
        var r = s.Readings[i];
        var st = _engine.Statistics.Get(_sensor.Id);
        var message = new
        {
            ev = "snapshot",
            d = new
            {
                t = s.Timestamp.ToUnixTimeMilliseconds(),
                r = new[] { new object?[] { r.Id.Value, r.Quality == DataQuality.Ok ? r.Value : null, r.Quality.ToString() } },
                s = st.Count > 0 ? new[] { new object?[] { r.Id.Value, st.Min, st.Average, st.Max } } : [],
            },
        };
        try { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => { if (WindowState != FormWindowState.Minimized) Post(message); }); }
        catch (InvalidOperationException) { }   // the window closed between the check and the call
    }

    private void Post(object message)
    {
        try { _core?.PostWebMessageAsJson(JsonSerializer.Serialize(message, WebBridge.Json)); }
        catch (Exception ex) when (ex is InvalidOperationException or COMException) { }   // the window is closing
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
