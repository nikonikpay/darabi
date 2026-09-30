using System.Buffers.Binary; using System.Net; using System.Net.Sockets;
namespace Mazesta.Diagnostics.Network;

/// <summary>
/// The stream both ends of the LAN test agree on: byte k of a session is byte (k mod 8) of splitmix64(seed + k / 8). Whoever receives it
/// regenerates the same bytes and counts every one that differs, so data damaged on the way (a network card, its driver or its memory that
/// corrupts what TCP's own checksum then passes, or a checksum offload that lies) is caught end to end, not only lost packets.
/// </summary>
public sealed class LanPattern(uint seed)
{
    private long _position;
    public long Position => _position;

    public static ulong Word(uint seed, long index)
    {
        ulong z = unchecked(seed + (ulong)index * 0x9E3779B97F4A7C15UL);
        z = unchecked((z ^ z >> 30) * 0xBF58476D1CE4E5B9UL); z = unchecked((z ^ z >> 27) * 0x94D049BB133111EBUL);
        return z ^ z >> 31;
    }

    public void Fill(Span<byte> buffer)
    {
        int i = 0;
        for (; i < buffer.Length && (_position & 7) != 0; i++, _position++) buffer[i] = (byte)(Word(seed, _position >> 3) >> (int)((_position & 7) * 8));
        for (; i + 8 <= buffer.Length; i += 8, _position += 8) BinaryPrimitives.WriteUInt64LittleEndian(buffer[i..], Word(seed, _position >> 3));
        for (; i < buffer.Length; i++, _position++) buffer[i] = (byte)(Word(seed, _position >> 3) >> (int)((_position & 7) * 8));
    }

    /// <summary>Counts the bytes of <paramref name="received"/> that differ from the stream at the current position, and moves past them.</summary>
    public long Check(ReadOnlySpan<byte> received)
    {
        long bad = 0; int i = 0;
        // Byte by byte up to a word boundary, then whole words, then the tail.
        for (; i < received.Length && (_position & 7) != 0; i++, _position++) if (received[i] != (byte)(Word(seed, _position >> 3) >> (int)((_position & 7) * 8))) bad++;
        for (; i + 8 <= received.Length; i += 8, _position += 8)
        {
            ulong diff = BinaryPrimitives.ReadUInt64LittleEndian(received[i..]) ^ Word(seed, _position >> 3);
            if (diff != 0) for (int b = 0; b < 8; b++) if ((diff >> (b * 8) & 0xFF) != 0) bad++;
        }
        for (; i < received.Length; i++, _position++) if (received[i] != (byte)(Word(seed, _position >> 3) >> (int)((_position & 7) * 8))) bad++;
        return bad;
    }
}

/// <summary>What a session asks the partner to do.</summary>
public enum LanMode : byte { Receive = 1, Send = 2, Echo = 3 }

/// <summary>
/// The partner side of the LAN test: listens on <see cref="Port"/> while the technician keeps it switched on, and serves each connection as
/// its 16-byte hello asks - receive and check the pattern (then answer bytes received and bytes wrong), send the pattern until the other side
/// hangs up, or echo 64-byte messages back. It holds no state between sessions and answers nothing that does not start with the magic.
/// </summary>
public sealed class LanPeerServer : IAsyncDisposable
{
    public const int Port = 47315;
    public static ReadOnlySpan<byte> Magic => "MZLAN1\0\0"u8;
    public const int HelloBytes = 16, EchoBytes = 64, ChunkBytes = 256 << 10;

    private readonly TcpListener _listener; private readonly CancellationTokenSource _cts = new(); private readonly Task _loop;
    public int Sessions => _sessions;
    private int _sessions;

    public LanPeerServer(int port = Port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _loop = AcceptLoop();
    }
    public int BoundPort => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false); }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            _ = Serve(client);
        }
    }

    private async Task Serve(TcpClient client)
    {
        using var _ = client; Interlocked.Increment(ref _sessions);
        client.NoDelay = true;
        var stream = client.GetStream(); var ct = _cts.Token;
        try
        {
            var hello = new byte[HelloBytes];
            await stream.ReadExactlyAsync(hello, ct).ConfigureAwait(false);
            if (!hello.AsSpan(0, 8).SequenceEqual(Magic)) return;
            var mode = (LanMode)hello[8]; uint seed = BinaryPrimitives.ReadUInt32LittleEndian(hello.AsSpan(12));
            var buffer = new byte[ChunkBytes];
            switch (mode)
            {
                case LanMode.Receive:
                {
                    var pattern = new LanPattern(seed); long bad = 0; int n;
                    while ((n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0) bad += pattern.Check(buffer.AsSpan(0, n));
                    var answer = new byte[16]; BinaryPrimitives.WriteInt64LittleEndian(answer, pattern.Position); BinaryPrimitives.WriteInt64LittleEndian(answer.AsSpan(8), bad);
                    await stream.WriteAsync(answer, ct).ConfigureAwait(false);
                    break;
                }
                case LanMode.Send:
                {
                    var pattern = new LanPattern(seed);
                    while (true) { pattern.Fill(buffer); await stream.WriteAsync(buffer, ct).ConfigureAwait(false); }
                }
                case LanMode.Echo:
                {
                    var message = new byte[EchoBytes];
                    while (true) { await stream.ReadExactlyAsync(message, ct).ConfigureAwait(false); await stream.WriteAsync(message, ct).ConfigureAwait(false); }
                }
            }
        }
        catch (Exception e) when (e is IOException or SocketException or EndOfStreamException or OperationCanceledException or ObjectDisposedException) { }   // the other side hung up
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel(); _listener.Stop();
        try { await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}
