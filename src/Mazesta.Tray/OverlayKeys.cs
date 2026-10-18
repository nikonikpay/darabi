using System.Runtime.InteropServices;
namespace Mazesta.Tray;

/// <summary>
/// The overlay's shortcuts while only the tray runs (the settings' four: by default Alt+O shows or hides it, Alt+M lets the mouse move and resize it, Alt+U takes it to the next layout and Alt+P to the next set of items). The app registers the same keys when it runs, and
/// whichever process asks first holds them (Windows gives a shortcut to one); the tray's presses reach the app through <see cref="Mazesta.Core.Tray.OverlaySignals"/>, so
/// either way the keys do the same. A key held elsewhere (the app started before the tray holds them all) is asked for again every 10 s until it is
/// free, so closing the app does not leave the shortcuts dead.
/// </summary>
internal sealed class OverlayKeys : IDisposable
{
    private const int WmHotkey = 0x0312, ModNoRepeat = 0x4000, IdToggle = 0x4D61, IdMove = 0x4D62, IdLayout = 0x4D63, IdPreset = 0x4D64;
    private readonly Window _window; private readonly IReadOnlyList<Mazesta.Core.Tray.Hotkey> _keys; private readonly HashSet<int> _missing = [];
    private readonly System.Windows.Forms.Timer _retry = new() { Interval = 10_000 };
    private static readonly int[] Ids = [IdToggle, IdMove, IdLayout, IdPreset];

    public OverlayKeys(Mazesta.Core.Tray.OverlayHotkeys keys, Action toggle, Action move, Action layout, Action preset)
    {
        _window = new Window(id => { switch (id) { case IdToggle: toggle(); break; case IdMove: move(); break; case IdLayout: layout(); break; default: preset(); break; } });
        _keys = keys.Parsed();
        _missing.UnionWith(Ids); _retry.Tick += (_, _) => Register(); Register();
    }

    private void Register()
    {
        foreach (int id in _missing.ToList()) { var k = _keys[Array.IndexOf(Ids, id)]; if (RegisterHotKey(_window.Handle, id, k.Modifiers | ModNoRepeat, k.VirtualKey)) _missing.Remove(id); }
        _retry.Enabled = _missing.Count > 0;
    }

    private sealed class Window : NativeWindow
    {
        private readonly Action<int> _pressed;
        public Window(Action<int> pressed) { _pressed = pressed; CreateHandle(new CreateParams { Caption = "Mazesta tray hotkey", Parent = -3 /* HWND_MESSAGE */ }); }
        protected override void WndProc(ref Message m) { if (m.Msg == WmHotkey) { _pressed((int)m.WParam); return; } base.WndProc(ref m); }
    }

    public void Dispose() { _retry.Dispose(); foreach (int id in Ids) UnregisterHotKey(_window.Handle, id); _window.DestroyHandle(); }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, int modifiers, int vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint hWnd, int id);
}
