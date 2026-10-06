namespace Mazesta.App;

/// <summary>
/// Whether Windows' "Smooth edges of screen fonts" is off. The page's text is drawn by the browser engine, which follows that setting: with it off
/// (the "best performance" visual effects, some remote sessions) every glyph is drawn without anti-aliasing and looks rough, whatever the font. The app cannot
/// switch the setting itself (it is the user's), so it says so and where to turn it on.
/// </summary>
internal static class FontSmoothing
{
    private const uint GetFontSmoothing = 0x004A;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);

    /// <summary>True only when Windows answers that smoothing is off; an unanswered question is not "off".</summary>
    public static bool IsOff() { int on = 1; return SystemParametersInfo(GetFontSmoothing, 0, ref on, 0) && on == 0; }
}
