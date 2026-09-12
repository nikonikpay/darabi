using System.Windows.Media; using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization; using Mazesta.Monitoring;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class ChartWindowViewModel : ObservableObject, IDisposable
{
    private readonly PollingEngine _engine; private readonly Func<Action, object> _dispatch;
    public SensorDefinition Sensor { get; } public HardwareNode Node { get; }
    public string Title => $"{Node.Name} — {Sensor.Name} ({Units.Symbol(Sensor.Unit)})"; public string UnitSymbol => Units.Symbol(Sensor.Unit);
    public int[] WindowChoices => [1, 5, 10, 30, 60, 360, 1440];
    [ObservableProperty] private int _windowMinutes = 10; [ObservableProperty] private RawSeries _raw; [ObservableProperty] private MinuteSeries _minutes; [ObservableProperty] private bool _useMinutes;
    [ObservableProperty] private string _currentText = Loc.Get("Value_NotAvailable"); [ObservableProperty] private string _minText = ""; [ObservableProperty] private string _maxText = ""; [ObservableProperty] private int _nowSeconds;
    [ObservableProperty] private int _maxGapSeconds = 6;
    public Brush SeriesBrush => SeriesBrushes.For(Node.Kind);
    public ChartWindowViewModel(PollingEngine engine, SensorDefinition sensor, HardwareNode node, Func<Action, object> dispatch)
    { _engine = engine; Sensor = sensor; Node = node; _dispatch = dispatch; engine.SnapshotPublished += OnSnapshot; }
    private void OnSnapshot(SensorSnapshot s) => _dispatch(Refresh);
    partial void OnWindowMinutesChanged(int value) => Refresh();
    internal void Refresh()
    {
        int windowSeconds = WindowMinutes * 60; NowSeconds = _engine.History.SecondsSinceEpoch(DateTimeOffset.UtcNow);
        UseMinutes = windowSeconds > _engine.History.RawCapacity * (int)_engine.FastInterval.TotalSeconds;
        MaxGapSeconds = (int)(_engine.FastInterval.TotalSeconds * 3);
        Raw = _engine.History.GetRaw(Sensor.Id); Minutes = _engine.History.GetMinutes(Sensor.Id);
        var last = Raw.Values.Length > 0 ? Raw.Values[^1] : float.NaN; CurrentText = float.IsNaN(last) ? Loc.Get("Value_NotAvailable") : Units.Format(last, Sensor.Unit);
        var st = _engine.Statistics.Get(Sensor.Id); MinText = st.Min is { } mn ? Units.Format(mn, Sensor.Unit) : ""; MaxText = st.Max is { } mx ? Units.Format(mx, Sensor.Unit) : "";
    }
    public void Dispose() => _engine.SnapshotPublished -= OnSnapshot;
}
