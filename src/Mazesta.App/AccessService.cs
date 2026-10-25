using System.IO; using System.Net.Http; using System.Text.Json; using System.Text.Json.Nodes;
using Mazesta.Core.Access; using Mazesta.Persistence; using Mazesta.Persistence.Updates; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

/// <summary>
/// What the shop's site lets this copy use (see <see cref="Entitlements"/>). Today the site answers "nothing is locked", so nothing shows and the program is free. The answer is
/// asked a minute after start and every six hours, kept in <c>Data/config/access.json</c> (so a lock or a Pro plan also holds offline), and the page re-draws when it changes.
/// Asking sends only the installation's random id and, once the user has signed in with a mobile number, the account's token. Like the usage statistics it asks only while that setting is on
/// (the user's own sign-in is always allowed: they asked for it). A failure is tried again later and never shown.
/// </summary>
internal sealed class AccessService : IDisposable
{
    private readonly string _file; private readonly UsageLog _log; private readonly AppConfig _config; private readonly SiteClient _site; private readonly ILogger _logger;
    private readonly object _gate = new(); private readonly System.Threading.Timer _timer; private Entitlements _now = Entitlements.Free; private string? _token; private string? _siteJson; private int _polling;

    /// <summary>Raised whenever what the copy may use (or whether an account is signed in) changed.</summary>
    public event Action? Changed;

    public AccessService(string configDir, UsageLog log, AppConfig config, SiteClient site, ILogger logger)
    {
        _file = Path.Combine(configDir, "access.json"); _log = log; _config = config; _site = site; _logger = logger;
        Load();
        _timer = new(_ => _ = RefreshAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(6));
    }

    public Entitlements Current { get { lock (_gate) return _now; } }
    public bool SignedIn { get { lock (_gate) return _token is not null; } }

    public async Task RefreshAsync()
    {
        if (!_config.UsageReport || Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            string? token; lock (_gate) token = _token;
            Apply(await _site.AccessAsync(_log.InstallId, token, CancellationToken.None).ConfigureAwait(false), token);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or SiteException or JsonException or InvalidOperationException or FormatException or IOException)
        { _logger.LogDebug("Access not fetched now: {Message}", e.Message); }
        finally { Interlocked.Exchange(ref _polling, 0); }
    }

    /// <summary>Asks the site to text a code to the number (the user asked: no matter the statistics setting). Throws <see cref="SiteException"/> with the site's words on a refusal.</summary>
    public Task CodeAsync(string mobile, CancellationToken ct) => _site.AccessCodeAsync(mobile, ct);

    /// <summary>Trades the code for the account; the site links this computer to the number. Throws <see cref="SiteException"/> when the code is wrong.</summary>
    public async Task VerifyAsync(string mobile, string code, CancellationToken ct)
    {
        string json = await _site.AccessVerifyAsync(mobile, code, _log.InstallId, ct).ConfigureAwait(false);
        string? token = JsonNode.Parse(json)?["token"]?.GetValue<string>();
        if (token is not { Length: 64 }) throw new SiteException("The site's answer is not understood.");
        Apply(json, token);
    }

    public async Task LogoutAsync()
    {
        string? token; lock (_gate) token = _token;
        if (token is not null) { try { await _site.AccessLogoutAsync(token, CancellationToken.None).ConfigureAwait(false); } catch (Exception e) when (e is HttpRequestException or TaskCanceledException or SiteException) { } }
        lock (_gate) { _token = null; _now = _now with { Plan = "free", Until = null, Account = null, Locks = _now.FreeLocks }; Save(); }   // until the site answers again, the free locks apply
        Changed?.Invoke(); await RefreshAsync().ConfigureAwait(false);
    }

    private void Apply(string json, string? token)
    {
        var ent = Entitlements.Parse(json); bool changed;
        if (token is not null && ent.Account is null) token = null;   // the site no longer knows the token (it ran out): signed out
        lock (_gate) { changed = ent != _now || token != _token; _now = ent; _token = token; _siteJson = json; if (changed) Save(); }
        if (changed) Changed?.Invoke();
    }

    private void Load()
    {
        try { var o = JsonNode.Parse(File.ReadAllText(_file)); _token = o?["token"]?.GetValue<string>(); _siteJson = o?["site"]?.ToJsonString(); _now = Entitlements.Parse(_siteJson); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!); string t = _file + ".tmp";
            File.WriteAllText(t, new JsonObject { ["token"] = _token, ["site"] = _siteJson is null ? null : JsonNode.Parse(_siteJson) }.ToJsonString());
            File.Move(t, _file, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { _logger.LogDebug("Access not saved: {Message}", e.Message); }
    }

    public void Dispose() => _timer.Dispose();
}
