using System.Runtime.InteropServices;
namespace Mazesta.Tray;

/// <summary>
/// The overlay's shortcuts while only the tray runs: Alt+O shows or hides it, Alt+M lets the mouse move and resize it. The app registers the same keys when it runs, and
/// whichever process asks first holds them (Windows gives a shortcut to one); the tray's presses reach the app through <see cref="Mazesta.Core.Tray.OverlaySignals"/>, so
/// either way the keys do the same. A key another program holds is simply not registered.
/// </summary>
internal sealed class OverlayKeys : IDisposable
{
    private const int WmHotkey = 0x0312, ModAlt = 0x1, ModNoRepeat = 0x4000, IdToggle = 0x4D61, IdMove = 0x4D62;
    private readonly Window _window;

    public OverlayKeys(Action toggle, Action move)
    {
        _window = new Window(id => { if (id == IdToggle) toggle(); else move(); });
        RegisterHotKey(_window.Handle, IdToggle, ModAlt | ModNoRepeat, 0x4F /* O */);
        RegisterHotKey(_window.Handle, IdMove, ModAlt | ModNoRepeat, 0x4D /* M */);
    }

    private sealed class Window : NativeWindow
    {
        private readonly Action<int> _pressed;
        public Window(Action<int> pressed) { _pressed = pressed; CreateHandle(new CreateParams { Caption = "Mazesta tray hotkey", Parent = -3 /* HWND_MESSAGE */ }); }
        protected override void WndProc(ref Message m) { if (m.Msg == WmHotkey) { _pressed((int)m.WParam); return; } base.WndProc(ref m); }
    }

    public void Dispose() { UnregisterHotKey(_window.Handle, IdToggle); UnregisterHotKey(_window.Handle, IdMove); _window.DestroyHandle(); }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, int modifiers, int vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint hWnd, int id);
}
