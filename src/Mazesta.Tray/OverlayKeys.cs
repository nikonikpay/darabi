using System.Runtime.InteropServices;
namespace Mazesta.Tray;

/// <summary>
/// The overlay's shortcuts while only the tray runs (the settings' four: by default Alt+O shows or hides it, Alt+M lets the mouse move and resize it, Alt+U takes it to the next layout and Alt+P to the next set of items). The app registers the same keys when it runs, and
/// whichever process asks first holds them (Windows gives a shortcut to one); the tray's presses reach the app through <see cref="Mazesta.Core.Tray.OverlaySignals"/>, so
/// either way the keys do the same. A key another program holds is simply not registered.
/// </summary>
internal sealed class OverlayKeys : IDisposable
{
    private const int WmHotkey = 0x0312, ModNoRepeat = 0x4000, IdToggle = 0x4D61, IdMove = 0x4D62, IdLayout = 0x4D63, IdPreset = 0x4D64;
    private readonly Window _window;

    public OverlayKeys(Mazesta.Core.Tray.OverlayHotkeys keys, Action toggle, Action move, Action layout, Action preset)
    {
        _window = new Window(id => { switch (id) { case IdToggle: toggle(); break; case IdMove: move(); break; case IdLayout: layout(); break; default: preset(); break; } });
        var k = keys.Parsed();
        int[] ids = [IdToggle, IdMove, IdLayout, IdPreset];
        for (int i = 0; i < 4; i++) RegisterHotKey(_window.Handle, ids[i], k[i].Modifiers | ModNoRepeat, k[i].VirtualKey);
    }

    private sealed class Window : NativeWindow
    {
        private readonly Action<int> _pressed;
        public Window(Action<int> pressed) { _pressed = pressed; CreateHandle(new CreateParams { Caption = "Mazesta tray hotkey", Parent = -3 /* HWND_MESSAGE */ }); }
        protected override void WndProc(ref Message m) { if (m.Msg == WmHotkey) { _pressed((int)m.WParam); return; } base.WndProc(ref m); }
    }

    public void Dispose() { foreach (int id in new[] { IdToggle, IdMove, IdLayout, IdPreset }) UnregisterHotKey(_window.Handle, id); _window.DestroyHandle(); }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, int modifiers, int vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint hWnd, int id);
}
