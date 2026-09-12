using System.Windows; using Mazesta.Core.Hardware; using Mazesta.Desktop.ViewModels; using Mazesta.Desktop.Views; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Desktop.Services;
public sealed class ChartWindowService(PollingEngine engine, AppConfig config) : IChartWindowService
{
    private readonly List<ChartWindow> _open = [];
    public void Open(SensorDefinition sensor, HardwareNode node) => Open(sensor, node, null, 10);
    private void Open(SensorDefinition sensor, HardwareNode node, WindowPlacement? placement, int minutes)
    {
        var vm = new ChartWindowViewModel(engine, sensor, node, a => Application.Current.Dispatcher.BeginInvoke(a)) { WindowMinutes = minutes };
        var w = new ChartWindow { DataContext = vm, Owner = Application.Current.MainWindow };
        if (placement is { } p) { w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = p.Left; w.Top = p.Top; w.Width = p.Width; w.Height = p.Height; }
        w.Closed += (_, _) => { _open.Remove(w); vm.Dispose(); }; _open.Add(w); w.Show(); vm.Refresh();
    }
    public void RestoreFromConfig()
    {
        foreach (var c in config.ChartWindows.ToList())
        {
            var sensor = engine.Hardware.SelectMany(n => n.Sensors).FirstOrDefault(s => s.Id.Value == c.SensorId); if (sensor is null) continue;
            Open(sensor, engine.Hardware.First(n => n.Id == sensor.Hardware), c.Placement, c.WindowMinutes);
        }
    }
    public void PersistOpenWindows() => config.ChartWindows = _open.Select(w => { var vm = (ChartWindowViewModel)w.DataContext; return new ChartWindowConfig(vm.Sensor.Id.Value, new WindowPlacement(w.Left, w.Top, w.Width, w.Height, false), vm.WindowMinutes); }).ToList();
}
