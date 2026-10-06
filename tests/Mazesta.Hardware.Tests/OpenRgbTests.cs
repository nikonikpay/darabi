using System.Buffers.Binary; using System.IO; using System.Net; using System.Net.Sockets; using System.Text; using Xunit; using Mazesta.Hardware.Rgb;
namespace Mazesta.Hardware.Tests;

public class OpenRgbTests
{
    private static void Str(BinaryWriter w, string s) { var b = Encoding.UTF8.GetBytes(s); w.Write((ushort)(b.Length + 1)); w.Write(b); w.Write((byte)0); }

    /// <summary>A device as an OpenRGB 3 server describes it: a RAM stick with a Direct and a Static mode, one zone with a matrix, 4 LEDs.</summary>
    private static byte[] Description(int type = 1, bool matrix = true, uint zoneMin = 4, uint zoneMax = 4, uint zoneCount = 4)
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(0u); w.Write(type); Str(w, "Corsair Vengeance RGB"); Str(w, "Corsair"); Str(w, "DRAM"); Str(w, "1.0"); Str(w, "SN"); Str(w, "I2C: SMBus 0x19");
        w.Write((ushort)2); w.Write(0u);
        Mode(w, "Direct", 0, 32, 1); Mode(w, "Static", 1, 64, 2);
        w.Write((ushort)1); Str(w, "DRAM"); w.Write(1u); w.Write(zoneMin); w.Write(zoneMax); w.Write(zoneCount);
        if (matrix) { w.Write((ushort)(8 + 4 * 4)); w.Write(1u); w.Write(4u); for (uint i = 0; i < 4; i++) w.Write(i); } else w.Write((ushort)0);
        w.Write((ushort)4); for (int i = 0; i < 4; i++) { Str(w, $"LED {i}"); w.Write(0u); }
        w.Write((ushort)4); for (int i = 0; i < 4; i++) { w.Write((byte)10); w.Write((byte)20); w.Write((byte)30); w.Write((byte)0); }
        w.Flush(); var b = s.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)b.Length); return b;
    }

    private static void Mode(BinaryWriter w, string name, uint value, uint flags, uint colorMode)
    {
        Str(w, name); w.Write(value); w.Write(flags); w.Write(0u); w.Write(0u); w.Write(0u); w.Write(100u); w.Write(0u); w.Write(colorMode == 2 ? 1u : 0u); w.Write(0u); w.Write(100u);
        w.Write(0u); w.Write(colorMode); w.Write((ushort)(colorMode == 2 ? 1 : 0)); if (colorMode == 2) { w.Write((byte)1); w.Write((byte)2); w.Write((byte)3); w.Write((byte)0); }
    }

    [Fact] public void A_device_description_is_read_with_its_maker_modes_and_leds()
    {
        var d = OpenRgbProtocol.Device(3, Description(), 3);
        Assert.Equal(3, d.Index); Assert.Equal(RgbKind.Memory, d.Kind); Assert.Equal("Corsair Vengeance RGB", d.Name); Assert.Equal("Corsair", d.Vendor); Assert.Equal("I2C: SMBus 0x19", d.Location);
        Assert.Equal(["Direct", "Static"], d.Modes.Select(m => m.Name)); Assert.True(d.Modes[0].PerLed); Assert.True(d.Modes[1].ModeColors); Assert.Equal(new RgbColor(1, 2, 3), d.Modes[1].Colors[0]);
        Assert.Equal(4, d.LedCount); Assert.Equal(new RgbColor(10, 20, 30), d.Colors[0]);
    }

    [Fact] public void A_zone_without_a_matrix_and_other_kinds_are_read_too()
    {
        var d = OpenRgbProtocol.Device(0, Description(type: 2, matrix: false), 3);
        Assert.Equal(RgbKind.Graphics, d.Kind); Assert.Equal(4, d.LedCount);
        Assert.Equal(RgbKind.Motherboard, OpenRgbProtocol.Kind(0)); Assert.Equal(RgbKind.Cooler, OpenRgbProtocol.Kind(3)); Assert.Equal(RgbKind.Other, OpenRgbProtocol.Kind(-1));
    }

    [Fact] public void A_cut_off_description_is_refused_not_guessed()
        => Assert.Throws<FormatException>(() => OpenRgbProtocol.Device(0, Description()[..60], 3));

    [Fact] public void Packets_carry_the_signature_the_index_the_id_and_the_size()
    {
        var p = OpenRgbProtocol.Packet(2, 1050, OpenRgbProtocol.Leds([new RgbColor(255, 0, 7)]));
        Assert.True(OpenRgbProtocol.ReadHeader(p, out int dev, out int id, out int size)); Assert.Equal((2, 1050, 10), (dev, id, size));
        Assert.Equal(new byte[] { 255, 0, 7, 0 }, p[(16 + 6)..]); Assert.False(OpenRgbProtocol.ReadHeader("XXXX000000000000"u8, out _, out _, out _));
    }

    [Fact] public void A_mode_written_back_reads_the_same()
    {
        var m = OpenRgbProtocol.Device(0, Description(), 3).Modes[1] with { Colors = [new RgbColor(9, 8, 7)] };
        var b = OpenRgbProtocol.Mode(1, m, 3);
        Assert.Equal((uint)b.Length, BinaryPrimitives.ReadUInt32LittleEndian(b)); Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4)));
        Assert.Equal([9, 8, 7, 0], b[^4..]);
    }

    [Fact] public void Hex_colours_parse_and_bad_ones_do_not()
    {
        Assert.Equal(new RgbColor(255, 128, 0), RgbColor.Parse("#ff8000")); Assert.Equal("#ff8000", new RgbColor(255, 128, 0).Hex);
        Assert.Null(RgbColor.Parse("ff8000")); Assert.Null(RgbColor.Parse("#ff80")); Assert.Null(RgbColor.Parse("#gg0000")); Assert.Null(RgbColor.Parse(null));
    }

    /// <summary>A one-device server on loopback, answering like OpenRGB; it records the packets it is sent.</summary>
    private static (int Port, List<(int Id, byte[] Body)> Got, Task Done) Serve() => ServeWith(Description());

    private static (int Port, List<(int Id, byte[] Body)> Got, Task Done) ServeWith(byte[] description)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var got = new List<(int, byte[])>();
        var done = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(); using var s = client.GetStream(); var header = new byte[16];
            try
            {
                while (true)
                {
                    await s.ReadExactlyAsync(header); OpenRgbProtocol.ReadHeader(header, out int dev, out int id, out int size); var body = new byte[size]; await s.ReadExactlyAsync(body); got.Add((id, body));
                    byte[]? reply = id switch
                    {
                        OpenRgbProtocol.RequestProtocolVersion => BitConverter.GetBytes(3u), OpenRgbProtocol.RequestControllerCount => BitConverter.GetBytes(1u),
                        OpenRgbProtocol.RequestControllerData => description, _ => null
                    };
                    if (reply is not null) await s.WriteAsync(OpenRgbProtocol.Packet(dev, id, reply));
                }
            }
            catch (EndOfStreamException) { }
            catch (IOException) { }
            finally { listener.Stop(); }
        });
        return (((IPEndPoint)listener.LocalEndpoint).Port, got, done);
    }

    [Fact] public async Task A_colour_goes_to_a_device_in_its_direct_mode_as_one_colour_per_led()
    {
        var (port, got, done) = Serve();
        using (var client = new OpenRgbClient())
        {
            await client.ConnectAsync(port, default);
            var devices = await client.RefreshAsync(default); Assert.Equal("Corsair Vengeance RGB", Assert.Single(devices).Name);
            await client.SetColorAsync(0, new RgbColor(255, 0, 0), default);
        }
        await done;
        var ids = got.Select(g => g.Id).ToList();
        Assert.Equal([OpenRgbProtocol.SetClientName, OpenRgbProtocol.RequestProtocolVersion, OpenRgbProtocol.RequestControllerCount, OpenRgbProtocol.RequestControllerData, OpenRgbProtocol.UpdateMode, OpenRgbProtocol.UpdateLeds], ids);
        var leds = got[^1].Body; Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(leds.AsSpan(4))); Assert.Equal([255, 0, 0, 0], leds[6..10]);
    }

    [Fact] public async Task Each_led_gets_its_own_colour_and_one_past_the_list_stays_dark()
    {
        var (port, got, done) = Serve();
        using (var client = new OpenRgbClient())
        {
            await client.ConnectAsync(port, default); await client.RefreshAsync(default);
            await client.SetLedsAsync(0, [new RgbColor(255, 0, 0), new RgbColor(0, 255, 0), new RgbColor(0, 0, 255)], default);   // the device has four LEDs
        }
        await done;
        Assert.Equal([OpenRgbProtocol.UpdateMode, OpenRgbProtocol.UpdateLeds], got.Select(g => g.Id).TakeLast(2));
        var leds = got[^1].Body; Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(leds.AsSpan(4)));
        Assert.Equal([255, 0, 0, 0, 0, 255, 0, 0, 0, 0, 255, 0, 0, 0, 0, 0], leds[6..22]);
    }

    [Fact] public async Task A_device_that_is_not_in_the_list_is_refused()
    {
        var (port, _, done) = Serve();
        using (var client = new OpenRgbClient())
        {
            await client.ConnectAsync(port, default); await client.RefreshAsync(default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SetColorAsync(5, new RgbColor(1, 1, 1), default));
        }
        await done;
    }

    [Fact] public void A_zone_is_read_with_its_range_and_only_a_ranged_one_can_be_resized()
    {
        var fixedZone = OpenRgbProtocol.Device(0, Description(), 3).Zones[0]; var header = OpenRgbProtocol.Device(0, Description(zoneMin: 0, zoneMax: 120, zoneCount: 0), 3).Zones[0];
        Assert.False(fixedZone.Resizable); Assert.True(header.Resizable); Assert.Equal((0u, 120u, 0u), (header.LedsMin, header.LedsMax, header.LedsCount));
    }

    [Fact] public void The_resize_payload_is_the_zone_and_the_new_count() => Assert.Equal(new byte[] { 1, 0, 0, 0, 30, 0, 0, 0 }, OpenRgbProtocol.Resize(1, 30));

    [Fact] public async Task A_fixed_zone_or_a_count_out_of_range_is_refused()
    {
        var (port, _, done) = Serve();
        using (var client = new OpenRgbClient())
        {
            await client.ConnectAsync(port, default); await client.RefreshAsync(default);
            await Assert.ThrowsAsync<NotSupportedException>(() => client.ResizeZoneAsync(0, 0, 8, default));
        }
        await done;
    }

    [Fact] public async Task A_zone_resize_reaches_the_server_as_its_own_packet()
    {
        var (port, got, done) = ServeWith(Description(zoneMin: 0, zoneMax: 120, zoneCount: 0));
        using (var client = new OpenRgbClient())
        {
            await client.ConnectAsync(port, default); await client.RefreshAsync(default);
            await client.ResizeZoneAsync(0, 0, 30, default);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ResizeZoneAsync(0, 0, 121, default));
        }
        await done; var resize = Assert.Single(got, g => g.Id == OpenRgbProtocol.ResizeZone); Assert.Equal(new byte[] { 0, 0, 0, 0, 30, 0, 0, 0 }, resize.Body);
    }

    [Fact] public async Task A_closed_port_is_found_not_listening_at_once_and_an_open_one_is_found_listening()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int port = ((IPEndPoint)l.LocalEndpoint).Port;
        try { Assert.True(await OpenRgbHost.ListeningAsync(port, default)); } finally { l.Stop(); }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(await OpenRgbHost.ListeningAsync(port, default));   // closed now: the table of listeners says so without a connect (which takes a second or more on a closed local port)
        Assert.True(watch.ElapsedMilliseconds < 400, $"took {watch.ElapsedMilliseconds} ms");
    }
}
