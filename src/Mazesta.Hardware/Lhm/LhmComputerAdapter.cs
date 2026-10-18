using LibreHardwareMonitor.Hardware;
namespace Mazesta.Hardware.Lhm;
internal sealed class LhmComputerAdapter : ILhmComputer
{
    private readonly Computer _computer = new()
    { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true, IsStorageEnabled = false, IsNetworkEnabled = true,
      IsControllerEnabled = false, IsPsuEnabled = false, IsBatteryEnabled = false, IsPowerMonitorEnabled = false };
    private bool _closed;
    private Task _storage = Task.CompletedTask;
    /// <summary>Drives are opened after the rest and waited for 10 s at most: a USB stick or external disk still being recognised held the
    /// whole start (and the overlay the tray asks for) until it was pulled out. LHM opens every drive in one call, so they cannot be waited on one
    /// by one; past the wait the start goes on and the drives join once that call returns (see <see cref="LibreHardwareMonitorProvider"/>.Poll).</summary>
    public void Open()
    {
        _computer.Open();
        _storage = Task.Run(() => _computer.IsStorageEnabled = true); _storage.Wait(TimeSpan.FromSeconds(10));
    }
    public bool StorageSkipped => !_storage.IsCompleted;
    public void Close() { if (_closed) return; _closed = true; _computer.Close(); }
    public IReadOnlyList<IHardware> Hardware => _computer.Hardware.ToList();
    public void Dispose() => Close();
}
