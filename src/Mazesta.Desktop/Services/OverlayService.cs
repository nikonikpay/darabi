using System.Runtime.InteropServices; using System.Windows.Forms;
using Mazesta.Core.Overlay; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Desktop.Views; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Desktop.Services;

/// <summary>
/// Shows and hides the on-screen overlay and remembers it (and its corner) in the settings. Ctrl+Shift+O toggles it from anywhere, also while a
/// game has the keyboard. The overlay window and its view model are made on first use and kept; hidden, the view model ignores the monitor's
/// snapshots, so an unused overlay costs nothing. It is not owned by the main window, so it stays up while the app is minimised - and, when the
/// tray runs, after the main window is closed (the app then lives on for the overlay alone; see Mazesta.App's App).
/// What it shows (items, charts, preset), its opacity and size come from the settings; changing them rebuilds the view model in place.
/// </summary>
public sealed class OverlayService(PollingEngine engine, AppConfig config, IFrameRateSource? frames = null, IPingSource? ping = null, Core.Inventory.MemoryFactsSource? memory = null) : IDisposable
{
    /// <summary>The overlay's shortcuts as set (a setting that is not usable, or two the same, gives the defaults).</summary>
    public Core.Tray.OverlayHotkeys Keys => new Core.Tray.OverlayHotkeys(config.OverlayKeyToggle, config.OverlayKeyMove, config.OverlayKeyLayout, config.OverlayKeyPreset).Valid();
    public string HotkeyText => Keys.Toggle;
    public string MoveHotkeyText => Keys.Move;
    // What the RAM runs at: read once, off the UI thread (it asks the modules), and shown once it is there.
    private Task<Core.Inventory.MemoryFacts?>? _facts;
    /// <summary>The text of a fixed fact the overlay can show (the RAM's speed), or null while it is not known.</summary>
    public string? StaticText(string id) => id == "ram.speed" && Facts()?.SpeedMts is { } mts ? $"{mts} MT/s" : null;
    private Core.Inventory.MemoryFacts? Facts() { _facts ??= Task.Run(() => memory?.Invoke()); return _facts.IsCompletedSuccessfully ? _facts.Result : null; }
    public static readonly string[] Corners = ["TopLeft", "TopRight", "BottomLeft", "BottomRight"];
    // Alt+O shows or hides it, Alt+M puts it in the mode where the mouse moves and resizes it; Ctrl+Shift+O, the shortcut of the earlier releases, still works.
    private const int HotkeyId = 0x4D5A, AltOId = 0x4D5B, AltMId = 0x4D5C, LayoutId = 0x4D5D, PresetId = 0x4D5E, WmHotkey = 0x0312, ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000, VkO = 0x4F, VkM = 0x4D;
    private OverlayWindow? _window; private OverlayViewModel? _vm; private HotkeyWindow? _source;

    public bool IsVisible => _window?.Visible == true;
    public event Action<bool>? VisibilityChanged;
    /// <summary>The overlay was moved or resized with the mouse and the mode has ended: the place is in the settings, to be saved.</summary>
    public event Action? PlaceChanged;
    public bool Editing => _window?.Editing == true;
    /// <summary>Raised after each poll the overlay showed, with what it showed (the web page mirrors it in its preview).</summary>
    public event Action<OverlayViewModel>? Updated;
    public IFrameRateSource? FrameSource => frames;
    public IPingSource? PingSource => ping;
    public OverlayViewModel? Current => _vm;

    public IReadOnlyList<OverlayChoice> Items => config.OverlayItems is { Count: > 0 } items ? WithSessionStats(items) : OverlayCatalog.Presets[OverlayCatalog.DefaultPreset];

    /// <summary>The game set gained the session's average, lowest and highest frame rate after it was first saved: a saved game set without them
    /// gets them after the 1 % low, once, so the owner does not have to pick the preset again. A custom set is left as it was chosen.</summary>
    private List<OverlayChoice> WithSessionStats(List<OverlayChoice> items)
    {
        string[] stats = ["fps.avg", "fps.min", "fps.max"];
        int fps = items.FindIndex(c => c.Id == "fps");
        if (config.OverlayPreset != "game" || fps < 0) return items;
        if (!items.Any(c => stats.Contains(c.Id)))
        {
            int low = items.FindIndex(c => c.Id == "low1");
            items.InsertRange((low >= 0 ? low : fps) + 1, stats.Select(id => new OverlayChoice(id, false)));
        }
        // The same for the link (ping, loss, jitter, traffic), which the game set gained later still: added at its end, once.
        string[] link = ["net.ping", "net.loss", "net.jitter", "net.down", "net.up"];
        if (!items.Any(c => link.Contains(c.Id))) items.AddRange(link.Select(id => new OverlayChoice(id, false)));
        // And the 0.1 % low, the processor's clocks (all cores, P-cores, E-cores) and power, and the graphics card's hot spot.
        void Add(string id, params string[] after)
        {
            int at = -1; foreach (var a in after) { at = items.FindIndex(c => c.Id == a); if (at >= 0) break; }
            items.Insert(at >= 0 ? at + 1 : items.Count, new OverlayChoice(id, false));
        }
        if (!items.Any(c => c.Id == "low01")) Add("low01", "low1", "fps");
        if (!items.Any(c => c.Id == "gpu.hotspot")) Add("gpu.hotspot", "gpu.temp");
        foreach (var id in new[] { "cpu.clock", "cpu.pclock", "cpu.eclock", "cpu.power" })
            if (!items.Any(c => c.Id == id)) Add(id, id == "cpu.clock" ? "cpu.load" : id == "cpu.pclock" ? "cpu.clock" : id == "cpu.eclock" ? "cpu.pclock" : "cpu.eclock", "cpu.temp");
        return items;
    }

