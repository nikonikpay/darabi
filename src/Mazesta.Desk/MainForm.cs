using System.Text.Json;
using Microsoft.Web.WebView2.Core; using Microsoft.Web.WebView2.WinForms;
namespace Mazesta.Desk;

/// <summary>
/// The window: one WebView2 showing the pages in wwwroot (a virtual host, nothing from the internet), and the bridge those pages talk through. The page sends
/// {id, op, ...}; this side answers {id, ...}. Only the ops below exist, every call to the site goes through <see cref="SiteSession"/> (path-checked by
/// <see cref="DeskRules"/>), and messages from any page other than ours are ignored.
/// </summary>
internal sealed class MainForm : Form
{
    private const string Host = "mazesta-desk.local";
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(0xF4, 0xF5, 0xF7) };
    private readonly SiteSession _site;
    private readonly string _data = Path.Combine(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "Data");
    private readonly string? _selftest;
    private readonly CancellationTokenSource _closing = new();
    private readonly TaskCompletionSource<string> _selftestDone = new();

    public MainForm(string? selftest)
    {
        _selftest = selftest; _site = new SiteSession(_data);
        Text = "مازستا — میز کار"; ClientSize = new Size(1280, 800); MinimumSize = new Size(980, 600); StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Controls.Add(_web);
        Shown += async (_, _) => await StartAsync();
        FormClosing += (_, _) => _closing.Cancel();
        FormClosed += (_, _) => { _closing.Dispose(); _site.Dispose(); };
    }

    private async Task StartAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(_data, "browser"));
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(this, "مرورگر داخلی (WebView2 Runtime) روی این سیستم نیست. آن را از سایت مایکروسافت نصب کنید.\n" + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            Close(); return;
        }
        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = _selftest is not null; core.Settings.AreDefaultContextMenusEnabled = false; core.Settings.IsStatusBarEnabled = false; core.Settings.IsZoomControlEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;   // F1–F12 are the accounting's keys: no reload on F5, no caret browsing on F7 (copy and paste still work)
        core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Allow);
        core.PermissionRequested += (_, a) => a.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, a) => a.Cancel = true;
        core.NewWindowRequested += (_, a) => a.Handled = true;
        core.NavigationStarting += (_, a) => { if (!a.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase) && !a.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) a.Cancel = true; };
        core.WebMessageReceived += async (_, a) => await OnMessageAsync(a);
        if (_selftest is not null) core.NavigationCompleted += async (_, _) => await RunSelfTestAsync();
        core.Navigate($"https://{Host}/index.html");
    }

    private async Task OnMessageAsync(CoreWebView2WebMessageReceivedEventArgs a)
    {
        if (!a.Source.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)) return;
        JsonElement msg; try { msg = JsonDocument.Parse(a.TryGetWebMessageAsString()).RootElement.Clone(); } catch (Exception e) when (e is JsonException or ArgumentException) { return; }
        int id = msg.TryGetProperty("id", out var i) && i.TryGetInt32(out int n) ? n : 0;
        string Str(string k) => msg.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        object reply;
        try
        {
            switch (Str("op"))
            {
                case "api": reply = Pack(await _site.CallAsync(Str("method"), Str("path"), Str("body"), _closing.Token)); break;
                case "login": reply = Pack(await _site.LoginAsync(Str("username"), Str("password"), _closing.Token)); break;
                case "logout": await _site.LogoutAsync(_closing.Token); reply = new { status = 200 }; break;
                case "session": reply = new { status = 200, data = new { server = _site.Server.GetLeftPart(UriPartial.Authority), signedIn = _site.SignedIn, machine = Environment.MachineName } }; break;
                case "server": reply = _site.SetServer(Str("server")) ? new { status = 200, data = new { server = _site.Server.GetLeftPart(UriPartial.Authority) } } : new { status = 400, error = "نشانی سایت درست نیست (باید https باشد)." }; break;
                case "selftest-done" when _selftest is not null: _selftestDone.TrySetResult(Str("result")); reply = new { status = 200 }; break;
                case "print": _web.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.System); reply = new { status = 200 }; break;
                default: reply = new { status = 400, error = "درخواست ناشناخته." }; break;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException) { reply = new { status = 500, error = e.Message }; }
        if (_web.CoreWebView2 is { } core) core.PostWebMessageAsString(JsonSerializer.Serialize(reply, Opts).Insert(1, $"\"id\":{id},"));
    }

    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static object Pack((int Status, JsonElement? Data, string? Error) r) => new { status = r.Status, data = r.Data, error = r.Error };

    /// <summary>Unattended run (<c>--selftest</c>): the page drives its own screens against the site named in MAZESTA_DESK_SELFTEST ("server|user|password") and the result is written to Data\logs\selftest-desk.txt.</summary>
    private async Task RunSelfTestAsync()
    {
        string log = Path.Combine(_data, "logs", "selftest-desk.txt"); Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        string result;
        try
        {
            string[] cfg = (Environment.GetEnvironmentVariable("MAZESTA_DESK_SELFTEST") ?? "").Split('|');
            if (cfg.Length != 3) throw new InvalidOperationException("MAZESTA_DESK_SELFTEST must be server|user|password");
            await _web.CoreWebView2.ExecuteScriptAsync($"window.deskSelfTestRun({JsonSerializer.Serialize(cfg[0])},{JsonSerializer.Serialize(cfg[1])},{JsonSerializer.Serialize(cfg[2])})");
            result = await _selftestDone.Task.WaitAsync(TimeSpan.FromSeconds(180));
        }
        catch (Exception e) { result = "ERROR " + e.Message; }
        File.WriteAllText(log, result);
        Close();
    }
}
