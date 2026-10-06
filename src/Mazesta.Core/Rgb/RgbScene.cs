namespace Mazesta.Core.Rgb;

/// <summary>One look: a colour ("#rrggbb"), an effect by its name (its number differs from device to device), a speed and a brightness in percent; <see cref="Off"/> is dark.</summary>
/// <param name="Leds">A colour for each LED of the device, in its order (set by clicking single LEDs); it stands in for <paramref name="Color"/>.</param>
public sealed record RgbLook(string? Color = null, string? Mode = null, int? Speed = null, int? Brightness = null, bool Off = false, IReadOnlyList<string>? Leds = null);

/// <summary>
/// What the user has set for the lights, kept in <c>Data/config/rgb-scene.json</c> so it comes back at every start: the app and the tray both put it back
/// (the lights forget it on every reboot and every sleep). <see cref="All"/> is for every device; a device with an entry of its own in <see cref="Devices"/>
/// keeps that instead. <see cref="Zones"/> holds the number of LEDs of the addressable headers, which no device can report by itself.
/// </summary>
public sealed class RgbScene
{
    /// <summary>The lights are the app's: the scene is put back at start-up, and the makers' lighting programs are kept stopped.</summary>
    public bool Enabled { get; set; }
    public bool StopMakers { get; set; } = true;
    /// <summary>Every light is switched off (from the tray's menu or the page) while the scene itself is kept, to be brought back by switching on.</summary>
    public bool Dark { get; set; }
    public RgbLook? All { get; set; }
    public Dictionary<string, RgbLook> Devices { get; set; } = [];
    public Dictionary<string, int> Zones { get; set; } = [];

    /// <summary>What an addressable header is given when nobody has said how long the strip on it is: a plausible length, so the lights work at once and
    /// no question is asked (OpenRGB's own window asks it, with 0 as the answer to give up).</summary>
    public const int DefaultLeds = 30;

    public static string DeviceKey(string vendor, string name, string location) => $"{vendor}|{name}|{location}";
    public static string ZoneKey(string vendor, string name, string location, string zone) => $"{vendor}|{name}|{location}|{zone}";
}
