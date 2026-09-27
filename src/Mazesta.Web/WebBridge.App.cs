using System.Collections; using System.Globalization; using System.Reflection; using System.Resources;
using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Monitoring;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    /// <summary>The shop links the page may open; the page names one, it never supplies an address.</summary>
    private static readonly Dictionary<string, string> Links = new()
    {
        ["site"] = "https://www.dfmrendering.com/", ["contact"] = "https://www.dfmrendering.com/contactus/", ["pawnio"] = ShellViewModel.PawnIoUrl,
    };

    private void RegisterApp()
    {
        var engine = _sp.GetRequiredService<PollingEngine>(); var shell = _sp.GetRequiredService<ShellViewModel>();
        Method("app.boot", _ => new
        {
            language = _config.Language, rtl = Loc.IsRtl, strings = Strings(),
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "",
            shopName = _config.ShopName, serviceNumber = _config.ServiceNumber, interval = engine.FastInterval.TotalSeconds, paused = engine.State == EngineState.Paused,
            provider = Provider(engine.Provider.Status),
            banner = _configCorrupt ? Loc.Get("Config_Corrupt") : _sp.GetRequiredService<TuningRecovery>().Message,
            units = Enum.GetValues<Unit>().ToDictionary(u => u.ToString(), Units.Symbol),
        });
        Method("app.hardware", _ => Hardware(engine));
        Method("app.setServiceNumber", p => { shell.ServiceNumber = Str(p, "value"); return shell.ServiceNumber; });
        Method("app.togglePause", _ => { shell.TogglePauseCommand.Execute(null); return engine.State == EngineState.Paused; });
        Method("app.openLink", p => { if (Links.TryGetValue(Str(p, "key"), out var url)) Open(url); return null; });
        Method("app.toggleOverlay", _ => { var o = _sp.GetRequiredService<Desktop.Services.OverlayService>(); o.Toggle(); return o.IsVisible; });
        Method("app.navReady", _ => engine.Provider.Status.State is ProviderState.Ready or ProviderState.Degraded or ProviderState.Failed);

        void OnStatus(ProviderStatus s) => Push("provider", Provider(s));
        void OnState(EngineState s) => Push("engine", new { paused = s == EngineState.Paused, failed = s == EngineState.Failed });
        engine.Provider.StatusChanged += OnStatus; engine.StateChanged += OnState;
        _cleanup.Add(() => { engine.Provider.StatusChanged -= OnStatus; engine.StateChanged -= OnState; });
        var overlay = _sp.GetRequiredService<Desktop.Services.OverlayService>();
        void OnOverlay(bool v) => Push("overlay", v);
        overlay.VisibilityChanged += OnOverlay; _cleanup.Add(() => overlay.VisibilityChanged -= OnOverlay);
        _window.Dispatcher.BeginInvoke(() => overlay.RegisterHotkey(_window));
    }

    private static object Provider(ProviderStatus s) => new
    {
        state = s.State.ToString(), text = ShellViewModel.Describe(s), count = s.SensorCount,
        pawnIo = s.ReasonKey == "Provider.PawnIoMissing", reason = s.ReasonKey is null ? null : Loc.Get(s.ReasonKey),
    };

    /// <summary>Every node and sensor, flat: the page groups, filters and formats them itself.</summary>
    private static object Hardware(PollingEngine engine) => engine.Hardware.Select(n => new
    {
        id = n.Id.Value, name = n.Name, kind = n.Kind.ToString(), vendor = n.Vendor.ToString(), parent = n.ParentId?.Value,
        sensors = n.Sensors.OrderBy(s => s.Ordinal).Select(s => new { id = s.Id.Value, name = s.Name, kind = s.Kind.ToString(), unit = s.Unit.ToString(), role = s.Role.ToString() }),
    });

    /// <summary>The whole string table in the app's language (English underneath, so a key missing in Persian still reads), shared with the
    /// WPF edition: one set of translations for both.</summary>
    private static Dictionary<string, string> Strings()
    {
        var rm = new ResourceManager("Mazesta.Desktop.Localization.Strings", typeof(Loc).Assembly);
        var table = new Dictionary<string, string>();
        void Add(ResourceSet? set) { if (set is null) return; foreach (DictionaryEntry e in set) if (e.Key is string k && e.Value is string v) table[k] = v; }
        Add(rm.GetResourceSet(CultureInfo.InvariantCulture, true, true));
        if (Loc.IsRtl) Add(rm.GetResourceSet(Loc.Culture, true, true));
        return table;
    }

    private static void Open(string target) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
}
