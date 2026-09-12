using LibreHardwareMonitor.Hardware;
namespace Mazesta.Hardware.Lhm;
internal sealed class LhmComputerAdapter : ILhmComputer
{
    private readonly Computer _computer = new()
    { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true, IsStorageEnabled = true, IsNetworkEnabled = true,
      IsControllerEnabled = false, IsPsuEnabled = false, IsBatteryEnabled = false, IsPowerMonitorEnabled = false };
    private bool _closed;
    public void Open() => _computer.Open();
    public void Close() { if (_closed) return; _closed = true; _computer.Close(); }
    public IReadOnlyList<IHardware> Hardware => _computer.Hardware.ToList();
    public void Dispose() => Close();
}
