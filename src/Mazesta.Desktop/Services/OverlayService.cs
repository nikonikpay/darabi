using System.Runtime.InteropServices; using System.Windows; using System.Windows.Interop;
using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Desktop.Views; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Desktop.Services;

/// <summary>
/// Shows and hides the on-screen overlay and remembers it (and its corner) in the settings. Ctrl+Shift+O toggles it from anywhere, also while a
/// game has the keyboard. The overlay window and its view model are made on first use and kept; hidden, the view model ignores the monitor's
/// snapshots, so an unused overlay costs nothing. It is not owned by the main window, so it stays up while the app is minimised.
/// </summary>
public sealed class OverlayService(PollingEngine engine, AppConfig config) : IDisposable
{
    public const string HotkeyText = "Ctrl+Shift+O";
    public static readonly string[] Corners = ["TopLeft", "TopRight", "BottomLeft", "BottomRight"];
    private const int HotkeyId = 0x4D5A, WmHotkey = 0x0312, ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000, VkO = 0x4F;
    private OverlayWindow? _window; private OverlayViewModel? _vm; private HwndSource? _source; private nint _hwnd;

    public bool IsVisible => _window?.IsVisible == true;
    public event Action<bool>? VisibilityChanged;

    public void Toggle() => SetVisible(!IsVisible);

    public void SetVisible(bool visible)
    {
        if (visible)
        {
            if (engine.Hardware.Count == 0) return;   // the hardware scan has not finished: there is nothing to show yet
            _vm ??= new OverlayViewModel(engine, a => Application.Current.Dispatcher.BeginInvoke(a));
            if (_window is null) { _window = new OverlayWindow { DataContext = _vm }; Rtl.Apply(_window.Root); }
            _vm.SetActive(true); _window.Show(); _window.SetCorner(Corners.Contains(config.OverlayCorner) ? config.OverlayCorner : Corners[0]);
        }
        else { _vm?.SetActive(false); _window?.Hide(); }
        config.OverlayVisible = visible;
        VisibilityChanged?.Invoke(visible);
    }

    public void SetCorner(string corner) { if (!Corners.Contains(corner)) return; config.OverlayCorner = corner; _window?.SetCorner(corner); }

    /// <summary>Registers the global shortcut on the main window. It can fail when another program already holds it; the overlay still works
    /// from its button, so that is only logged by the caller.</summary>
    public bool RegisterHotkey(Window owner)
    {
        _hwnd = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd); _source?.AddHook(WndProc);
        return RegisterHotKey(_hwnd, HotkeyId, ModControl | ModShift | ModNoRepeat, VkO);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam == HotkeyId) { Toggle(); handled = true; }
        return 0;
    }

    public void Dispose()
    {
        if (_hwnd != 0) UnregisterHotKey(_hwnd, HotkeyId);
        _source?.RemoveHook(WndProc);
        _window?.Close(); _vm?.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, int modifiers, int vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint hWnd, int id);
}