    /// <summary>New items (from a preset or by hand): kept in the settings and shown at once if the overlay is up.</summary>
    public void Configure(IReadOnlyList<OverlayChoice> items, string preset)
    {
        // The hand-picked set is kept when the overlay goes to another one, so a shortcut (or a preset button) never throws it away.
        if (config.OverlayPreset == "custom" && preset != "custom") config.OverlayCustomItems = [.. Items];
        config.OverlayItems = [.. items]; config.OverlayPreset = preset;
        Rebuild();
    }

    public void SetAppearance(double opacity, double scale)
    {
        config.OverlayOpacity = Math.Clamp(opacity, 0.5, 1); config.OverlayScale = Math.Clamp(scale, 0.7, 1.6);
        Rebuild();
    }

    public void SetBare(bool bare) { config.OverlayBare = bare; Rebuild(); }

    /// <summary>The overlay's own pace: while it is shown the sensors are read every <paramref name="seconds"/> (0: at the app's own interval);
    /// hidden, the monitor goes back to the interval of the Settings page. Only the monitor's allowed intervals are taken.</summary>
    public void SetRefresh(double seconds) { config.OverlayRefreshChosen = true; config.OverlayRefresh = MonitoringOptions.OverlayRefreshSeconds.Contains(seconds) ? seconds : 0; Pace(config.OverlayVisible); }
    private void Pace(bool shown)
    {
        var interval = TimeSpan.FromSeconds(shown && config.OverlayRefresh > 0 ? config.OverlayRefresh : config.FastIntervalSeconds);
        if (MonitoringOptions.IsAllowed(interval) && engine.FastInterval != interval) engine.SetFastInterval(interval);
    }

    /// <summary>The overlay's own language: English titles and a left-to-right layout whatever the app's language is.</summary>
    public void SetEnglish(bool english) { config.OverlayEnglish = english; Rebuild(); }

