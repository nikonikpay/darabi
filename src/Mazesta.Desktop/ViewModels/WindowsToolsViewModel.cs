using System.Collections.ObjectModel; using System.IO; using System.Globalization; using System.Text; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input;
using Mazesta.Desktop.Localization; using Mazesta.Diagnostics.Windows; using Mazesta.Hardware.Wmi;
namespace Mazesta.Desktop.ViewModels;

/// <summary>
/// Windows Tools (spec 11.2-11.4): Windows' own repair tools, run only when the technician presses a button, with their live output; the page
/// file shown as Windows has it; Disk Cleanup and Windows Update opened in Windows' own windows rather than changed from here. One tool runs at a
/// time. A single instance for the session (like the engines, and not IDisposable): a ten-minute sfc run and its output survive leaving the page.
/// </summary>
public sealed partial class WindowsToolsViewModel : ObservableObject
{
    private const int MaxLines = 400;
    private readonly ICommandRunner _runner; private readonly Action<string> _open; private readonly Func<Action, object> _dispatch;
    private CancellationTokenSource? _cts;

    public ObservableCollection<string> Output { get; } = [];
    public IReadOnlyList<InfoRow> PageFile { get; }
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SfcCommand), nameof(DismScanCommand), nameof(DismRestoreCommand), nameof(CancelCommand))] private bool _isBusy;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private double _percent;
    [ObservableProperty] private bool _canCancel;
    private bool Idle() => !IsBusy;
    private bool Cancellable() => IsBusy && CanCancel;

    public WindowsToolsViewModel(ICommandRunner runner, IWmiQuery wmi, Action<string> open, Func<Action, object> dispatch)
    {
        _runner = runner; _open = open; _dispatch = dispatch;
        PageFile = Describe(TryRead(() => WmiPageFile.Read(wmi)));
    }

    private static T? TryRead<T>(Func<T> read) where T : class { try { return read(); } catch (Exception e) when (e is System.Management.ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { return null; } }

    internal static IReadOnlyList<InfoRow> Describe(PageFileInfo? p)
    {
        if (p is null) return [new(Loc.Get("Tools_PageFile"), Loc.Get("Value_NotAvailable"))];
        List<InfoRow> rows = [new(Loc.Get("Tools_PageFile_Managed"), p.SystemManaged is { } m ? Loc.Get(m ? "Value_Yes" : "Value_No") : Loc.Get("Value_NotAvailable"))];
        static string Mb(long? v) => v is { } x ? x.ToString(CultureInfo.InvariantCulture) + " MB" : Loc.Get("Value_NotAvailable");
        foreach (var f in p.Files) rows.AddRange([new(Loc.Get("Tools_PageFile"), f.Path), new(Loc.Get("Tools_PageFile_Size"), Mb(f.AllocatedMb)), new(Loc.Get("Tools_PageFile_Used"), Mb(f.CurrentMb)), new(Loc.Get("Tools_PageFile_Peak"), Mb(f.PeakMb))]);
        if (p.Files.Count == 0) rows.Add(new(Loc.Get("Tools_PageFile"), Loc.Get("Tools_PageFile_None")));
        return rows;
    }

    [RelayCommand(CanExecute = nameof(Idle))] private Task Sfc() => RunAsync("sfc.exe", "/scannow", Encoding.Unicode, SfcExecutor.ParseSfc, cancellable: true);
    [RelayCommand(CanExecute = nameof(Idle))] private Task DismScan() => RunAsync("dism.exe", "/Online /Cleanup-Image /ScanHealth", WindowsTool.Oem, DismScanExecutor.ParseDism, cancellable: true);
    /// <summary>Repairs the component store (downloads from Windows Update when it has to). Not cancellable: stopping DISM half-way through a repair is what can damage the store.</summary>
    [RelayCommand(CanExecute = nameof(Idle))] private Task DismRestore() => RunAsync("dism.exe", "/Online /Cleanup-Image /RestoreHealth", WindowsTool.Oem, DismScanExecutor.ParseDism, cancellable: false);
    [RelayCommand(CanExecute = nameof(Cancellable))] private void Cancel() => _cts?.Cancel();
    [RelayCommand] private void DiskCleanup() => _open(Path.Combine(Environment.SystemDirectory, "cleanmgr.exe"));
    [RelayCommand] private void WindowsUpdate() => _open("ms-settings:windowsupdate");
    [RelayCommand] private void PageFileSettings() => _open(Path.Combine(Environment.SystemDirectory, "SystemPropertiesPerformance.exe"));

    private async Task RunAsync(string file, string arguments, Encoding encoding, Func<IReadOnlyList<string>, WindowsHealth> parse, bool cancellable)
    {
        IsBusy = true; CanCancel = cancellable; Percent = 0; Output.Clear(); Status = Loc.Format("Tools_Running", $"{file} {arguments}");
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _runner.RunAsync(file, arguments, encoding, line => _dispatch(() => Append(line)), _cts.Token).ConfigureAwait(true);
            Status = Loc.Get("Tools_Result_" + parse(result.Output)); Percent = 100;
        }
        catch (OperationCanceledException) { Status = Loc.Get("Bench_Status_Cancelled"); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Status = Loc.Format("Tools_CouldNotRun", file, e.Message); }
        finally { _cts.Dispose(); _cts = null; IsBusy = false; CanCancel = false; }
    }

    /// <summary>Progress lines update the bar instead of filling the log; the log keeps the last <see cref="MaxLines"/> lines.</summary>
    internal void Append(string line)
    {
        if (WindowsTool.ProgressOf(line) is { } p) { Percent = p * 100; return; }
        Output.Add(line); while (Output.Count > MaxLines) Output.RemoveAt(0);
    }
}
