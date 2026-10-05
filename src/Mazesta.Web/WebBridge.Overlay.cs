using Mazesta.Core.Hardware; using Mazesta.Core.Overlay; using Mazesta.Desktop.Localization; using Mazesta.Desktop.Services; using Mazesta.Monitoring;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    /// <summary>
    /// The overlay page: what the overlay can show on this machine (the catalog, each item with the sensors it reads here, so the page can show a
    /// live preview from the same snapshots), what it shows now, the presets, and its look. Changes are applied to the overlay at once and saved.
    /// </summary>
    private void RegisterOverlay()
    {
        var overlay = _sp.GetRequiredService<OverlayService>(); var engine = _sp.GetRequiredService<PollingEngine>();
        bool Include(HardwareNode n) => n.Kind != HardwareKind.Network || (!NetworkAdapterFilter.IsVirtualBinding(n.Name) && !n.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase));
        object State()
        {
            var chosen = overlay.Items;
            return new
            {
                visible = overlay.IsVisible, corner = _config.OverlayCorner, corners = OverlayService.Corners.Select(c => new { value = c, label = Loc.Get("Overlay_Corner_" + c) }),
                opacity = _config.OverlayOpacity, scale = _config.OverlayScale, preset = _config.OverlayPreset, hotkey = OverlayService.HotkeyText, layout = _config.OverlayLayout, bare = _config.OverlayBare, english = _config.OverlayEnglish,
                userPresets = (_config.OverlayUserPresets ?? []).OrderBy(p => p.Key).Select(p => new { id = "user:" + p.Key, name = p.Key, count = p.Value.Count }),
                frameProblem = overlay.FrameSource?.Problem, pingTarget = overlay.PingSource?.Target,
                presets = OverlayCatalog.Presets.Select(p => new { id = p.Key, count = p.Value.Count }),
                order = chosen.Select(c => c.Id),
                items = OverlayCatalog.All.Concat(OverlayCatalog.ForDrives(engine.Hardware)).Select(i =>
                {
                    var sensors = OverlayCatalog.Resolve(i, engine.Hardware, Include);
                    var choice = chosen.FirstOrDefault(c => c.Id == i.Id);
                    return new
                    {
                        id = i.Id, part = i.Part.ToString(), label = Loc.Get(i.LabelKey), labelEn = Loc.GetEnglish(i.LabelKey), frame = i.IsMeasured, available = i.IsMeasured || sensors.Count > 0,
                        on = choice is not null, chart = choice?.Chart ?? false, aggregate = i.Of is not null ? "Share" : i.Aggregate.ToString(), max = i.FixedMax, sensors = sensors.Select(s => s.Id.Value),
                        device = i.Device, deviceName = i.Device is null ? null : engine.Hardware.FirstOrDefault(n => n.Id.Value == i.Device)?.Name,
                    };
                }),
            };
        }
        void Save() { _store.Save(_config); PushSoon("overlayState", State); }

        Method("overlay.state", _ => State());
        Method("overlay.set", p =>
        {
            switch (Str(p, "field"))
            {
                case "visible": overlay.SetVisible(Bool(p, "value")); break;
                case "corner": overlay.SetCorner(Str(p, "value")); break;
                case "opacity": overlay.SetAppearance(Num(p, "value") ?? _config.OverlayOpacity, _config.OverlayScale); break;
                case "scale": overlay.SetAppearance(_config.OverlayOpacity, Num(p, "value") ?? _config.OverlayScale); break;
                case "bare": overlay.SetBare(Bool(p, "value")); break;
                case "english": overlay.SetEnglish(Bool(p, "value")); break;
                case "layout": overlay.SetLayout(Str(p, "value")); break;
                // The address the ping goes to: an IPv4 address or a name. The echoes restart towards it when the overlay is next shown.
                case "pingTarget":
                    {
                        string target = Str(p, "value").Trim();
                        if (target.Length > 0 && Uri.CheckHostName(target) is not (UriHostNameType.IPv4 or UriHostNameType.Dns)) throw new ArgumentException(Loc.Get("Overlay_PingTarget_Bad"));
                        _config.OverlayPingTarget = target.Length > 0 ? target : PingMonitor.DefaultTarget;
                        if (overlay.IsVisible) { overlay.SetVisible(false); overlay.SetVisible(true); }
                        break;
                    }
                default: throw new ArgumentException("unknown field");
            }
            Save(); return null;
        });
        // One item on or off, or its chart: the list keeps its order, a new item goes to the end of its part; the preset becomes "custom".
        Method("overlay.item", p =>
        {
            var item = OverlayCatalog.Find(Str(p, "id")) ?? throw new ArgumentException("unknown item");
            var list = overlay.Items.ToList();
            int at = list.FindIndex(c => c.Id == item.Id);
            bool on = Bool(p, "on"), chart = Bool(p, "chart");
            if (!on) { if (at >= 0) list.RemoveAt(at); }
            else if (at >= 0) list[at] = new(item.Id, chart);
            else list.Add(new(item.Id, chart));
            overlay.Configure(list, "custom");
            Save(); return null;
        });
        // The order the technician dragged the shown items into (blocks follow their first item). Ids not shown now are ignored; shown ones
        // missing from the list keep their place after the listed ones. Reordering keeps the preset: it is the same set.
        Method("overlay.order", p =>
        {
            var ids = p.TryGetProperty("ids", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.Array ? a.EnumerateArray().Select(e => e.GetString() ?? "").ToList() : [];
            var current = overlay.Items;
            var ordered = ids.Select(id => current.FirstOrDefault(c => c.Id == id)).OfType<OverlayChoice>().Distinct().ToList();
            ordered.AddRange(current.Where(c => !ordered.Contains(c)));
            overlay.Configure(ordered, _config.OverlayPreset);
            Save(); return null;
        });
        Method("overlay.preset", p =>
        {
            string id = Str(p, "id");
            IReadOnlyList<OverlayChoice>? items = null;
            if (id.StartsWith("user:", StringComparison.Ordinal)) { if (_config.OverlayUserPresets?.TryGetValue(id[5..], out var mine) == true) items = mine; }
            else OverlayCatalog.Presets.TryGetValue(id, out items);
            overlay.Configure(items ?? throw new ArgumentException("unknown preset"), id);
            Save(); return null;
        });
        // The items shown now, kept under a name of the user's own; and taken away again.
        Method("overlay.preset.save", p => { if (!overlay.SavePreset(Str(p, "name"))) throw new ArgumentException(Loc.Get("Web_Overlay_Preset_NameBad")); Save(); return null; });
        Method("overlay.preset.delete", p => { overlay.DeletePreset(Str(p, "name")); Save(); return null; });

        // The frame rate the overlay measured at its last update, for the page's preview (sensor items the page reads from the snapshots itself).
        void OnUpdated(Desktop.ViewModels.OverlayViewModel vm)
        {
            if (!vm.NeedsFrames && !vm.NeedsPing) return;
            var f = vm.Frames; var link = vm.Ping;
            Push("overlayFrames", new { fps = f?.Fps, low1 = f?.Low1Fps, low01 = f?.Low01Fps, frametime = f?.FrameTimeMs, app = f?.App, avg = vm.SessionAverage, min = vm.SessionMin, max = vm.SessionMax,
                ping = link?.PingMs, loss = link?.LossPercent, jitter = link?.JitterMs });
        }
        overlay.Updated += OnUpdated; _cleanup.Add(() => overlay.Updated -= OnUpdated);
        void OnVisible(bool _) => PushSoon("overlayState", State);
        overlay.VisibilityChanged += OnVisible; _cleanup.Add(() => overlay.VisibilityChanged -= OnVisible);
    }

    private static double? Num(System.Text.Json.JsonElement p, string name)
        => p.ValueKind == System.Text.Json.JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetDouble() : null;
}
