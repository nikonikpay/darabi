using System.Runtime.InteropServices; using Mazesta.Core.Tray; using Mazesta.Persistence;
namespace Mazesta.Tray.FileSearch;

/// <summary>
/// The quick file search in the tray: its shortcut (Ctrl+Alt+F by default), the app's two signals (<see cref="FileSearchSignals"/>) and,
/// while it is on, the drives' index and the window. Turned off in the settings, the index is dropped and its memory returned; turned on,
/// the drives are read again. Waiting for the signals costs nothing: Windows calls back when one is set.
/// </summary>
internal sealed class FileSearchHost : IDisposable
{
    private const int WmHotkey = 0x0312, ModNoRepeat = 0x4000, Id = 0x4D70;
    private readonly string _configFile; private readonly Control _ui = new(); private readonly KeyWindow _keys;
    private readonly EventWaitHandle _show = new(false, EventResetMode.AutoReset, FileSearchSignals.Show), _settings = new(false, EventResetMode.AutoReset, FileSearchSignals.Settings);
    private readonly RegisteredWaitHandle _onShow, _onSettings; private readonly System.Windows.Forms.Timer _retry = new() { Interval = 10_000 }, _later = new();
    private DriveSet? _drives; private SearchForm? _form; private Hotkey? _key; private bool _held; private readonly string? _log;

    public bool On => _drives is not null;
    /// <summary>The shortcut's text for the menu ("Ctrl+Alt+F"), or null while the search is off.</summary>
    public string? Shortcut => On ? _key?.ToString() : null;

    /// <param name="startAfter">The drives are read this long after the tray starts (not during sign-in), or at once when the window is asked for.</param>
    public FileSearchHost(string configFile, TimeSpan startAfter, string? log)
    {
        _configFile = configFile; _log = log; _ui.CreateControl(); _keys = new KeyWindow(() => Open());
        _retry.Tick += (_, _) => Register();
        _later.Interval = (int)Math.Max(1000, startAfter.TotalMilliseconds); _later.Tick += (_, _) => { _later.Stop(); _drives?.Start(); };
        _onShow = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => Ui(() => Open()), null, Timeout.Infinite, false);
        _onSettings = ThreadPool.RegisterWaitForSingleObject(_settings, (_, _) => Ui(Apply), null, Timeout.Infinite, false);
        Apply();
    }

    /// <summary>The window, opened from the tray menu, the shortcut or the app; with the search off it is turned on first.</summary>
    public void Open()
    {
        if (_drives is null) return;
        _later.Stop(); _drives.Start();
        _form ??= new SearchForm(_drives, _key?.ToString() ?? "");
        _form.Summon();
    }

    /// <summary>The switch and the shortcut read again from the settings file.</summary>
    private void Apply()
    {
        var (on, key) = FileSearchFile.Read(_configFile);
        if (on && _drives is null) { _drives = new DriveSet(_log); _later.Start(); }
        else if (!on && _drives is not null)
        {
            _form?.Dispose(); _form = null; _drives.Dispose(); _drives = null;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true); Memory.Release();   // the names' memory returned at once
            var info = GC.GetGCMemoryInfo(); using var me = System.Diagnostics.Process.GetCurrentProcess();
            if (_log is not null) try { File.AppendAllText(_log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  turned off: managed heap {info.HeapSizeBytes >> 20} MB, committed {info.TotalCommittedBytes >> 20} MB, process private {me.PrivateMemorySize64 >> 20} MB{Environment.NewLine}"); } catch (IOException) { }
        }
        if (!on) { Unregister(); _key = null; return; }
        if (_key != key) { Unregister(); _key = key; if (_form is not null) { _form.Dispose(); _form = null; } }   // the window shows the shortcut
        Register();
    }

    private void Register()
    {
        if (_key is not { } k || _held) { _retry.Enabled = false; return; }
        _held = RegisterHotKey(_keys.Handle, Id, k.Modifiers | ModNoRepeat, k.VirtualKey);
        _retry.Enabled = !_held;   // another program holds it: asked again every 10 s, as the overlay's keys are
    }

    private void Unregister() { if (_held) UnregisterHotKey(_keys.Handle, Id); _held = false; _retry.Enabled = false; }

    private void Ui(Action a) { try { _ui.BeginInvoke(a); } catch (InvalidOperationException) { } }

    private sealed class KeyWindow : NativeWindow
    {
        private readonly Action _pressed;
        public KeyWindow(Action pressed) { _pressed = pressed; CreateHandle(new CreateParams { Caption = "Mazesta file search hotkey", Parent = -3 /* HWND_MESSAGE */ }); }
        protected override void WndProc(ref Message m) { if (m.Msg == WmHotkey) { _pressed(); return; } base.WndProc(ref m); }
    }

    public void Dispose()
    {
        _onShow.Unregister(null); _onSettings.Unregister(null); _show.Dispose(); _settings.Dispose();
        Unregister(); _retry.Dispose(); _later.Dispose(); _keys.DestroyHandle(); _form?.Dispose(); _drives?.Dispose(); _ui.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, int modifiers, int vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint hWnd, int id);
}
