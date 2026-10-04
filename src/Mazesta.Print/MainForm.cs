using System.Globalization; using System.Net.Http; using System.Runtime.InteropServices;
using Mazesta.Persistence.Updates;
using Microsoft.Web.WebView2.Core; using Microsoft.Web.WebView2.WinForms;
namespace Mazesta.Print;

/// <summary>
/// The secretary's window: on the left the reports the shop's site keeps (newest first, the service number first), on the right the chosen report's
/// summary page, and under it the buttons that print it or save it as a PDF. The list is fetched when the program starts, with the button, and
/// every few minutes while the window is open; the summary is fetched when a row is chosen. The page is shown with scripts off and every request
/// to the internet refused: it is a finished page and needs nothing from outside.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Uri Api = new("https://www.dfmrendering.com/wp-json/mazesta/v1/");
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(3);
    private readonly PrintSettings _settings = PrintSettings.Load();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SiteClient _site;
    private readonly TextBox _search = new() { PlaceholderText = PrintText.Search, Width = 280 };
    private readonly Button _refresh = new() { Text = PrintText.Refresh, AutoSize = true }, _key = new() { Text = PrintText.Settings, AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(8, 7, 0, 0) };
    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10.5f) };
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
    private readonly Button _print = new() { Text = PrintText.Print, AutoSize = true, Enabled = false }, _pdf = new() { Text = PrintText.SavePdf, AutoSize = true, Enabled = false };
    private readonly Label _hint = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 11f), ForeColor = Color.FromArgb(0x55, 0x55, 0x5F) };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = (int)Every.TotalMilliseconds };
    private readonly Font _bold = new("Segoe UI Semibold", 10.5f);
    private IReadOnlyList<SiteReportItem> _all = [];
    private string? _html, _shownId, _serviceOfShown; private int _loadingId; private bool _busy, _browserReady;

    public MainForm()
    {
        _site = new SiteClient(Api, _http);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MazestaPrint/1");
        Text = PrintText.Title; ClientSize = new Size(1180, 720); MinimumSize = new Size(900, 520); StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (!PrintText.English) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 8, 8, 4), WrapContents = false };
        top.Controls.AddRange([_search, _refresh, _key, _status]);
        _list.Columns.Add(PrintText.Service, 130); _list.Columns.Add(PrintText.Date, 130); _list.Columns.Add(PrintText.Machine, 230); _list.Columns.Add(PrintText.Result, 80);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8, 8, 8, 8), WrapContents = false };
        bottom.Controls.AddRange([_print, _pdf]);
        var preview = new Panel { Dock = DockStyle.Fill };
        preview.Controls.Add(_web); preview.Controls.Add(_hint); preview.Controls.Add(bottom);
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterDistance = 610 };
        split.Panel1.Controls.Add(_list); split.Panel2.Controls.Add(preview);
        Controls.Add(split); Controls.Add(top);
        _web.Visible = false; _hint.BringToFront();
        foreach (var b in new[] { _print, _pdf }) b.Font = new Font("Segoe UI Semibold", 10.5f);

        _search.TextChanged += (_, _) => Fill();
        _refresh.Click += async (_, _) => await RefreshAsync();
        _key.Click += (_, _) => { if (AskKey()) _ = RefreshAsync(); };
        _list.SelectedIndexChanged += async (_, _) => { if (_list.SelectedItems.Count > 0) await ShowAsync((SiteReportItem)_list.SelectedItems[0].Tag!); };
        _print.Click += (_, _) => PrintShown();
        _pdf.Click += async (_, _) => await SavePdfAsync();
        _timer.Tick += async (_, _) => { if (WindowState != FormWindowState.Minimized && Visible) await RefreshAsync(); };
        Shown += async (_, _) => { _timer.Start(); await RefreshAsync(); };
        FormClosed += (_, _) => { _timer.Dispose(); _http.Dispose(); };
        _hint.Text = PrintText.PickOne;
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        if (_settings.Key.Length == 0) { _status.Text = ""; _hint.Text = PrintText.NeedKey; _hint.Visible = true; if (!AskKey()) return; }
        _busy = true; _refresh.Enabled = false; _status.Text = PrintText.Loading;
        try
        {
            _all = await _site.ReportsAsync(_settings.Key, CancellationToken.None);
            _status.Text = PrintText.Updated(DateTime.Now, _all.Count);
            Fill();
        }
        catch (SiteException e) when (e.Status == System.Net.HttpStatusCode.Unauthorized) { _status.Text = PrintText.WrongKey; _hint.Text = PrintText.NeedKey; _hint.Visible = true; _web.Visible = false; }
        catch (Exception e) when (e is SiteException or HttpRequestException or TaskCanceledException) { _status.Text = PrintText.Failed(e.Message); }
        finally { _busy = false; _refresh.Enabled = true; }
    }

    /// <summary>The list as the search filters it, the chosen report kept chosen.</summary>
    private void Fill()
    {
        string q = _search.Text.Trim(); string? keep = _shownId;
        var rows = _all.Where(r => q.Length == 0 || (r.Service ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || r.Machine.Contains(q, StringComparison.OrdinalIgnoreCase)
            || r.Summary.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var r in rows)
        {
            var item = new ListViewItem([string.IsNullOrWhiteSpace(r.Service) ? PrintText.NoService : r.Service, r.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), r.Machine,
                PrintText.Verdict(r.Verdict, r.Kind)]) { Tag = r };
            if (string.IsNullOrWhiteSpace(r.Service)) item.ForeColor = Color.FromArgb(0x88, 0x88, 0x88);
            if (!string.IsNullOrWhiteSpace(r.Service)) item.Font = _bold;
            if (r.Id == keep) item.Selected = true;
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        if (_list.Items.Count == 0 && _shownId is null) { _hint.Text = _all.Count == 0 ? PrintText.Empty : PrintText.NoMatch; _hint.Visible = true; _web.Visible = false; }
    }

    private async Task ShowAsync(SiteReportItem report)
    {
        if (report.Id == _shownId && _html is not null) return;
        int mine = ++_loadingId; _print.Enabled = _pdf.Enabled = false; _status.Text = PrintText.Loading;
        try
        {
            string html = await _site.ReportHtmlAsync(_settings.Key, report.Id, CancellationToken.None);
            if (mine != _loadingId) return;   // another row was chosen meanwhile
            await EnsureBrowserAsync();
            _html = html; _shownId = report.Id; _serviceOfShown = report.Service;
            _web.CoreWebView2.NavigateToString(html);
            _web.Visible = true; _hint.Visible = false; _print.Enabled = _pdf.Enabled = true;
            _status.Text = PrintText.Updated(DateTime.Now, _all.Count);
        }
        catch (Exception e) when (e is SiteException or HttpRequestException or TaskCanceledException or WebView2RuntimeNotFoundException)
        {
            _status.Text = PrintText.Failed(e is WebView2RuntimeNotFoundException ? PrintText.NoBrowser : e.Message);
        }
    }

    private async Task EnsureBrowserAsync()
    {
        if (_browserReady) return;
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(PrintSettings.DataDir, "browser"));
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2; core.Settings.IsScriptEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false; core.Settings.AreDevToolsEnabled = false;
        core.PermissionRequested += (_, a) => a.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, a) => a.Cancel = true; core.NewWindowRequested += (_, a) => a.Handled = true;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, a) => { if (a.Request.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)) a.Response = env.CreateWebResourceResponse(null, 403, "Offline report", ""); };
        _browserReady = true;
    }

    private void PrintShown() { if (_browserReady && _html is not null) _web.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.System); }

    private async Task SavePdfAsync()
    {
        if (!_browserReady || _html is null) return;
        string name = "mazesta-" + (string.IsNullOrWhiteSpace(_serviceOfShown) ? "report" : string.Concat(_serviceOfShown.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c))) + ".pdf";
        using var dialog = new SaveFileDialog { Title = PrintText.SaveDialog, Filter = "PDF|*.pdf", FileName = name };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var settings = _web.CoreWebView2.Environment.CreatePrintSettings(); settings.PageWidth = 5.8268; settings.PageHeight = 8.2677; settings.ShouldPrintBackgrounds = true; settings.ShouldPrintHeaderAndFooter = false;
            if (!await _web.CoreWebView2.PrintToPdfAsync(dialog.FileName, settings)) throw new IOException("Printing to PDF failed.");
            _status.Text = PrintText.Saved(dialog.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException) { _status.Text = PrintText.Failed(e.Message); }
    }

    /// <summary>Asks for the reading key (pasted from the site's plugin page); true when a key was saved.</summary>
    private bool AskKey()
    {
        using var dialog = new Form { Text = PrintText.KeyTitle, ClientSize = new Size(520, 190), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, RightToLeft = RightToLeft, RightToLeftLayout = RightToLeftLayout };
        var help = new Label { Text = PrintText.KeyHelp, Left = 14, Top = 12, Width = 490, Height = 52 };
        var box = new TextBox { Left = 14, Top = 70, Width = 490, UseSystemPasswordChar = false, Text = _settings.Key, RightToLeft = RightToLeft.No };
        var bad = new Label { Left = 14, Top = 98, Width = 490, ForeColor = Color.FromArgb(0xB0, 0x20, 0x10) };
        var ok = new Button { Text = PrintText.Save, Left = 14, Top = 140, Width = 110 }; var cancel = new Button { Text = PrintText.Cancel, Left = 134, Top = 140, Width = 110, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) =>
        {
            if (SiteClient.FindKey(box.Text) is not { } key) { bad.Text = PrintText.KeyBad; return; }
            _settings.Key = key; _settings.Save(); dialog.DialogResult = DialogResult.OK;
        };
        dialog.AcceptButton = ok; dialog.CancelButton = cancel;
        dialog.Controls.AddRange([help, box, bad, ok, cancel]);
        return dialog.ShowDialog(this) == DialogResult.OK;
    }
}
