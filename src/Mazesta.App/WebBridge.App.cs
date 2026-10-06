using System.Collections; using System.Globalization; using System.Reflection; using System.Resources;
using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Monitoring;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>The shop links the page may open; the page names one, it never supplies an address.</summary>
    private static readonly Dictionary<string, string> Links = new()
    {
        ["site"] = "https://www.dfmrendering.com/", ["contact"] = "https://www.dfmrendering.com/contactus/", ["pawnio"] = ProviderText.PawnIoUrl,
        ["shop"] = "https://www.dfmrendering.com/shop/", ["systems"] = "https://www.dfmrendering.com/product-category/systems/",
        ["sales-whatsapp"] = "https://wa.me/989197588700", ["support-whatsapp"] = "https://wa.me/989197588701",
        ["sales-telegram"] = "https://t.me/DFMRendering", ["support-telegram"] = "https://t.me/dfm_support", ["bale"] = "https://ble.ir/join/DjEi5p9iS5", ["channel-telegram"] = "https://t.me/DFMRendering", ["instagram"] = "https://www.instagram.com/dfm.rendering/",
        ["email"] = "mailto:info@dfmrendering.com",
    };

    /// <summary>The company's contact details as its own site publishes them (dfmrendering.com/contactus, read 2026-09-29; the site gives one Bale link for both desks). Numbers stay as the
    /// site writes them; the page shows them and opens only the links above.</summary>
    private static readonly object Contact = new
    {
        sales = "09197588700", support = "09197588701", office = "021-41139", email = "info@dfmrendering.com",
        hours = "Contact_Hours", address = "Contact_Address", postcode = "1571837738",
    };

    private void RegisterApp()
    {
        var engine = _sp.GetRequiredService<PollingEngine>();
        // The service number is Mazesta's own: a users' copy has no field for it, so one left in a carried-over settings file is not printed.
        if (!Staff) _config.ServiceNumber = "";
        Method("app.boot", _ => new
        {
            language = _config.Language, rtl = Loc.IsRtl, strings = Strings(),
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "",
            staff = Staff, serviceNumber = _config.ServiceNumber, interval = engine.FastInterval.TotalSeconds, paused = engine.State == EngineState.Paused,
            provider = Provider(engine.Provider.Status),
            banner = _configCorrupt ? Loc.Get("Config_Corrupt") : string.Join(" ", new[] { _sp.GetRequiredService<TuningRecovery>().Message, _sp.GetRequiredService<Desktop.Services.BenchmarkBreakWatch>().Message, FontSmoothing.IsOff() ? Loc.Get("Font_Smoothing_Off") : null,
                // A test session cut off by a restart: the Tests page says where it stopped and why; this says so at the first look.
                _sp.GetRequiredService<Diagnostics.TestEngine>().FindIncompleteSession() is not null ? Loc.Get("Test_Break_Banner") : null }.OfType<string>()) is { Length: > 0 } b ? b : null,
            units = Enum.GetValues<Unit>().ToDictionary(u => u.ToString(), Units.Symbol),
            contact = Contact, updated = Program.TakeJustUpdated(),
        });
        var shop = new ShopFeed(_paths.CacheDir, _log, null, "product", onSaleFirst: true); var systems = new ShopFeed(_paths.CacheDir, _log, ShopFeed.SystemsCategory, "system");
        MethodAsync("shop.product", async p => await (Str(p, "kind") == "system" ? systems : shop).GetAsync(Bool(p, "another")).ConfigureAwait(true));
        Method("shop.open", p => { var url = Str(p, "url"); if (ShopFeed.IsShopLink(url)) Open(url); return null; });
        Method("app.hardware", _ => Hardware(engine));
        // The adapters connected now, the internet's first: the network page shows those, not the first port Windows lists (often unplugged).
        Method("app.network", _ => Diagnostics.Network.InternetAdapter.Find() is var a ? new { internet = a.Internet, up = a.Up } : null);
        // The service job being worked on, printed on every report; Persian digits become Latin so the number reads the same everywhere.
        Method("app.setServiceNumber", p => { StaffOnly(); return _config.ServiceNumber = Core.Text.PersianDigits.Normalize(Str(p, "value") ?? "").Trim(); });
        Method("app.togglePause", _ => { if (engine.State == EngineState.Paused) engine.Resume(); else engine.Pause(); return engine.State == EngineState.Paused; });
        Method("app.openLink", p => { if (Links.TryGetValue(Str(p, "key"), out var url)) Open(url); return null; });
        Method("app.toggleOverlay", _ => { var o = _sp.GetRequiredService<Desktop.Services.OverlayService>(); o.Toggle(); return o.IsVisible; });
        Method("app.navReady", _ => engine.Provider.Status.State is ProviderState.Ready or ProviderState.Degraded or ProviderState.Failed);

        void OnStatus(ProviderStatus s) => Push("provider", Provider(s));
        void OnState(EngineState s) => Push("engine", new { paused = s == EngineState.Paused, failed = s == EngineState.Failed });
        engine.Provider.StatusChanged += OnStatus; engine.StateChanged += OnState;
        _cleanup.Add(() => { engine.Provider.StatusChanged -= OnStatus; engine.StateChanged -= OnState; });
        var notifier = new Notifier(_window, engine, _sp.GetRequiredService<Diagnostics.TestEngine>(), _sp.GetRequiredService<IEnumerable<Diagnostics.ITestExecutor>>(),
            (text, kind) => Push("toast", new { text, kind }), _log);
        notifier.Limits = () => (_config.TrayCpuAlertC, _config.TrayGpuAlertC);
        _cleanup.Add(notifier.Dispose);
        var failures = new FailureWatch(_sp.GetRequiredService<Diagnostics.TestEngine>(), _sp.GetRequiredService<Diagnostics.Benchmarks.BenchmarkRunner>(), _sp.GetRequiredService<IEnumerable<Diagnostics.ITestExecutor>>(), _log, f => Push("testfail", f));
        _cleanup.Add(failures.Dispose);
        var overlay = _sp.GetRequiredService<Desktop.Services.OverlayService>();
        void OnOverlay(bool v) => Push("overlay", v);
        overlay.VisibilityChanged += OnOverlay; _cleanup.Add(() => overlay.VisibilityChanged -= OnOverlay);
    }

    private static object Provider(ProviderStatus s) => new
    {
        state = s.State.ToString(), text = ProviderText.Describe(s), count = s.SensorCount,
        pawnIo = s.ReasonKey == ProviderText.PawnIoMissing, reason = s.ReasonKey is null ? null : Loc.Get(s.ReasonKey),
    };

    /// <summary>Every node and sensor, flat: the page groups, filters and formats them itself.</summary>
    private static object Hardware(PollingEngine engine) => engine.Hardware.Select(n => new
    {
        id = n.Id.Value, name = n.Name, kind = n.Kind.ToString(), vendor = n.Vendor.ToString(), parent = n.ParentId?.Value,
        sensors = n.Sensors.OrderBy(s => s.Ordinal).Select(s => new { id = s.Id.Value, name = s.Name, kind = s.Kind.ToString(), unit = s.Unit.ToString(), role = s.Role.ToString() }),
    });

    /// <summary>The whole string table in the app's language (English underneath, so a key missing in Persian still reads), kept in the app layer
    /// (Mazesta.Desktop/Localization).</summary>
    internal static Dictionary<string, string> Strings()
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
