using System.Runtime.InteropServices; using Mazesta.Core.Tray; using Mazesta.Core.Windows;
namespace Mazesta.Tray.Colors;

/// <summary>
/// The screen colour picker in the tray, so it works with the app closed: Win+Shift+C (Ctrl+Alt+C when another program, PowerToys for one,
/// holds that), the tray menu, or the app's button (a named event Windows waits on). The pick's HEX is copied and shown in <see cref="ColorForm"/>
/// with its other codes and the last ones picked. Nothing runs between picks.
/// </summary>
internal sealed class ColorHost : IDisposable
{
    private const int WmHotkey = 0x0312, ModNoRepeat = 0x4000, Id = 0x4D43;
    private static readonly Hotkey[] Keys = [new(Hotkey.Win | Hotkey.Shift, 'C'), new(Hotkey.Ctrl | Hotkey.Alt, 'C')];
    private readonly KeyWindow _window; private readonly Control _ui = new(); private readonly string _historyFile;
    private readonly EventWaitHandle _signal = new(false, EventResetMode.AutoReset, FileSearchSignals.ColorPick); private readonly RegisteredWaitHandle _wait;
    private readonly System.Windows.Forms.Timer _retry = new() { Interval = 10_000 };
    private ColorForm? _form; private bool _picking;

    /// <summary>The shortcut held now ("Win+Shift+C"), or null while another program holds both.</summary>
    public string? Shortcut { get; private set; }

    public ColorHost(string historyFile)
    {
        _historyFile = historyFile; _ui.CreateControl(); _window = new KeyWindow(Pick);
        _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) => { try { _ui.BeginInvoke(Pick); } catch (InvalidOperationException) { } }, null, Timeout.Infinite, false);
        _retry.Tick += (_, _) => Register();
        Register();
    }

    private void Register()
    {
        foreach (var k in Keys)
            if (RegisterHotKey(_window.Handle, Id, k.Modifiers | ModNoRepeat, k.VirtualKey)) { Shortcut = k.ToString(); _retry.Enabled = false; return; }
        _retry.Enabled = true;   // both held elsewhere: asked again every 10 s, as the search's shortcut is
    }

    /// <summary>The picker over the whole screen; the colour picked is copied and shown with its codes.</summary>
    public void Pick()
    {
        if (_picking) return;
        _picking = true;
        try
        {
            _form?.Hide();
            if (ColorPicker.Pick() is not { } c) return;
            string hex = ColorText.Hex(c.R, c.G, c.B);
            try { Clipboard.SetText(hex); } catch (ExternalException) { /* another program holds the clipboard; the window still shows it */ }
            _form ??= new ColorForm(_historyFile, Pick);
            _form.Summon(c);
        }
        finally { _picking = false; }
    }

    private sealed class KeyWindow : NativeWindow
    {
        private readonly Action _pressed;
        public KeyWindow(Action pressed) { _pressed = pressed; CreateHandle(new CreateParams { Caption = "Mazesta colour picker hotkey", Parent = -3 /* HWND_MESSAGE */ }); }
        protected override void WndProc(ref Message m) { if (m.Msg == WmHotkey) { _pressed(); return; } base.WndProc(ref m); }
    }

    public void Dispose()
    {
        _wait.Unregister(null); _signal.Dispose(); _retry.Dispose();
        if (Shortcut is not null) UnregisterHotKey(_window.Handle, Id);
        _window.DestroyHandle(); _form?.Dispose(); _ui.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, int modifiers, int vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint hWnd, int id);
}
