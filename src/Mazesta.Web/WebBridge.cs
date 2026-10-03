using System.Collections.Specialized; using System.ComponentModel; using System.Text.Json; using System.Text.Json.Serialization;
using Mazesta.Persistence; using Microsoft.Extensions.Logging; using Microsoft.Web.WebView2.Core;
namespace Mazesta.Web;

/// <summary>
/// The only way the page reaches the machine. The page sends <c>{id, m, p}</c> and gets <c>{id, ok, r | e}</c> back; the host pushes
/// <c>{ev, d}</c> events. Every method is registered by name here, so the page can only do what is listed: there is no generic "call any
/// method" and no file path ever comes from the page. Handlers run on the UI thread, as the view models expect.
/// Live pushes stop while the window is minimised: nothing is serialised for a page nobody sees.
/// </summary>
public sealed partial class WebBridge : IDisposable
{
    private readonly CoreWebView2 _core; private readonly IServiceProvider _sp; private readonly AppPaths _paths; private readonly AppConfig _config;
    private readonly JsonStore<AppConfig> _store; private readonly bool _configCorrupt; private readonly MainWindow _window; private readonly ILogger _log;
    private readonly Dictionary<string, Func<JsonElement, Task<object?>>> _methods = [];
    private readonly List<Action> _cleanup = [];
    private readonly HashSet<string> _pending = [];
    private bool _visible = true;

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        ReferenceHandler = ReferenceHandler.IgnoreCycles, Converters = { new JsonStringEnumConverter() }, DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public WebBridge(CoreWebView2 core, IServiceProvider sp, AppPaths paths, AppConfig config, JsonStore<AppConfig> store, bool configCorrupt, MainWindow window, ILogger log)
    {
        _core = core; _sp = sp; _paths = paths; _config = config; _store = store; _configCorrupt = configCorrupt; _window = window; _log = log;
        core.WebMessageReceived += OnMessage;
        RegisterApp(); RegisterMonitoring(); RegisterTests(); RegisterBenchmarks(); RegisterCheckup(); RegisterAi(); RegisterLan(); RegisterTuning(); RegisterReports(); RegisterTools(); RegisterSettings(); RegisterDiagnostics(); RegisterOverlay(); RegisterSystem(); RegisterTweaks(); RegisterAppUpdate(); RegisterDrivers(); RegisterQuiet(); RegisterBattery(); RegisterNetRepair();
    }

    private void MethodAsync(string name, Func<JsonElement, Task<object?>> handler) => _methods[name] = handler;
    private void Method(string name, Func<JsonElement, object?> handler) => _methods[name] = p => Task.FromResult(handler(p));

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        long id = 0;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            id = root.GetProperty("id").GetInt64();
            string name = root.GetProperty("m").GetString() ?? "";
            var p = root.TryGetProperty("p", out var pv) ? pv.Clone() : default;
            if (!_methods.TryGetValue(name, out var handler)) { Reply(id, false, null, $"unknown method {name}"); return; }
            Reply(id, true, await handler(p), null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Bridge call failed");
            Reply(id, false, null, ex.Message);
        }
    }

    private void Reply(long id, bool ok, object? result, string? error)
        => Post(ok ? new { id, ok, r = result } : new { id, ok, r = (object?)null, e = error });

    /// <summary>An event for the page. Always marshalled to the UI thread: the engines raise theirs on worker threads.</summary>
    public void Push(string ev, object? data)
    {
        if ((!_visible || _quiet) && ev is "snapshot" or "overlayFrames") return;
        if (_window.Dispatcher.CheckAccess()) Post(new { ev, d = data }); else _window.Dispatcher.BeginInvoke(() => Post(new { ev, d = data }));
    }

    /// <summary>A state push that may fire many times in a row (a view model changing several properties): the page gets one, a moment later.</summary>
    private void PushSoon(string ev, Func<object?> state)
    {
        void Queue()
        {
            if (!_pending.Add(ev)) return;   // one already queued sends the newest state
            _window.Dispatcher.BeginInvoke(() => { _pending.Remove(ev); Post(new { ev, d = state() }); });
        }
        if (_window.Dispatcher.CheckAccess()) Queue(); else _window.Dispatcher.BeginInvoke(Queue);
    }

    private void Post(object message)
    {
        try { _core.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json)); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }   // the view is closing
    }

    public void SetVisible(bool visible) { _visible = visible; Push("visibility", visible); }

    /// <summary>Watches a view model, its row view models and its collections, and pushes its state (as <paramref name="project"/> shapes it)
    /// whenever any of them changes.</summary>
    private void Mirror(string ev, INotifyPropertyChanged vm, Func<object?> project, params INotifyCollectionChanged[] collections)
    {
        void Changed() => PushSoon(ev, project);
        PropertyChangedEventHandler onProp = (_, _) => Changed();
        var watched = new HashSet<INotifyPropertyChanged>();
        void Watch(object? item) { if (item is INotifyPropertyChanged n && watched.Add(n)) n.PropertyChanged += onProp; }
        NotifyCollectionChangedEventHandler onCollection = (_, a) => { foreach (var i in a.NewItems ?? Array.Empty<object>()) Watch(i); Changed(); };
        Watch(vm);
        foreach (var c in collections) { c.CollectionChanged += onCollection; if (c is System.Collections.IEnumerable items) foreach (var i in items) Watch(i); }
        _cleanup.Add(() => { foreach (var n in watched) n.PropertyChanged -= onProp; foreach (var c in collections) c.CollectionChanged -= onCollection; });
    }

    private static string Str(JsonElement p, string name) => p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString() : "";
    private static bool Bool(JsonElement p, string name) => p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public void Dispose()
    {
        _core.WebMessageReceived -= OnMessage;
        foreach (var c in _cleanup) c();
    }
}
