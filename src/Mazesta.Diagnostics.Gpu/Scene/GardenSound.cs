using System.Numerics; using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// Plays <see cref="GardenSoundMix"/> through the default sound device (winmm's waveOut: no package, works on every Windows) while a garden window is open. A thread of its own keeps four short buffers filled; the frame loop only calls
/// <see cref="Listen"/> with where the camera is, which sets three numbers. With no sound device the garden is silent and nothing else changes.
/// </summary>
internal sealed unsafe partial class GardenSound : IDisposable
{
    private const int Buffers = 4, Frames = 2048;   // 43 ms each
    [StructLayout(LayoutKind.Sequential)] private struct WaveFormat { public ushort Tag, Channels; public uint Rate, BytesPerSecond; public ushort BlockAlign, Bits, Size; }
    [StructLayout(LayoutKind.Sequential)] private struct WaveHeader { public byte* Data; public uint Length, Recorded; public nint User; public uint Flags, Loops; public nint Next, Reserved; }
    private const uint Done = 1, Prepared = 2;
    [LibraryImport("winmm.dll")] private static partial uint waveOutOpen(out nint handle, uint device, in WaveFormat format, nint callback, nint instance, uint flags);
    [LibraryImport("winmm.dll")] private static partial uint waveOutPrepareHeader(nint handle, WaveHeader* header, uint size);
    [LibraryImport("winmm.dll")] private static partial uint waveOutUnprepareHeader(nint handle, WaveHeader* header, uint size);
    [LibraryImport("winmm.dll")] private static partial uint waveOutWrite(nint handle, WaveHeader* header, uint size);
    [LibraryImport("winmm.dll")] private static partial uint waveOutReset(nint handle);
    [LibraryImport("winmm.dll")] private static partial uint waveOutClose(nint handle);

    private readonly GardenSoundMix _mix = new(); private readonly Vector3? _fountain; private readonly Thread? _thread; private volatile bool _stop;
    private nint _device; private readonly WaveHeader* _headers; private readonly short[][] _pcm = new short[Buffers][]; private GCHandle[] _pins = new GCHandle[Buffers];

    public GardenSound(Vector3? fountain)
    {
        _fountain = fountain;
        var format = new WaveFormat { Tag = 1, Channels = 2, Rate = GardenSoundMix.Rate, BytesPerSecond = GardenSoundMix.Rate * 4, BlockAlign = 4, Bits = 16 };
        if (waveOutOpen(out _device, 0xFFFFFFFF, format, 0, 0, 0) != 0) { _device = 0; return; }   // no device: silent
        _headers = (WaveHeader*)NativeMemory.AllocZeroed((nuint)(Buffers * sizeof(WaveHeader)));
        for (int i = 0; i < Buffers; i++) { _pcm[i] = new short[Frames * 2]; _pins[i] = GCHandle.Alloc(_pcm[i], GCHandleType.Pinned); _headers[i].Data = (byte*)_pins[i].AddrOfPinnedObject(); _headers[i].Length = Frames * 4; }
        _thread = new Thread(Pump) { IsBackground = true, Name = "Mazesta garden sound", Priority = ThreadPriority.AboveNormal }; _thread.Start();
    }

    /// <summary>Tells the mix where the listener is (the camera's eye at the walk's <paramref name="time"/>).</summary>
    public void Listen(Vector3 eye, float time) => _mix.Set(SoundLevels.At(eye, _fountain, time));

    private void Pump()
    {
        for (int i = 0; i < Buffers; i++) { _mix.Render(_pcm[i], Frames); waveOutPrepareHeader(_device, &_headers[i], (uint)sizeof(WaveHeader)); waveOutWrite(_device, &_headers[i], (uint)sizeof(WaveHeader)); }
        while (!_stop)
        {
            bool idle = true;
            for (int i = 0; i < Buffers && !_stop; i++)
                if ((_headers[i].Flags & Done) != 0) { _mix.Render(_pcm[i], Frames); waveOutWrite(_device, &_headers[i], (uint)sizeof(WaveHeader)); idle = false; }
            if (idle) Thread.Sleep(8);
        }
    }

    public void Dispose()
    {
        if (_device == 0) return;
        _stop = true; _thread?.Join(); waveOutReset(_device);
        for (int i = 0; i < Buffers; i++) { waveOutUnprepareHeader(_device, &_headers[i], (uint)sizeof(WaveHeader)); _pins[i].Free(); }
        waveOutClose(_device); NativeMemory.Free(_headers); _device = 0;
    }
}
