using System.Buffers.Binary; using System.Net.Sockets;
namespace Mazesta.Hardware.Rgb;

/// <summary>
/// A client of OpenRGB's SDK server (the program's "--server" mode, local port 6742 unless told otherwise). OpenRGB is a separate program that
/// speaks to the lights of Corsair, ASUS Aura, Gigabyte RGB Fusion, MSI Mystic Light and many other makers' memory, graphics cards, boards,
/// fans and strips; this app only talks to it over the socket, so none of its (GPL) code is part of this one. Calls are one at a time.
/// </summary>
public sealed class OpenRgbClient : IDisposable
{
    public const int DefaultPort = 6742;
    private TcpClient? _tcp; private NetworkStream? _stream; private uint _version; private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<RgbDevice> _devices = [];

    public bool Connected => _tcp?.Connected == true;
    public IReadOnlyList<RgbDevice> Devices => _devices;

    public async Task ConnectAsync(int port, CancellationToken ct)
    {
        Close(); var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await tcp.ConnectAsync("127.0.0.1", port, timeout.Token).ConfigureAwait(false);
        }
        catch { tcp.Dispose(); throw; }
        _tcp = tcp; _stream = tcp.GetStream();
        await SendAsync(0, OpenRgbProtocol.SetClientName, "Mazesta\0"u8.ToArray(), ct).ConfigureAwait(false);
        var ask = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(ask, OpenRgbProtocol.ClientVersion);
        var answer = await RequestAsync(0, OpenRgbProtocol.RequestProtocolVersion, ask, ct).ConfigureAwait(false);
        _version = answer.Length >= 4 ? Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(answer), OpenRgbProtocol.ClientVersion) : 0;   // an old server never answers with more than it knows
    }

    /// <summary>Reads the devices the server knows now (a server that has just started may still be detecting: ask again a little later).</summary>
    public async Task<IReadOnlyList<RgbDevice>> RefreshAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var count = await RequestAsync(0, OpenRgbProtocol.RequestControllerCount, [], ct).ConfigureAwait(false);
            int n = count.Length >= 4 ? (int)BinaryPrimitives.ReadUInt32LittleEndian(count) : 0; var list = new List<RgbDevice>(n);
            var ask = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(ask, _version);
            for (int i = 0; i < n; i++) list.Add(OpenRgbProtocol.Device(i, await RequestAsync(i, OpenRgbProtocol.RequestControllerData, ask, ct).ConfigureAwait(false), _version));
            return _devices = list;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Paints a device one colour, or switches it off (<paramref name="color"/> null): in its "Direct" mode (a colour for each LED) when it has one, else
    /// "Static" (the mode's own colour), else the mode it is in. A device whose modes take no colour at all is refused, not guessed at.</summary>
    public async Task SetColorAsync(int device, RgbColor? color, CancellationToken ct)
    {
        var d = Find(device);
        if (color is null && d.Modes.FirstOrDefault(m => m.Name.Equals("Off", StringComparison.OrdinalIgnoreCase)) is { } off) { await SendModeAsync(d, off, null, ct).ConfigureAwait(false); return; }
        var mode = Named(d, "Direct") ?? Named(d, "Static") ?? Named(d, "Custom") ?? (d.ActiveMode >= 0 && d.ActiveMode < d.Modes.Count ? d.Modes[d.ActiveMode] : null);
        if (mode is null || !(mode.PerLed || mode.ModeColors)) throw new NotSupportedException($"{d.Name} has no mode that takes a colour.");
        await SendModeAsync(d, mode, color ?? new RgbColor(0, 0, 0), ct).ConfigureAwait(false);
    }

    /// <summary>Puts a device in one of its modes (an index into <see cref="RgbDevice.Modes"/>); a colour is used only if the mode takes one.</summary>
    public async Task SetModeAsync(int device, int mode, RgbColor? color, CancellationToken ct)
    {
        var d = Find(device);
        if (mode < 0 || mode >= d.Modes.Count) throw new ArgumentOutOfRangeException(nameof(mode));
        await SendModeAsync(d, d.Modes[mode], color, ct).ConfigureAwait(false);
    }

    private RgbDevice Find(int device) => _devices.FirstOrDefault(d => d.Index == device) ?? throw new InvalidOperationException("That device is not in the list; refresh it.");
    private static RgbMode? Named(RgbDevice d, string name) => d.Modes.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private async Task SendModeAsync(RgbDevice d, RgbMode mode, RgbColor? color, CancellationToken ct)
    {
        var send = mode;
        if (color is { } c && mode.ModeColors)
        {
            int n = Math.Max(1, (int)Math.Clamp(1u, mode.ColorsMin, Math.Max(mode.ColorsMin, mode.ColorsMax))); send = mode with { Colors = Enumerable.Repeat(c, n).ToList() };
        }
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SendAsync(d.Index, OpenRgbProtocol.UpdateMode, OpenRgbProtocol.Mode(d.Modes.ToList().IndexOf(mode), send, _version), ct).ConfigureAwait(false);
            if (color is { } leds && mode.PerLed) await SendAsync(d.Index, OpenRgbProtocol.UpdateLeds, OpenRgbProtocol.Leds(Enumerable.Repeat(leds, Math.Max(d.LedCount, 1)).ToList()), ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task SendAsync(int device, int id, byte[] payload, CancellationToken ct)
    {
        var stream = _stream ?? throw new InvalidOperationException("Not connected to OpenRGB.");
        await stream.WriteAsync(OpenRgbProtocol.Packet(device, id, payload), ct).ConfigureAwait(false);
    }

    private async Task<byte[]> RequestAsync(int device, int id, byte[] payload, CancellationToken ct)
    {
        await SendAsync(device, id, payload, ct).ConfigureAwait(false);
        var stream = _stream!; var header = new byte[OpenRgbProtocol.HeaderSize];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)   // the server may push other packets (a device list that changed) before the answer
        {
            await stream.ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
            if (!OpenRgbProtocol.ReadHeader(header, out _, out int got, out int size) || size > 1 << 24) throw new InvalidDataException("OpenRGB sent something that is not its protocol.");
            var body = new byte[size]; await stream.ReadExactlyAsync(body, timeout.Token).ConfigureAwait(false);
            if (got == id) return body;
        }
    }

    public void Disconnect() => Close();
    private void Close() { _stream?.Dispose(); _tcp?.Dispose(); _stream = null; _tcp = null; _devices = []; }
    public void Dispose() { Close(); _gate.Dispose(); }
}
