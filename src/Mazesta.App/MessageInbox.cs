using System.IO; using System.Net.Http; using System.Text.Json; using System.Text.Json.Nodes;
using Mazesta.Persistence; using Mazesta.Persistence.Updates; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

/// <summary>One message from the shop, as it is kept and shown: plain text (the page never gets markup) and a link only to the shop's own site.</summary>
public sealed record ShopMessage(long Id, string Title, string Body, string? Link, DateTimeOffset At, bool Read);

/// <summary>
/// The shop's messages to this computer ("system messages"): asks the site every fifteen minutes (and a minute after start) for what was sent to every installation or to this
/// one - the installation's random id is all that is said, no key - keeps them in <c>Data/config/messages.json</c> so they can be read again later, and tells the app about the
/// new ones (a notification). Like the usage statistics it runs only while the setting for them is on; a failure is tried again later and never shown.
/// </summary>
internal sealed class MessageInbox : IDisposable
{
    private const int Keep = 200;
    private readonly string _file; private readonly UsageLog _log; private readonly AppConfig _config; private readonly SiteClient _site; private readonly ILogger _logger;
    private readonly object _gate = new(); private readonly System.Threading.Timer _timer; private List<ShopMessage> _items; private long _after; private int _polling;

    /// <summary>Raised (on a pool thread) for each message that arrived just now.</summary>
    public event Action<ShopMessage>? Arrived;
    /// <summary>Raised whenever the list or a read mark changed.</summary>
    public event Action? Changed;

    public MessageInbox(string configDir, UsageLog log, AppConfig config, SiteClient site, ILogger logger)
    {
        _file = Path.Combine(configDir, "messages.json"); _log = log; _config = config; _site = site; _logger = logger;
        (_after, _items) = Load();
        _timer = new(_ => _ = PollAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15));
    }

    public IReadOnlyList<ShopMessage> List() { lock (_gate) return [.. _items.OrderByDescending(m => m.At)]; }
    public int Unread { get { lock (_gate) return _items.Count(m => !m.Read); } }

    public void MarkRead(long? id)
    {
        lock (_gate) { _items = [.. _items.Select(m => id is null || m.Id == id ? m with { Read = true } : m)]; Save(); }
        Changed?.Invoke();
    }

    public async Task PollAsync()
    {
        if (!_config.UsageReport || Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            var root = JsonNode.Parse(await _site.MessagesAsync(_log.InstallId, _after, CancellationToken.None).ConfigureAwait(false));
            var fresh = new List<ShopMessage>();
            foreach (var n in root?["messages"] as JsonArray ?? [])
            {
                if (n?["id"]?.GetValue<long>() is not { } id || id <= _after) continue;
                string? link = n["link"]?.GetValue<string>();
                string title = Clip(n["title"]?.GetValue<string>(), 120), body = Clip(n["body"]?.GetValue<string>(), 2000);
                if (title.Length == 0 && body.Length == 0) continue;
                _ = DateTimeOffset.TryParse(n["created"]?.GetValue<string>(), out var at);
                fresh.Add(new(id, title, body, link is not null && ShopFeed.IsShopLink(link) ? link : null, at == default ? DateTimeOffset.UtcNow : at, false));
            }
            if (fresh.Count == 0) return;
            lock (_gate) { _items = [.. _items.Concat(fresh).OrderByDescending(m => m.At).Take(Keep)]; _after = Math.Max(_after, fresh.Max(m => m.Id)); Save(); }
            Changed?.Invoke();
            foreach (var m in fresh.OrderBy(m => m.At)) Arrived?.Invoke(m);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or SiteException or JsonException or InvalidOperationException or FormatException or IOException)
        { _logger.LogDebug("Messages not fetched now: {Message}", e.Message); }
        finally { Interlocked.Exchange(ref _polling, 0); }
    }

    private static string Clip(string? s, int n) { s = (s ?? "").Trim(); return s.Length > n ? s[..n] : s; }

    private (long, List<ShopMessage>) Load()
    {
        try
        {
            var o = JsonNode.Parse(File.ReadAllText(_file));
            return (o?["after"]?.GetValue<long>() ?? 0, o?["items"]?.Deserialize<List<ShopMessage>>() ?? []);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return (0, []); }
    }

    private void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(_file)!); string t = _file + ".tmp"; File.WriteAllText(t, new JsonObject { ["after"] = _after, ["items"] = JsonSerializer.SerializeToNode(_items) }.ToJsonString()); File.Move(t, _file, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _logger.LogInformation("Messages not saved: {Message}", e.Message); }
    }

    public void Dispose() => _timer.Dispose();
}
