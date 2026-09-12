using System.Collections.ObjectModel; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input; using Mazesta.Core.Hardware; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Desktop.ViewModels;
public interface IChartWindowService { void Open(SensorDefinition sensor, HardwareNode node); }
public sealed partial class MonitoringViewModel : ObservableObject, IDisposable
{
    private readonly PollingEngine _engine; private readonly MonitoringFocus _focus; private readonly AppConfig _config; private readonly IChartWindowService _charts; private readonly Func<Action, object> _dispatch;
    private readonly Dictionary<SensorId, SensorRowViewModel> _rows = [];
#pragma warning disable CS0649 // reserved for a future task that needs to mutate IsExpanded without persisting it
    private bool _suppressConfig;
#pragma warning restore CS0649
    public ObservableCollection<HardwareGroupViewModel> Groups { get; } = [];
    public int[] Intervals => MonitoringOptions.AllowedFastSeconds;
    [ObservableProperty] private string _filterText = ""; [ObservableProperty] private int _selectedIntervalSeconds; [ObservableProperty] private bool _isPaused;
    public MonitoringViewModel(PollingEngine engine, MonitoringFocus focus, AppConfig config, IChartWindowService charts, Func<Action, object> dispatch)
    {
        _engine = engine; _focus = focus; _config = config; _charts = charts; _dispatch = dispatch; _selectedIntervalSeconds = (int)engine.FastInterval.TotalSeconds; _isPaused = engine.State == EngineState.Paused;
        var byId = engine.Hardware.ToDictionary(n => n.Id);
        foreach (var node in engine.Hardware.Where(n => n.ParentId is null))
        {
            var g = new HardwareGroupViewModel(node) { IsExpanded = config.ExpandedGroups.Contains(node.Id.Value) };
            AddRows(g, node, ""); foreach (var sub in engine.Hardware.Where(n => n.ParentId == node.Id)) AddRows(g, sub, sub.Name);
            g.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HardwareGroupViewModel.IsExpanded) && !_suppressConfig) Persist(g); };
            Groups.Add(g);
        }
        engine.SnapshotPublished += OnSnapshot; engine.StateChanged += OnState; focus.FocusRequested += OnFocus;
    }
    private void AddRows(HardwareGroupViewModel g, HardwareNode node, string sub) { foreach (var s in node.Sensors) { var row = new SensorRowViewModel(s, sub); _rows[s.Id] = row; g.Rows.Add(row); } }
    private void Persist(HardwareGroupViewModel g) { if (g.IsExpanded) { if (!_config.ExpandedGroups.Contains(g.Id)) _config.ExpandedGroups.Add(g.Id); } else _config.ExpandedGroups.Remove(g.Id); }
    private void OnSnapshot(SensorSnapshot s) => _dispatch(() => ApplySnapshot(s));
    private void OnState(EngineState s) => _dispatch(() => IsPaused = s == EngineState.Paused);
    private void OnFocus(FocusRequest r) => _dispatch(() => ApplyFocus(r));
    internal void ApplySnapshot(SensorSnapshot s) { foreach (var r in s.Readings) if (_rows.TryGetValue(r.Id, out var row)) row.Apply(r, _engine.Statistics.Get(r.Id)); }
    internal void ApplyFocus(FocusRequest r) { foreach (var g in Groups) g.IsExpanded = r.Kinds.Contains(g.Kind); }
    partial void OnFilterTextChanged(string value)
    {
        foreach (var g in Groups)
        {
            bool groupMatch = value.Length == 0 || g.Title.Contains(value, StringComparison.OrdinalIgnoreCase);
            bool any = false; foreach (var row in g.Rows) { row.IsVisible = groupMatch || row.Name.Contains(value, StringComparison.OrdinalIgnoreCase) || row.SubGroup.Contains(value, StringComparison.OrdinalIgnoreCase); any |= row.IsVisible; }
            g.IsVisible = any;
        }
    }
    partial void OnSelectedIntervalSecondsChanged(int value) { _engine.SetFastInterval(TimeSpan.FromSeconds(value)); _config.FastIntervalSeconds = value; }
    [RelayCommand] private void ResetStats() => _engine.Statistics.ResetAll(DateTimeOffset.UtcNow);
    [RelayCommand] private void TogglePause() { if (_engine.State == EngineState.Paused) _engine.Resume(); else _engine.Pause(); }
    [RelayCommand] private void OpenChart(SensorRowViewModel? row) { if (row is null) return; var node = _engine.Hardware.First(n => n.Id == row.Definition.Hardware); _charts.Open(row.Definition, node); }
    public void Dispose() { _engine.SnapshotPublished -= OnSnapshot; _engine.StateChanged -= OnState; _focus.FocusRequested -= OnFocus; }
}
