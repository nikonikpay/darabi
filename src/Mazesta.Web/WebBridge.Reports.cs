using Mazesta.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    private void RegisterReports()
    {
        // The Reports view model does the opening, PDF printing (WebView2, offline), comparison, deletion and the customer summary; the page only
        // names a report by its id.
        var reports = _sp.GetRequiredService<Func<ReportsViewModel>>()();
        _cleanup.Add(reports.Dispose);
        object State() => new
        {
            status = reports.Status, making = reports.IsMakingSummary, canCompare = reports.CompareCommand.CanExecute(null),
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
                case "compare": if (reports.CompareCommand.CanExecute(null)) reports.CompareCommand.Execute(null); break;
                case "summary": if (reports.CreateSummaryCommand.CanExecute(null)) await reports.CreateSummaryCommand.ExecuteAsync(null); break;
                default: throw new ArgumentException("unknown command");
            }
            return null;
        });
    }
}
