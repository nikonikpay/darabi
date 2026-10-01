using System.IO; using System.Windows.Forms;
using Microsoft.Web.WebView2.Core; using Microsoft.Web.WebView2.WinForms;
namespace Mazesta.Desktop.Services;

/// <summary>
/// Prints a report's HTML to an A4 PDF with the system's WebView2 (Edge) runtime, which shapes Persian text correctly.
/// The browser is offline for this: scripts are off and every network request is refused. It exists only for the few
/// seconds of an export, so it costs nothing while the app is idle.
/// </summary>
public static class PdfExporter
{
    /// <summary>A4 unless <paramref name="a5"/> (the customer summary, a half sheet).</summary>
    public static async Task ExportAsync(string htmlPath, string pdfPath, string busyText, IWin32Window? owner, string browserDataDir, bool a5 = false)
    {
        string html = await File.ReadAllTextAsync(htmlPath);
        using var browser = new WebView2 { Dock = DockStyle.Fill };
        // A small window that says what is happening (the browser needs a window to print from); it closes when the file is written.
        using var window = new Form { Text = busyText, Width = 360, Height = 120, StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.FixedToolWindow, RightToLeft = Localization.Loc.IsRtl ? RightToLeft.Yes : RightToLeft.No };
        window.Controls.Add(browser);
        if (owner is not null) window.Show(owner); else window.Show();
        string temp = pdfPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: browserDataDir);   // inside the app's Data folder: the app is portable
            await browser.EnsureCoreWebView2Async(env).WaitAsync(TimeSpan.FromSeconds(30));
            var core = browser.CoreWebView2; core.Settings.IsScriptEnabled = false;
            core.PermissionRequested += (_, a) => a.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, a) => a.Cancel = true; core.NewWindowRequested += (_, a) => a.Handled = true;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, a) => { if (a.Request.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)) a.Response = env.CreateWebResourceResponse(null, 403, "Offline report", ""); };
            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationCompleted += (_, a) => loaded.TrySetResult(a.IsSuccess);
            core.NavigateToString(html);
            if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30))) throw new IOException("The report page did not load.");
            var settings = env.CreatePrintSettings(); settings.PageWidth = a5 ? 5.8268 : 8.2677; settings.PageHeight = a5 ? 8.2677 : 11.6929; settings.ShouldPrintBackgrounds = true; settings.ShouldPrintHeaderAndFooter = false;
            if (!await core.PrintToPdfAsync(temp, settings).WaitAsync(TimeSpan.FromSeconds(60))) throw new IOException("Printing to PDF failed.");
            File.Move(temp, pdfPath, overwrite: true);
        }
        finally { window.Close(); if (File.Exists(temp)) File.Delete(temp); }
    }
}
