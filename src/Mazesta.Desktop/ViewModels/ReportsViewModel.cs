using System.Collections.ObjectModel; using System.Globalization; using System.IO;
using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input; using Mazesta.Desktop.Localization; using Mazesta.Desktop.Services; using Mazesta.Reporting;
namespace Mazesta.Desktop.ViewModels;

public sealed partial class ReportRowViewModel(StoredReport report) : ObservableObject
{
    /// <summary>Ticked for a before/after comparison.</summary>
    [ObservableProperty] private bool _isSelected;
    public StoredReport Report { get; } = report;
    /// <summary>The verdict, or Benchmark for a benchmark report (which has none) - what the badge shows and is coloured by.</summary>
    public string Badge => Report.Verdict?.ToString() ?? nameof(ReportKind.Benchmark);
    public string VerdictText => Loc.Get("Reports_Verdict_" + Badge);
    public string Title => Stamp(Report.CreatedAt);
    public string Summary { get; } = report.Kind == ReportKind.Benchmark ? string.Join(" · ", report.Benchmarks)
        : Loc.Format("Reports_RowCounts", report.Counts.Total, report.Counts.Passed, report.Counts.Failed, report.Counts.Cancelled + report.Counts.Unsupported + report.Counts.NotRun);

    /// <summary>Solar Hijri date with the local time when the app is Persian, ISO otherwise.</summary>
    private static string Stamp(DateTimeOffset t)
    {
        var local = t.ToLocalTime().DateTime;
        if (!Loc.IsRtl) return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var pc = new PersianCalendar();
        return string.Create(CultureInfo.InvariantCulture, $"{pc.GetYear(local)}/{pc.GetMonth(local):00}/{pc.GetDayOfMonth(local):00}  {local:HH:mm}");
    }
}

/// <summary>The saved reports, newest first, with every format one click away, and a before/after comparison of two ticked reports.</summary>
public sealed partial class ReportsViewModel : ObservableObject, IDisposable
{
    private readonly ReportService _service; private readonly Func<Action, object> _dispatch; private readonly Action<string> _open; private readonly Func<string, bool> _confirm;

    public ObservableCollection<ReportRowViewModel> Items { get; } = [];
    public bool IsEmpty => Items.Count == 0;
    [ObservableProperty] private string _status = "";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SummarizeCommand))] private bool _isMakingSummary;

    public ReportsViewModel(ReportService service, Func<Action, object> dispatch, Action<string> open, Func<string, bool> confirm)
    {
        _service = service; _dispatch = dispatch; _open = open; _confirm = confirm;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        Refresh(); service.ReportCreated += OnCreated;
    }

    private void OnCreated(StoredReport report) => _dispatch(() => { Items.Insert(0, Row(report)); Status = Loc.Get("Reports_Created"); });   // newest first, without re-reading every report
    private void Refresh() { Items.Clear(); foreach (var r in _service.Store.List()) Items.Add(Row(r)); }
    private ReportRowViewModel Row(StoredReport report)
    {
        var row = new ReportRowViewModel(report);
        row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ReportRowViewModel.IsSelected)) CompareCommand.NotifyCanExecuteChanged(); };
        return row;
    }

    private bool CanCompare() => Items.Count(r => r.IsSelected) == 2;
    /// <summary>The older of the two ticked reports is "before". Reports of two different machines are refused with the reason, never compared.</summary>
    [RelayCommand(CanExecute = nameof(CanCompare))]
    private void Compare()
    {
        var pair = Items.Where(r => r.IsSelected).Select(r => r.Report).OrderBy(r => r.CreatedAt).ToList();
        try
        {
            var (path, refused) = _service.Compare(pair[0], pair[1]);
            if (path is not null) { Status = ""; _open(path); } else Status = Loc.Format("Reports_NotComparable", Loc.Get("Reports_Mismatch_" + refused));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { Status = Loc.Format("Reports_CompareFailed", e.Message); }
    }

    [RelayCommand] private void OpenHtml(ReportRowViewModel row) => _open(row.Report.HtmlPath);
    [RelayCommand] private void OpenJson(ReportRowViewModel row) => _open(row.Report.JsonPath);
    [RelayCommand] private void OpenText(ReportRowViewModel row) => _open(_service.Store.EnsureText(row.Report, ReportText.For(Loc.IsRtl ? "fa" : "en")));
    [RelayCommand] private void OpenFolder(ReportRowViewModel row) => _open(row.Report.Folder);

    [RelayCommand]
    private async Task ExportPdf(ReportRowViewModel row)
    {
        try
        {
            if (!File.Exists(row.Report.PdfPath)) { Status = Loc.Get("Reports_PdfBusy"); await PdfExporter.ExportAsync(row.Report.HtmlPath, row.Report.PdfPath, Loc.Get("Reports_PdfBusy"), Composition.UiDispatcher.Owner, _service.BrowserDataDir); }
            Status = ""; _open(row.Report.PdfPath);
        }
        catch (Exception e) { Status = Loc.Format("Reports_PdfFailed", e.Message); }
    }

    private bool CanSummarize(ReportRowViewModel? row) => !IsMakingSummary;

    /// <summary>The one-page summary of this report (its verdict, each test's result and the highest temperatures while it ran), printed to an
    /// A5 PDF next to the report and opened.</summary>
    [RelayCommand(CanExecute = nameof(CanSummarize))]
    private async Task Summarize(ReportRowViewModel row)
    {
        IsMakingSummary = true; Status = Loc.Get("Reports_SummaryBusy");
        try
        {
            string html = _service.CreateSummary(row.Report), pdf = Path.ChangeExtension(html, ".pdf");
            await PdfExporter.ExportAsync(html, pdf, Loc.Get("Reports_SummaryBusy"), Composition.UiDispatcher.Owner, _service.BrowserDataDir, a5: true);
            Status = Loc.Get("Reports_SummaryDone"); _open(pdf);
        }
        catch (Exception e) { Status = Loc.Format("Reports_SummaryFailed", e.Message); }
        finally { IsMakingSummary = false; }
    }

    [RelayCommand]
    private void Delete(ReportRowViewModel row)
    {
        if (!_confirm(Loc.Get("Reports_DeleteConfirm"))) return;
        _service.Store.Delete(row.Report); Items.Remove(row); CompareCommand.NotifyCanExecuteChanged();
    }

    public void Dispose() => _service.ReportCreated -= OnCreated;
}
