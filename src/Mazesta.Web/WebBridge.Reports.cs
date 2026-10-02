using System.IO; using Mazesta.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    private void RegisterReports()
    {
        // The Reports view model does the opening, PDF printing (WebView2, offline), comparison, deletion and each report's summary; the page only
        // names a report by its id.
        var reports = _sp.GetRequiredService<Func<ReportsViewModel>>()();
        _cleanup.Add(reports.Dispose);
        object State() => new
        {
            status = reports.Status, making = reports.IsMakingSummary, canCompare = reports.CompareCommand.CanExecute(null), canDelete = reports.Items.Any(r => r.IsSelected),
            items = reports.Items.Select(r => new
            {
                id = r.Report.Id, title = r.Title, badge = r.Badge, verdict = r.VerdictText, summary = r.Summary, selected = r.IsSelected,
                kind = r.Report.Kind.ToString(), service = (string?)null, created = r.Report.CreatedAt.ToUnixTimeMilliseconds(),
            }),
        };
        Mirror("reports", reports, State, reports.Items);

        ReportRowViewModel Row(System.Text.Json.JsonElement p) => reports.Items.FirstOrDefault(r => r.Report.Id == Str(p, "id")) ?? throw new ArgumentException("unknown report");
        Method("reports.state", _ => State());
        Method("reports.select", p => { Row(p).IsSelected = Bool(p, "value"); return null; });
        MethodAsync("reports.exec", async p =>
        {
            switch (Str(p, "cmd"))
            {
                case "html": reports.OpenHtmlCommand.Execute(Row(p)); break;
                case "pdf": await reports.ExportPdfCommand.ExecuteAsync(Row(p)); break;
                case "text": reports.OpenTextCommand.Execute(Row(p)); break;
                case "json": reports.OpenJsonCommand.Execute(Row(p)); break;
                case "folder": reports.OpenFolderCommand.Execute(Row(p)); break;
                case "delete": reports.DeleteCommand.Execute(Row(p)); break;
                case "deleteSelected": reports.DeleteSelectedCommand.Execute(null); break;
                case "compare": if (reports.CompareCommand.CanExecute(null)) reports.CompareCommand.Execute(null); break;
                case "summary": { var row = Row(p); if (reports.SummarizeCommand.CanExecute(row)) await reports.SummarizeCommand.ExecuteAsync(row); break; }
                default: throw new ArgumentException("unknown command");
            }
            return null;
        });

        // The System page's export: HTML and JSON are written together; PDF is printed from the HTML (A4). The file opens in its own program.
        var service = _sp.GetRequiredService<Desktop.Services.ReportService>();
        MethodAsync("specs.export", async p =>
        {
            try
            {
                string html = await service.SaveSpecsAsync().ConfigureAwait(true);
                string open = Str(p, "format") switch { "json" => Path.ChangeExtension(html, ".json"), "pdf" => Path.ChangeExtension(html, ".pdf"), _ => html };
                if (open.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    await Desktop.Services.PdfExporter.ExportAsync(html, open, Desktop.Localization.Loc.Get("System_Export_Busy"), _window, service.BrowserDataDir);
                Open(open);
                return new { path = open };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                _log.LogWarning(e, "Specifications export failed");
                return new { error = Desktop.Localization.Loc.Format("System_Export_Failed", e.Message) };
            }
        });
    }
}
