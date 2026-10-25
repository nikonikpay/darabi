using System.IO; using System.Net.Http; using System.Security.Cryptography; using System.Text; using System.Text.Json; using System.Text.Json.Nodes;
using Mazesta.Core.Access; using Mazesta.Persistence; using Mazesta.Persistence.Updates; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

/// <summary>
/// What the shop's site lets this copy use (see <see cref="Entitlements"/>). Today the site answers "nothing is locked", so nothing shows and the program is free. The answer is
/// asked a minute after start and once a day, whatever the usage statistics setting says (a lock must not depend on it). The answer, with the end date of a Pro plan, is kept in
/// <c>Data/config/access.dat</c>, encrypted and bound to this installation (so it is not readable or editable as text), and also holds offline: a plan whose end date has passed stops by
/// itself, and a clock turned back does not help (the latest time seen is kept too). Asking sends only the installation's random id and, once signed in, the account's token.
/// A failure is tried again later and never shown.
/// </summary>
internal sealed class AccessService : IDisposable
{
    private readonly string _file; private readonly UsageLog _log; private readonly AppConfig _config; private readonly SiteClient _site; private readonly ILogger _logger;
    private readonly object _gate = new(); private readonly System.Threading.Timer _timer; private Entitlements _now = Entitlements.Free; private string? _token; private string? _siteJson; private int _polling; private DateTimeOffset _seen = DateTimeOffset.MinValue, _savedSeen = DateTimeOffset.MinValue;

    /// <summary>Raised whenever what the copy may use (or whether an account is signed in) changed.</summary>
    public event Action? Changed;

    public AccessService(string configDir, UsageLog log, AppConfig config, SiteClient site, ILogger logger)
    {
        _file = Path.Combine(configDir, "access.dat"); _log = log; _config = config; _site = site; _logger = logger;
        Load();
        _timer = new(_ => _ = RefreshAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(24));
    }

    public Entitlements Current { get { lock (_gate) return _now; } }
    public bool SignedIn { get { lock (_gate) return _token is not null; } }

    public async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
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

    /// <summary>The time to judge a Pro plan's end by: the clock, but never earlier than the latest time seen before (turning the clock back gains nothing). The latest time is kept at most once an hour.</summary>
    public DateTimeOffset Now()
    {
        var n = DateTimeOffset.UtcNow; bool save = false;
        lock (_gate) { if (n < _seen) return _seen; _seen = n; save = n - _savedSeen > TimeSpan.FromHours(1); }
        if (save) lock (_gate) Save();
        return n;
    }

    // The file is AES-GCM sealed with a key made from the installation's id: nonce (12) + tag (16) + data.
    private byte[] Key() => SHA256.HashData(Encoding.UTF8.GetBytes("mazesta-access|" + _log.InstallId + "|7Zp2#k"));

    private void Load()
    {
        try
        {
            var raw = File.ReadAllBytes(_file); if (raw.Length < 29) return;
            var plain = new byte[raw.Length - 28]; using var g = new AesGcm(Key(), 16); g.Decrypt(raw.AsSpan(0, 12), raw.AsSpan(28), raw.AsSpan(12, 16), plain);
            var o = JsonNode.Parse(Encoding.UTF8.GetString(plain)); _token = o?["token"]?.GetValue<string>(); _siteJson = o?["site"]?.ToJsonString(); _now = Entitlements.Parse(_siteJson);
            if (DateTimeOffset.TryParse(o?["seen"]?.GetValue<string>(), out var s)) _seen = _savedSeen = s;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or CryptographicException) { }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!); string t = _file + ".tmp";
            var now = DateTimeOffset.UtcNow; if (now > _seen) _seen = now; _savedSeen = _seen;
            var plain = Encoding.UTF8.GetBytes(new JsonObject { ["token"] = _token, ["site"] = _siteJson is null ? null : JsonNode.Parse(_siteJson), ["seen"] = _seen.ToString("O") }.ToJsonString());
            var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var cipher = new byte[plain.Length];
            using (var g = new AesGcm(Key(), 16)) g.Encrypt(nonce, plain, cipher, tag);
            File.WriteAllBytes(t, [.. nonce, .. tag, .. cipher]);
            File.Move(t, _file, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { _logger.LogDebug("Access not saved: {Message}", e.Message); }
    }

    public void Dispose() => _timer.Dispose();
}
