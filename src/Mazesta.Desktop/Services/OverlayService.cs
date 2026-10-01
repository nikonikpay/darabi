using System.Runtime.InteropServices; using System.Windows.Forms;
using Mazesta.Core.Overlay; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Desktop.Views; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Desktop.Services;

/// <summary>
/// Shows and hides the on-screen overlay and remembers it (and its corner) in the settings. Ctrl+Shift+O toggles it from anywhere, also while a
/// game has the keyboard. The overlay window and its view model are made on first use and kept; hidden, the view model ignores the monitor's
/// snapshots, so an unused overlay costs nothing. It is not owned by the main window, so it stays up while the app is minimised - and, when the
/// tray runs, after the main window is closed (the app then lives on for the overlay alone; see Mazesta.Web's App).
/// What it shows (items, charts, preset), its opacity and size come from the settings; changing them rebuilds the view model in place.
/// </summary>
public sealed class OverlayService(PollingEngine engine, AppConfig config, IFrameRateSource? frames = null) : IDisposable
{
    public const string HotkeyText = "Ctrl+Shift+O";
    public static readonly string[] Corners = ["TopLeft", "TopRight", "BottomLeft", "BottomRight"];
    private const int HotkeyId = 0x4D5A, WmHotkey = 0x0312, ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000, VkO = 0x4F;
    private OverlayWindow? _window; private OverlayViewModel? _vm; private HotkeyWindow? _source;

    public bool IsVisible => _window?.Visible == true;
    public event Action<bool>? VisibilityChanged;
    /// <summary>Raised after each poll the overlay showed, with what it showed (the web page mirrors it in its preview).</summary>
    public event Action<OverlayViewModel>? Updated;
    public IFrameRateSource? FrameSource => frames;
    public OverlayViewModel? Current => _vm;

    public IReadOnlyList<OverlayChoice> Items => config.OverlayItems is { Count: > 0 } items ? WithSessionStats(items) : OverlayCatalog.Presets[OverlayCatalog.DefaultPreset];

    /// <summary>The game set gained the session's average, lowest and highest frame rate after it was first saved: a saved game set without them
    /// gets them after the 1 % low, once, so the owner does not have to pick the preset again. A custom set is left as it was chosen.</summary>
    private List<OverlayChoice> WithSessionStats(List<OverlayChoice> items)
    {
        string[] stats = ["fps.avg", "fps.min", "fps.max"];
        int fps = items.FindIndex(c => c.Id == "fps");
        if (config.OverlayPreset != "game" || fps < 0 || items.Any(c => stats.Contains(c.Id))) return items;
        int low = items.FindIndex(c => c.Id == "low1");
        items.InsertRange((low >= 0 ? low : fps) + 1, stats.Select(id => new OverlayChoice(id, false)));
        return items;
    }

    /// <summary>New items (from a preset or by hand): kept in the settings and shown at once if the overlay is up.</summary>
    public void Configure(IReadOnlyList<OverlayChoice> items, string preset)
    {
        config.OverlayItems = [.. items]; config.OverlayPreset = preset;
        Rebuild();
    }

    public void SetAppearance(double opacity, double scale)
    {
        config.OverlayOpacity = Math.Clamp(opacity, 0.5, 1); config.OverlayScale = Math.Clamp(scale, 0.7, 1.5);
        Rebuild();
    }

    public static readonly string[] Layouts = OverlayViewModel.Layouts;
    public void SetLayout(string layout) { if (!Layouts.Contains(layout)) return; config.OverlayLayout = layout; Rebuild(); }

    private OverlayViewModel Create()
    {
        var vm = new OverlayViewModel(engine, UiDispatcher.Post, Items, frames, config.OverlayOpacity, config.OverlayScale, config.OverlayLayout);
        vm.Updated += () => Updated?.Invoke(vm);
        return vm;
    }

    private void Rebuild()
    {
        if (_vm is null) return;
        bool visible = IsVisible;
        _vm.SetActive(false); _vm.Dispose();
        _vm = Create();
        _window?.SetModel(_vm);
        if (visible) _vm.SetActive(true);
    }

    public void Toggle() => SetVisible(!IsVisible);

    /// <summary>The kinds of hardware the overlay reads while it is on screen (none while hidden): the monitor keeps polling these during a benchmark.</summary>
    public IReadOnlySet<Core.Hardware.HardwareKind> ShownKinds()
    {
        if (!IsVisible || _vm is null) return new HashSet<Core.Hardware.HardwareKind>();
        var ids = _vm.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Sensors).Select(s => s.Hardware).ToHashSet();
        return engine.Hardware.Where(n => ids.Contains(n.Id)).Select(n => n.Kind).ToHashSet();
    }

    public void SetVisible(bool visible)
    {
        if (visible)
        {
            if (engine.Hardware.Count == 0) return;   // the hardware scan has not finished: there is nothing to show yet
            _vm ??= Create();
            _window ??= new OverlayWindow(_vm, Loc.IsRtl);
            _vm.SetActive(true); _window.SetCorner(Corners.Contains(config.OverlayCorner) ? config.OverlayCorner : Corners[0]); _window.Show();
        }
        else { _vm?.SetActive(false); _window?.Hide(); }
        config.OverlayVisible = visible;
        VisibilityChanged?.Invoke(visible);
    }

    public void SetCorner(string corner) { if (!Corners.Contains(corner)) return; config.OverlayCorner = corner; _window?.SetCorner(corner); }

    /// <summary>Registers the global shortcut on a hidden message-only window of its own, so it works with or without the main window. It can
    /// fail when another program already holds it; the overlay still works from its button and the tray, so that is only logged by the caller.</summary>
    public bool RegisterHotkey()
    {
        if (_source is not null) return true;
        _source = new HotkeyWindow(Toggle);
        return RegisterHotKey(_source.Handle, HotkeyId, ModControl | ModShift | ModNoRepeat, VkO);
    }

    /// <summary>A message-only window (no screen presence) that receives the shortcut.</summary>
    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly Action _pressed;
        public HotkeyWindow(Action pressed) { _pressed = pressed; CreateHandle(new CreateParams { Caption = "Mazesta overlay hotkey", Parent = -3 /* HWND_MESSAGE */ }); }
        protected override void WndProc(ref Message m) { if (m.Msg == WmHotkey && m.WParam == HotkeyId) { _pressed(); return; } base.WndProc(ref m); }
    }

    public void Dispose()
    {
        if (_source is not null) { UnregisterHotKey(_source.Handle, HotkeyId); _source.DestroyHandle(); }
        _window?.Close(); _window?.Dispose(); _vm?.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, int modifiers, int vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint hWnd, int id);
}
