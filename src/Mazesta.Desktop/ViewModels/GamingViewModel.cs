using System.Collections.ObjectModel; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input;
using Mazesta.Desktop.Localization; using Mazesta.Diagnostics.Windows;
namespace Mazesta.Desktop.ViewModels;

public sealed record PowerPlanRow(PowerPlan Plan) { public string Name => Plan.Name; public bool IsActive => Plan.IsActive; }

/// <summary>
/// Gaming (spec 11.1): the Windows power plan - listed and switched with powercfg, which is reversible by switching back - and the state of
/// Game Mode and hardware-accelerated GPU scheduling, read from the registry and changed only in Windows' own settings pages (opened from
/// here). GPU overclocking and undervolting live on their own page (TuningViewModel); an FPS overlay is not offered.
/// </summary>
public sealed partial class GamingViewModel : ObservableObject
{
    private readonly ICommandRunner _runner; private readonly Action<string> _open;
    public ObservableCollection<PowerPlanRow> Plans { get; } = [];
    public string GameMode { get; }
    public string GpuScheduling { get; }
    [ObservableProperty] private string _status = "";
    public Task Loaded { get; }

    public GamingViewModel(ICommandRunner runner, Action<string> open, GamingStatus status)
    {
        _runner = runner; _open = open;
        GameMode = State(status.GameMode); GpuScheduling = State(status.GpuScheduling);
        Loaded = LoadAsync();
    }

    private static string State(bool? on) => Loc.Get(on switch { true => "Gaming_On", false => "Gaming_Off", null => "Gaming_Default" });

    private async Task LoadAsync()
    {
        try
        {
            var plans = PowerPlans.Parse((await _runner.RunAsync("powercfg.exe", "/list", WindowsTool.Oem, null, CancellationToken.None).ConfigureAwait(true)).Output);
            Plans.Clear(); foreach (var p in plans) Plans.Add(new(p));
            if (plans.Count == 0) Status = Loc.Get("Gaming_NoPlans");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Status = Loc.Format("Tools_CouldNotRun", "powercfg", e.Message); }
    }

    [RelayCommand]
    private async Task Activate(PowerPlanRow row)
    {
        if (row.IsActive) return;
        var result = await _runner.RunAsync("powercfg.exe", $"/setactive {row.Plan.Id}", WindowsTool.Oem, null, CancellationToken.None).ConfigureAwait(true);
        Status = result.ExitCode == 0 ? Loc.Format("Gaming_PlanActivated", row.Name) : Loc.Format("Tools_CouldNotRun", "powercfg", string.Join(" ", result.Output));
        await LoadAsync().ConfigureAwait(true);   // show what Windows now says is active, not what was asked for
    }

    [RelayCommand] private void OpenGameMode() => _open("ms-settings:gaming-gamemode");
    [RelayCommand] private void OpenGraphics() => _open("ms-settings:display-advancedgraphics");
}
