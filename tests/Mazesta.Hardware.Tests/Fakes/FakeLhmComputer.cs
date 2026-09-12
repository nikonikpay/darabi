using LibreHardwareMonitor.Hardware; using Mazesta.Hardware.Lhm;
namespace Mazesta.Hardware.Tests.Fakes;
public sealed class FakeLhmComputer : ILhmComputer
{
    public List<IHardware> Roots { get; } = [];
    public Exception? ThrowOnOpen; public Exception? ThrowOnDispose; public bool Opened, Closed, Disposed;
    public IReadOnlyList<IHardware> Hardware => Roots;
    public void Open() { if (ThrowOnOpen is not null) throw ThrowOnOpen; Opened = true; }
    public void Close() => Closed = true;
    public void Dispose() { Disposed = true; if (ThrowOnDispose is not null) throw ThrowOnDispose; }
}
