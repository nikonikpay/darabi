using LibreHardwareMonitor.Hardware;
namespace Mazesta.Hardware.Lhm;
internal sealed class LhmComputerAdapter : ILhmComputer
{
    private readonly Computer _computer = new()
    { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true, IsStorageEnabled = false, IsNetworkEnabled = true,
      IsControllerEnabled = false, IsPsuEnabled = false, IsBatteryEnabled = false, IsPowerMonitorEnabled = false };
    private bool _closed;
    /// <summary>Drives are opened after the rest and waited for 3 s at most: a USB stick or external disk still being recognised held the
    /// whole start (and the overlay the tray asks for) until it was pulled out. A drive that answers later is left out of this run.</summary>
    public void Open()
    {
        _computer.Open();
        StorageSkipped = !Task.Run(() => _computer.IsStorageEnabled = true).Wait(TimeSpan.FromSeconds(3));
    }
    public bool StorageSkipped { get; private set; }
    public void Close() { if (_closed) return; _closed = true; _computer.Close(); }
    public IReadOnlyList<IHardware> Hardware => _computer.Hardware.ToList();
    public void Dispose() => Close();
}
