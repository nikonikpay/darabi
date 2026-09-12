using Mazesta.Core.Hardware; using Mazesta.Desktop.ViewModels;
namespace Mazesta.Desktop.Services;
public sealed class NoOpChartWindowService : IChartWindowService
{
    public void Open(SensorDefinition s, HardwareNode n) { }
}
