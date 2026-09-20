using System.Collections.ObjectModel; using System.Globalization; using System.IO; using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input; using Mazesta.Desktop.Localization; using Mazesta.Desktop.Services; using Mazesta.Reporting;
namespace Mazesta.Desktop.ViewModels;

public sealed class ReportRowViewModel(StoredReport report)
{
    public StoredReport Report { get; } = report;
    public ReportVerdict Verdict => Report.Verdict;
    public string VerdictText => Loc.Get("Reports_Verdict_" + Report.Verdict);
    public string Title => Stamp(Report.CreatedAt);
    public string Summary { get; } = Loc.Format("Reports_Summary", report.Counts.Total, report.Counts.Passed, report.Counts.Failed, report.Counts.Cancelled + report.Counts.Unsupported + report.Counts.NotRun);

    /// <summary>Solar Hijri date with the local time when the app is Persian, ISO otherwise.</summary>
    private static string Stamp(DateTimeOffset t)
    {
        var local = t.ToLocalTime().DateTime;
        if (!Loc.IsRtl) return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var pc = new PersianCalendar();
        return string.Create(CultureInfo.InvariantCulture, $"{pc.GetYear(local)}/{pc.GetMonth(local):00}/{pc.GetDayOfMonth(local):00}  {local:HH:mm}");
    }
}

/// <summary>The saved reports of finished test runs, newest first, with the three formats one click away.</summary>
public sealed partial class ReportsViewModel : ObservableObject, IDisposable
{
    private readonly ReportService _service; private readonly Func<Action, object> _dispatch; private readonly Action<string> _open; private readonly Func<string, bool> _confirm;

    public ObservableCollection<ReportRowViewModel> Items { get; } = [];
    public bool IsEmpty => Items.Count == 0;
    [ObservableProperty] private string _status = "";

    public ReportsViewModel(ReportService service, Func<Action, object> dispatch, Action<string> open, Func<string, bool> confirm)
    {
        _service = service; _dispatch = dispatch; _open = open; _confirm = confirm;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        Refresh(); service.ReportCreated += OnCreated;
    }

    private void OnCreated(StoredReport _) => _dispatch(() => { Refresh(); Status = Loc.Get("Reports_Created"); });
    private void Refresh() { Items.Clear(); foreach (var r in _service.Store.List()) Items.Add(new(r)); }

    [RelayCommand] private void OpenHtml(ReportRowViewModel row) => _open(row.Report.HtmlPath);
    [RelayCommand] private void OpenJson(ReportRowViewModel row) => _open(row.Report.JsonPath);
    [RelayCommand] private void OpenFolder(ReportRowViewModel row) => _open(row.Report.Folder);

    [RelayCommand]
    private async Task ExportPdf(ReportRowViewModel row)
    {
        try
        {
            if (!File.Exists(row.Report.PdfPath)) { Status = Loc.Get("Reports_PdfBusy"); await PdfExporter.ExportAsync(row.Report.HtmlPath, row.Report.PdfPath, Loc.Get("Reports_PdfBusy"), Application.Current.MainWindow); }
            Status = ""; _open(row.Report.PdfPath);
        }
        catch (Exception e) { Status = Loc.Format("Reports_PdfFailed", e.Message); }
    }

    [RelayCommand]
    private void Delete(ReportRowViewModel row)
    {
        if (!_confirm(Loc.Get("Reports_DeleteConfirm"))) return;
        _service.Store.Delete(row.Report); Items.Remove(row);
    }

    public void Dispose() => _service.ReportCreated -= OnCreated;
}
