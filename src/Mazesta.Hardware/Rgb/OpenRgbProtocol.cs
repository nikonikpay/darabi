using System.Buffers.Binary; using System.IO; using System.Text;
namespace Mazesta.Hardware.Rgb;

/// <summary>
/// OpenRGB's SDK wire format (https://gitlab.com/CalcProgrammer1/OpenRGB/-/wikis/OpenRGB-SDK-Documentation), protocol versions 0 to 3: everything little-endian,
/// a packet is "ORGB", device index, packet id and size, then the payload. Kept apart from the socket so it can be tested on bytes. Version 3 is asked for
/// (the server answers with the lower of the two), which is why the segments and zone flags of versions 4 and 5 never appear.
/// </summary>
internal static class OpenRgbProtocol
{
    public const uint ClientVersion = 3;
    public const int RequestControllerCount = 0, RequestControllerData = 1, RequestProtocolVersion = 40, SetClientName = 50, ResizeZone = 1000, UpdateLeds = 1050, UpdateMode = 1101;
    public const int HeaderSize = 16;

    public static byte[] Packet(int device, int id, ReadOnlySpan<byte> payload)
    {
        var b = new byte[HeaderSize + payload.Length];
        "ORGB"u8.CopyTo(b); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), (uint)device); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), (uint)id);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), (uint)payload.Length); payload.CopyTo(b.AsSpan(HeaderSize)); return b;
    }

    public static bool ReadHeader(ReadOnlySpan<byte> h, out int device, out int id, out int size)
    {
        device = id = size = 0;
        if (h.Length < HeaderSize || !h[..4].SequenceEqual("ORGB"u8)) return false;
        device = (int)BinaryPrimitives.ReadUInt32LittleEndian(h[4..]); id = (int)BinaryPrimitives.ReadUInt32LittleEndian(h[8..]); size = (int)BinaryPrimitives.ReadUInt32LittleEndian(h[12..]); return size >= 0;
    }

    /// <summary>The "set LEDs" payload: its own size, the number of colours, then each as red, green, blue and a padding byte.</summary>
    public static byte[] Leds(IReadOnlyList<RgbColor> colors)
    {
        var b = new byte[4 + 2 + colors.Count * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)b.Length); BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), (ushort)colors.Count);
        for (int i = 0; i < colors.Count; i++) { b[6 + i * 4] = colors[i].R; b[7 + i * 4] = colors[i].G; b[8 + i * 4] = colors[i].B; }
        return b;
    }

    /// <summary>The "update mode" payload: its own size, the mode's index, then the mode as a device lists it (sending a mode makes the device switch to it).</summary>
    public static byte[] Mode(int index, RgbMode m, uint version)
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(0u); w.Write((uint)index); Str(w, m.Name); w.Write(m.Value); w.Write((uint)m.Flags); w.Write(m.SpeedMin); w.Write(m.SpeedMax);
        if (version >= 3) { w.Write(m.BrightnessMin); w.Write(m.BrightnessMax); }
        w.Write(m.ColorsMin); w.Write(m.ColorsMax); w.Write(m.Speed); if (version >= 3) w.Write(m.Brightness);
        w.Write(m.Direction); w.Write(m.ColorMode); w.Write((ushort)m.Colors.Count);
        foreach (var c in m.Colors) { w.Write(c.R); w.Write(c.G); w.Write(c.B); w.Write((byte)0); }
        w.Flush(); var b = s.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)b.Length); return b;
    }

    /// <summary>The "resize zone" payload: the zone's index and its new number of LEDs.</summary>
    public static byte[] Resize(int zone, int leds)
    {
        var b = new byte[8]; BinaryPrimitives.WriteInt32LittleEndian(b, zone); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), leds); return b;
    }

    private static void Str(BinaryWriter w, string s) { var bytes = Encoding.UTF8.GetBytes(s); w.Write((ushort)(bytes.Length + 1)); w.Write(bytes); w.Write((byte)0); }

    /// <summary>Reads a device's description (the answer to "controller data"). Throws <see cref="FormatException"/> on a truncated or odd one.</summary>
    public static RgbDevice Device(int index, ReadOnlySpan<byte> data, uint version)
    {
        var r = new Reader(data.ToArray()); r.U32();   // its own size
        var kind = Kind((int)r.U32()); string name = r.Str(), vendor = version >= 1 ? r.Str() : "", description = r.Str(); r.Str(); r.Str(); string location = r.Str();
        int modeCount = r.U16(); int active = (int)r.U32(); var modes = new List<RgbMode>(modeCount);
        for (int i = 0; i < modeCount; i++) modes.Add(ReadMode(r, version));
        int zoneCount = r.U16(); var zones = new List<RgbZone>(zoneCount);
        for (int i = 0; i < zoneCount; i++) { string zoneName = r.Str(); uint type = r.U32(), min = r.U32(), max = r.U32(), count = r.U32(); r.Skip(r.U16()); zones.Add(new RgbZone(i, zoneName, type, min, max, count)); }
        int leds = r.U16(); for (int i = 0; i < leds; i++) { r.Str(); r.U32(); }
        int colorCount = r.U16(); var colors = new List<RgbColor>(colorCount);
        for (int i = 0; i < colorCount; i++) colors.Add(r.Color());
        return new RgbDevice(index, kind, name, vendor, description, location, modes, active, leds, colors, zones);
    }

    private static RgbMode ReadMode(Reader r, uint version)
    {
        string name = r.Str(); uint value = r.U32(), flags = r.U32(), speedMin = r.U32(), speedMax = r.U32(); uint bMin = 0, bMax = 0, bright = 0;
        if (version >= 3) { bMin = r.U32(); bMax = r.U32(); }
        uint cMin = r.U32(), cMax = r.U32(), speed = r.U32(); if (version >= 3) bright = r.U32();
        uint direction = r.U32(), colorMode = r.U32(); int n = r.U16(); var colors = new List<RgbColor>(n);
        for (int i = 0; i < n; i++) colors.Add(r.Color());
        return new RgbMode(name, value, (RgbModeFlags)flags, speedMin, speedMax, bMin, bMax, cMin, cMax, speed, bright, direction, colorMode, colors);
    }

    internal static RgbKind Kind(int type) => type switch
    {
        0 => RgbKind.Motherboard, 1 => RgbKind.Memory, 2 => RgbKind.Graphics, 3 => RgbKind.Cooler, 4 => RgbKind.Strip, 5 or 18 => RgbKind.Keyboard, 6 or 7 => RgbKind.Mouse,
        14 => RgbKind.Storage, 15 => RgbKind.Case, _ => RgbKind.Other
    };

    private sealed class Reader(byte[] d)
    {
        private int _p;
        private ReadOnlySpan<byte> Take(int n) { if (n < 0 || _p + n > d.Length) throw new FormatException("The device description ends early."); var s = d.AsSpan(_p, n); _p += n; return s; }
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public int U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public void Skip(int n) => Take(n);
        public RgbColor Color() { var c = Take(4); return new RgbColor(c[0], c[1], c[2]); }
        public string Str() { int n = U16(); var s = Take(n); return n == 0 ? "" : Encoding.UTF8.GetString(s[..(s[^1] == 0 ? n - 1 : n)]); }
    }
}
