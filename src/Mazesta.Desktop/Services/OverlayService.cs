using System.Runtime.InteropServices; using System.Windows; using System.Windows.Interop;
using Mazesta.Core.Overlay; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Desktop.Views; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Desktop.Services;

/// <summary>
/// Shows and hides the on-screen overlay and remembers it (and its corner) in the settings. Ctrl+Shift+O toggles it from anywhere, also while a
/// game has the keyboard. The overlay window and its view model are made on first use and kept; hidden, the view model ignores the monitor's
/// snapshots, so an unused overlay costs nothing. It is not owned by the main window, so it stays up while the app is minimised.
/// What it shows (items, charts, preset), its opacity and size come from the settings; changing them rebuilds the view model in place.
/// </summary>
public sealed class OverlayService(PollingEngine engine, AppConfig config, IFrameRateSource? frames = null) : IDisposable
{
    public const string HotkeyText = "Ctrl+Shift+O";
    public static readonly string[] Corners = ["TopLeft", "TopRight", "BottomLeft", "BottomRight"];
    private const int HotkeyId = 0x4D5A, WmHotkey = 0x0312, ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000, VkO = 0x4F;
    private OverlayWindow? _window; private OverlayViewModel? _vm; private HwndSource? _source; private nint _hwnd;

    public bool IsVisible => _window?.IsVisible == true;
    public event Action<bool>? VisibilityChanged;
    /// <summary>Raised after each poll the overlay showed, with what it showed (the web page mirrors it in its preview).</summary>
    public event Action<OverlayViewModel>? Updated;
    public IFrameRateSource? FrameSource => frames;
    public OverlayViewModel? Current => _vm;

    public IReadOnlyList<OverlayChoice> Items => config.OverlayItems is { Count: > 0 } items ? items : OverlayCatalog.Presets[OverlayCatalog.DefaultPreset];

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

    public static readonly string[] Layouts = ["list", "columns"];
    public void SetLayout(string layout) { if (!Layouts.Contains(layout)) return; config.OverlayLayout = layout; Rebuild(); }

    private OverlayViewModel Create()
    {
        var vm = new OverlayViewModel(engine, a => Application.Current.Dispatcher.BeginInvoke(a), Items, frames, config.OverlayOpacity, config.OverlayScale, config.OverlayLayout == "columns");
        vm.Updated += () => Updated?.Invoke(vm);
        return vm;
    }

    private void Rebuild()
    {
        if (_vm is null) return;
        bool visible = IsVisible;
        _vm.SetActive(false); _vm.Dispose();
        _vm = Create();
        if (_window is not null) _window.DataContext = _vm;
        if (visible) _vm.SetActive(true);
    }

    public void Toggle() => SetVisible(!IsVisible);

    public void SetVisible(bool visible)
    {
        if (visible)
        {
            if (engine.Hardware.Count == 0) return;   // the hardware scan has not finished: there is nothing to show yet
            _vm ??= Create();
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
