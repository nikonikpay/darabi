using System.Net; using System.Net.Http; using System.Text; using System.Text.Json; using System.Text.Json.Nodes; using System.Text.Json.Serialization;
namespace Mazesta.Persistence.Updates;

/// <summary>What the shop's site answered to "are you there, and is this key yours": <see cref="Key"/> is "ok", "wrong" or "missing"; the counts
/// are only told to a holder of the key.</summary>
public sealed record SiteStatus(string? Version, string Key, bool OpenUploads, int? Reports, int? Runs, int? Pending, bool Sharing = false);

/// <summary>A report's one-page summary as the site keeps it: who and what it is about, and the page itself (HTML with nothing to load or run).</summary>
public sealed record SiteReport(string Id, string Title, DateTimeOffset Created, string Kind, string? Verdict, string Machine, string? Service, string Summary, string AppVersion, string Html);
public sealed record SiteReportReceipt(string Id, bool Updated, string Url, string? Link);
/// <summary>The computer a shared result was measured on, as its user chose to show it: the parts' names, never the computer's own name.</summary>
public sealed record SiteMachine(string? Cpu, string? Gpu, double? RamGb, string? Os, string? Name = null);
/// <summary><see cref="Link"/>: the page the site made of the results; <see cref="Queued"/>: how many of them also wait for the shop's review.</summary>
public sealed record SiteShareReceipt(string Link, int Rows, int Queued);
public sealed record SitePairing(string Url, string Code, int Seconds);
public sealed record SitePairClaim(string State, string? Key);
/// <summary><see cref="Pending"/>: the runs went to the site's review queue (sent without the shop's key), not into the lists yet.</summary>
public sealed record SiteRunsReceipt(int Added, int Known, int Rejected, bool Pending, int? Lists);

/// <summary>The site refused or could not be understood; <see cref="Status"/> is its HTTP status when it answered at all (401: the key, 404: the
/// plugin is not installed).</summary>
public sealed class SiteException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// The app's side of the shop's site plugin (Mazesta Connect, REST namespace <c>mazesta/v1</c>): everything the app sends to the site, and the
/// benchmark comparison lists it reads back. The key travels in a header, never in the address. What comes back is data only: a list is checked
/// against the size and SHA-256 the site's index names, which guards against a broken transfer, not against the site itself; nothing from here
/// is ever run (the app's own releases stay behind the shop's signature, see <see cref="UpdateClient"/>).
/// </summary>
public sealed class SiteClient(Uri api, HttpClient http)
{
    public const string KeyHeader = "X-Mazesta-Key";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public Uri Api => api;

