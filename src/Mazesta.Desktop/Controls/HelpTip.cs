using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Mazesta.Desktop.Controls;

public sealed class HelpTip : Button
{
    public static readonly DependencyProperty HelpKeyProperty = DependencyProperty.Register(nameof(HelpKey), typeof(string), typeof(HelpTip), new PropertyMetadata(""));
    public string HelpKey { get => (string)GetValue(HelpKeyProperty); set => SetValue(HelpKeyProperty, value); }
    private readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };

    public HelpTip()
    {
        Content = "?"; Width = 18; Height = 18; Padding = new Thickness(0); Margin = new Thickness(4, 0, 4, 0); Focusable = true; ToolTip = "راهنما";
        SetResourceReference(StyleProperty, "HelpTipStyle");
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 320, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Right, Margin = new Thickness(12) };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "App.Font"); text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        var border = new Border { Child = text, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt"); border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        _popup.Child = border; _popup.PlacementTarget = this;
        Click += (_, _) => { text.Text = Localization.HelpText.Get(HelpKey); _popup.IsOpen = !_popup.IsOpen; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { _popup.IsOpen = false; e.Handled = true; } };
    }
}