    /// <summary>The current items kept under a name (a saved set of the user's own); the preset becomes that set. False for a name that is empty, too long or a ready-made one.</summary>
    public bool SavePreset(string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 40 || OverlayCatalog.Presets.ContainsKey(name) || name.Equals("custom", StringComparison.OrdinalIgnoreCase)) return false;
        (config.OverlayUserPresets ??= [])[name] = [.. Items]; config.OverlayPreset = "user:" + name; return true;
    }

    public void DeletePreset(string name)
    {
        if (config.OverlayUserPresets?.Remove(name) != true) return;
        if (config.OverlayPreset == "user:" + name) config.OverlayPreset = "custom";
    }

    /// <summary>The overlay's next layout (list, columns, compact, line, and round again); the new one's name is returned.</summary>
    public string CycleLayout() { string next = Layouts[(Array.IndexOf(Layouts, config.OverlayLayout) + 1) % Layouts.Length]; SetLayout(next); return next; }

    /// <summary>The overlay's next set of items: the three ready-made ones, the user's own saved ones, then their hand-picked set when one is kept. Returns the preset's id.</summary>
    public string CyclePreset()
    {
        string next = OverlayCatalog.NextPreset(config.OverlayPreset, (config.OverlayUserPresets ?? []).Keys, config.OverlayCustomItems is { Count: > 0 } || config.OverlayPreset == "custom");
        var items = next == "custom" ? config.OverlayCustomItems : next.StartsWith("user:", StringComparison.Ordinal) ? config.OverlayUserPresets?.GetValueOrDefault(next[5..]) : OverlayCatalog.Presets.GetValueOrDefault(next);
        if (items is { Count: > 0 }) Configure(items, next);
        return next;
    }

    public static readonly string[] Layouts = OverlayViewModel.Layouts;
    public void SetLayout(string layout) { if (!Layouts.Contains(layout)) return; config.OverlayLayout = layout; Rebuild(); }

    private OverlayViewModel Create()
    {
        var vm = new OverlayViewModel(engine, UiDispatcher.Post, Items, frames, config.OverlayOpacity, config.OverlayScale, config.OverlayLayout, ping, config.OverlayEnglish, Facts) { Bare = config.OverlayBare };
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
            if (_window is null)
            {
                _window = new OverlayWindow(_vm, Loc.IsRtl);
                _window.Placed += (x, y) => { config.OverlayX = x; config.OverlayY = y; };
                _window.Resized += scale => { SetAppearance(config.OverlayOpacity, scale); PlaceChanged?.Invoke(); };
            }
            _window.SetPlace(config.OverlayX, config.OverlayY);
            _vm.SetActive(true); _window.SetCorner(Corners.Contains(config.OverlayCorner) ? config.OverlayCorner : Corners[0]); _window.Show();
        }
        else { _window?.SetEditing(false); _vm?.SetActive(false); _window?.Hide(); }
        if (config.OverlayRefresh > 0) Pace(visible);
        config.OverlayVisible = visible;
        VisibilityChanged?.Invoke(visible);
    }

    public void SetCorner(string corner) { if (!Corners.Contains(corner)) return; config.OverlayCorner = corner; config.OverlayX = config.OverlayY = null; _window?.SetPlace(null, null); _window?.SetCorner(corner); }

    /// <summary>Alt+M: shows the overlay if it is hidden and puts it in the mode where the mouse moves and resizes it; again, out of it, keeping the place.</summary>
    public void ToggleMove()
    {
        if (!IsVisible) { SetVisible(true); if (!IsVisible) return; }
        bool on = _window is { Editing: false };
        _window!.SetEditing(on);
        if (!on) PlaceChanged?.Invoke();
    }

    /// <summary>Registers the global shortcuts (the settings' four, and Ctrl+Shift+O of the earliest releases) on a hidden message-only window of its own, so they work with or without
    /// the main window. A key another program already holds is not registered (the overlay still works from its button and the tray, so the caller only logs it); false when the
    /// show-or-hide key could not be had.</summary>
    public bool RegisterHotkey()
    {
        _source ??= new HotkeyWindow(id => { switch (id) { case AltMId: ToggleMove(); break; case LayoutId: CycleLayout(); Saved?.Invoke(); break; case PresetId: CyclePreset(); Saved?.Invoke(); break; default: Toggle(); break; } });
        foreach (int id in new[] { HotkeyId, AltOId, AltMId, LayoutId, PresetId }) UnregisterHotKey(_source.Handle, id);
        var k = Keys.Parsed();
        bool old = RegisterHotKey(_source.Handle, HotkeyId, ModControl | ModShift | ModNoRepeat, VkO);
        RegisterHotKey(_source.Handle, AltMId, k[1].Modifiers | ModNoRepeat, k[1].VirtualKey);   // (when the tray holds a shortcut, it signals the app: see OverlaySignals)
        RegisterHotKey(_source.Handle, LayoutId, k[2].Modifiers | ModNoRepeat, k[2].VirtualKey); RegisterHotKey(_source.Handle, PresetId, k[3].Modifiers | ModNoRepeat, k[3].VirtualKey);
        return RegisterHotKey(_source.Handle, AltOId, k[0].Modifiers | ModNoRepeat, k[0].VirtualKey) || old;
    }

    /// <summary>The layout or the set changed by a shortcut: the settings are to be saved (the page shows the change too).</summary>
    public event Action? Saved;

    /// <summary>One shortcut changed on the settings page: false (and nothing changed) when the text is not a usable shortcut or another of the four already has it.</summary>
    public bool SetKey(string which, string text)
    {
        if (Core.Tray.Hotkey.Normalize(text) is not { } key) return false;
        var now = Keys;
        var next = which switch { "toggle" => now with { Toggle = key }, "move" => now with { Move = key }, "layout" => now with { Layout = key }, "preset" => now with { Preset = key }, _ => null };
        if (next is null || next.Valid() != next) return false;
        (config.OverlayKeyToggle, config.OverlayKeyMove, config.OverlayKeyLayout, config.OverlayKeyPreset) = (next.Toggle, next.Move, next.Layout, next.Preset);
        if (_source is not null) RegisterHotkey();
        return true;
    }

    /// <summary>A message-only window (no screen presence) that receives the shortcuts.</summary>
    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly Action<int> _pressed;
        public HotkeyWindow(Action<int> pressed) { _pressed = pressed; CreateHandle(new CreateParams { Caption = "Mazesta overlay hotkey", Parent = -3 /* HWND_MESSAGE */ }); }
        protected override void WndProc(ref Message m) { if (m.Msg == WmHotkey && (int)m.WParam is HotkeyId or AltOId or AltMId or LayoutId or PresetId) { _pressed((int)m.WParam); return; } base.WndProc(ref m); }
    }

    public void Dispose()
    {
        if (_source is not null) { foreach (int id in new[] { HotkeyId, AltOId, AltMId, LayoutId, PresetId }) UnregisterHotKey(_source.Handle, id); _source.DestroyHandle(); }
        _window?.Close(); _window?.Dispose(); _vm?.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, int modifiers, int vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint hWnd, int id);
}
