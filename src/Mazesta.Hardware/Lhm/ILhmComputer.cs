using LibreHardwareMonitor.Hardware;
namespace Mazesta.Hardware.Lhm;
public interface ILhmComputer : IDisposable { void Open(); void Close(); IReadOnlyList<IHardware> Hardware { get; } }
