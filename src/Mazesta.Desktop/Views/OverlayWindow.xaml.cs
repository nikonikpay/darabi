using System.Runtime.InteropServices; using System.Windows; using System.Windows.Interop;
namespace Mazesta.Desktop.Views;

/// <summary>The overlay's window: it never takes focus and the mouse goes through it to the game or program underneath (a transparent
/// tool window), and it sits in the chosen corner of the primary screen's work area.</summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20, WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
    private string _corner = "TopLeft";

    public OverlayWindow() { InitializeComponent(); SizeChanged += (_, _) => Place(); }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLongPtr(hwnd, GwlExStyle, GetWindowLongPtr(hwnd, GwlExStyle) | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    /// <summary>TopLeft, TopRight, BottomLeft or BottomRight of the primary screen, a little in from its edges.</summary>
    public void SetCorner(string corner) { _corner = corner; Place(); }

    private void Place()
    {
        const double margin = 14; var area = SystemParameters.WorkArea;
        Left = _corner.EndsWith("Right", StringComparison.Ordinal) ? area.Right - ActualWidth - margin : area.Left + margin;
        Top = _corner.StartsWith("Bottom", StringComparison.Ordinal) ? area.Bottom - ActualHeight - margin : area.Top + margin;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
