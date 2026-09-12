using System.Windows;

namespace Mazesta.Desktop.Localization;

/// <summary>
/// The single place that turns RTL on for a window.
/// </summary>
/// <remarks>
/// <para>FlowDirection is applied to the window's root <em>content</em> element, never to the
/// Window itself: setting it on the Window flips the underlying HWND (WS_EX_LAYOUTRTL), which
/// mirrors the native title bar and, on this rendering path, the glyphs themselves (letters render
/// as literal mirror images). Applying it to the content keeps the mirroring inside WPF's visual
/// tree, which lays out RTL correctly and still renders text properly.</para>
/// <para>RightToLeft on a container also sets the bidi <em>paragraph direction</em> of every
/// TextBlock under it. A latin value+unit run such as "53.0 °C" then reorders to "C° 53.0", because
/// the space between the number and the symbol is a neutral that takes the paragraph's RTL
/// direction. Strings that are entirely latin - readings with units, clock speeds, "(24C/32T)",
/// phone numbers, file paths, versions, chart axis labels - therefore carry
/// <c>FlowDirection="LeftToRight"</c> (in XAML via the shared <c>Text.Value</c> /
/// <c>TextBox.Value</c> styles) so they keep an LTR paragraph inside the RTL page. Persian text
/// (state words, labels, the shop name) is left alone and inherits RTL.</para>
/// </remarks>
public static class Rtl
{
    /// <summary>Applies the current language's flow direction to a window's root content element.</summary>
    public static void Apply(FrameworkElement root)
        => root.FlowDirection = Loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
}