    public async Task<SiteStatus> StatusAsync(string? key, CancellationToken ct)
        => Read<SiteStatus>(await SendAsync(HttpMethod.Get, "status?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(), key, null, ct).ConfigureAwait(false));

    public async Task<SiteReportReceipt> SendReportAsync(string key, SiteReport report, CancellationToken ct)
        => Read<SiteReportReceipt>(await SendAsync(HttpMethod.Post, "reports", key, JsonSerializer.Serialize(report, Json), ct).ConfigureAwait(false));

    /// <summary>Benchmark runs (as the run log writes them), which benchmarks count higher as better, and the shop's marks on runs. At most
    /// <see cref="RunsPerRequest"/> runs a call; the site takes each run once, by its id.</summary>
    public async Task<SiteRunsReceipt> SendRunsAsync(string? key, IReadOnlyList<JsonNode> runs, IReadOnlyDictionary<string, bool> higher, JsonNode? marks, CancellationToken ct,
        IReadOnlyDictionary<string, string>? names = null)
    {
        var body = new JsonObject { ["runs"] = new JsonArray([.. runs.Select(r => r.DeepClone())]), ["higher"] = JsonSerializer.SerializeToNode(higher) };
        if (marks is not null) body["marks"] = marks.DeepClone();
        if (names is { Count: > 0 }) body["names"] = JsonSerializer.SerializeToNode(names);
        return Read<SiteRunsReceipt>(await SendAsync(HttpMethod.Post, "bench/runs", key, body.ToJsonString(), ct).ConfigureAwait(false));
    }
    public const int RunsPerRequest = 200;

    /// <summary>A user's latest results (one run per benchmark, at most <see cref="RunsPerShare"/>), sent without a key to become a page of
    /// their own on the site. <paramref name="names"/> are the benchmarks' names as the app shows them.</summary>
    public async Task<SiteShareReceipt> ShareAsync(IReadOnlyList<JsonNode> runs, IReadOnlyDictionary<string, string> names, IReadOnlyDictionary<string, bool> higher, SiteMachine machine, string appVersion, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["runs"] = new JsonArray([.. runs.Select(r => r.DeepClone())]), ["names"] = JsonSerializer.SerializeToNode(names), ["higher"] = JsonSerializer.SerializeToNode(higher),
            ["machine"] = JsonSerializer.SerializeToNode(machine, Json), ["appVersion"] = appVersion,
        };
        return Read<SiteShareReceipt>(await SendAsync(HttpMethod.Post, "share", null, body.ToJsonString(), ct).ConfigureAwait(false));
    }
    public const int RunsPerShare = 60;

    /// <summary>The site's key as the site makes it; anything else typed into the key's field (another secret, a line of text) is not sent anywhere.</summary>
    public static bool IsKey(string? text) => text is { Length: 51 } && text.StartsWith("mz_", StringComparison.Ordinal) && text.AsSpan(3).IndexOfAnyExcept("0123456789abcdef") < 0;

    /// <summary>
    /// Connecting without typing the key. The app keeps a random secret and sends its SHA-256; the site answers with the dashboard page where a
    /// manager signed in to the site approves the request, and a short code both sides show. <see cref="PairClaimAsync"/> then trades the secret
    /// for the key, once, after the approval. A stranger who starts a request gets nothing unless a manager approves that very code.
    /// </summary>
    /// <summary>The key inside what was pasted (a copy from a web page can bring spaces, a direction mark or the label beside it), or null.</summary>
    public static string? FindKey(string? text)
    {
        if (text is null) return null;
        for (int i = text.IndexOf("mz_", StringComparison.Ordinal); i >= 0 && i + 51 <= text.Length; i = text.IndexOf("mz_", i + 1, StringComparison.Ordinal))
            if (IsKey(text.Substring(i, 51)) && (i + 51 == text.Length || !Uri.IsHexDigit(text[i + 51]))) return text.Substring(i, 51);
        return null;
    }

    public async Task<SitePairing> PairStartAsync(string secret, string computer, CancellationToken ct)
    {
        string id = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(secret)));
        return Read<SitePairing>(await SendAsync(HttpMethod.Post, "pair/start", null, JsonSerializer.Serialize(new { id, name = computer }), ct).ConfigureAwait(false));
    }
    /// <summary>State "waiting", "gone" (refused or older than ten minutes) or "ok" with the key.</summary>
    public async Task<SitePairClaim> PairClaimAsync(string secret, CancellationToken ct)
        => Read<SitePairClaim>(await SendAsync(HttpMethod.Post, "pair/claim", null, JsonSerializer.Serialize(new { secret }), ct).ConfigureAwait(false));

    /// <summary>The site's comparison lists as a manifest (file, size, SHA-256 each), read through the same checks as the signed one's entries.</summary>
    public async Task<UpdateManifest> ListsAsync(CancellationToken ct)
    {
        var json = await SendAsync(HttpMethod.Get, "bench/index?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(), null, null, ct).ConfigureAwait(false);
        try
        {
            var files = JsonNode.Parse(json)?["files"] as JsonArray ?? throw new SiteException("The site's list index is not understood.");
            var manifest = new JsonObject { ["format"] = UpdateManifest.CurrentFormat, ["published"] = DateTimeOffset.UtcNow, ["data"] = files.DeepClone() };
            return UpdateManifest.Parse(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException) { throw new SiteException(e.Message); }
    }

    /// <summary>Fetches the lists that changed into <paramref name="folder"/> and removes the ones the site no longer has.</summary>
    public async Task<DataSync.Result> SyncListsAsync(string folder, CancellationToken ct)
        => await DataSync.SyncAsync(new UpdateClient(api, "", http), await ListsAsync(ct).ConfigureAwait(false), folder, ct).ConfigureAwait(false);

    private async Task<string> SendAsync(HttpMethod method, string path, string? key, string? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, new Uri(api, path));
        if (!string.IsNullOrEmpty(key)) req.Headers.TryAddWithoutValidation(KeyHeader, key);
        if (body is not null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        string text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (res.IsSuccessStatusCode) return text;
        // WordPress words an error as {"code":…,"message":…}; a page of HTML instead means something else answered (no plugin, a firewall).
        string? said = null;
        try { said = JsonNode.Parse(text)?["message"]?.GetValue<string>(); } catch (Exception e) when (e is JsonException or InvalidOperationException) { }
        throw new SiteException(said ?? $"The site answered {(int)res.StatusCode}.", res.StatusCode);
    }

    private static T Read<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, Json) ?? throw new SiteException("The site's answer was empty."); }
        catch (JsonException e) { throw new SiteException("The site's answer is not understood: " + e.Message); }
    }
}
