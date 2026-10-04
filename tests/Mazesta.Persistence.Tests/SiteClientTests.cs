using System.Net; using System.Net.Http; using System.Security.Cryptography; using System.Text; using System.Text.Json.Nodes;
using Xunit; using Mazesta.Persistence.Updates;
namespace Mazesta.Persistence.Tests;

public class SiteClientTests
{
    /// <summary>The site, as far as the client can tell: each request is recorded and answered by path.</summary>
    private sealed class FakeSite(Func<HttpRequestMessage, string, (HttpStatusCode, string)> answer) : HttpMessageHandler
    {
        public List<(string Method, string Path, string? Key, string Body)> Seen { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Seen.Add((request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.TryGetValues(SiteClient.KeyHeader, out var k) ? k.First() : null, body));
            var (status, text) = answer(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }
    private static (SiteClient, FakeSite) Client(Func<HttpRequestMessage, string, (HttpStatusCode, string)> answer)
    {
        var site = new FakeSite(answer);
        return (new SiteClient(new Uri("https://shop.example/wp-json/mazesta/v1/"), new HttpClient(site)), site);
    }
    private static string Sha(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    [Fact] public async Task The_key_travels_in_a_header_never_in_the_address()
    {
        var (client, site) = Client((_, _) => (HttpStatusCode.OK, """{"name":"Mazesta Connect","version":"1.0.0","key":"ok","openUploads":false,"reports":3,"runs":40,"pending":2}"""));
        var s = await client.StatusAsync("mz_secret", CancellationToken.None);
        Assert.Equal(("1.0.0", "ok", 3, 40, 2), (s.Version, s.Key, s.Reports, s.Runs, s.Pending));
        var seen = Assert.Single(site.Seen);
        Assert.Equal("mz_secret", seen.Key); Assert.DoesNotContain("mz_secret", seen.Path); Assert.StartsWith("/wp-json/mazesta/v1/status?t=", seen.Path);
    }

    [Fact] public async Task A_refusal_carries_the_site_s_own_words_and_its_status()
    {
        var (client, _) = Client((_, _) => (HttpStatusCode.Unauthorized, """{"code":"mazesta_key","message":"The site key is missing or wrong.","data":{"status":401}}"""));
        var e = await Assert.ThrowsAsync<SiteException>(() => client.SendReportAsync("bad", new SiteReport("abcdef0123456789", "t", DateTimeOffset.UnixEpoch, "TestSession", "Passed", "CPU", "S-12", "3 tests", "0.8.0", "<html></html>"), CancellationToken.None));
        Assert.Equal(HttpStatusCode.Unauthorized, e.Status); Assert.Equal("The site key is missing or wrong.", e.Message);
    }

    [Fact] public async Task A_page_of_html_instead_of_the_plugin_s_answer_is_an_error_not_a_crash()
    {
        var (client, _) = Client((_, _) => (HttpStatusCode.NotFound, "<html>not here</html>"));
        var e = await Assert.ThrowsAsync<SiteException>(() => client.StatusAsync(null, CancellationToken.None));
        Assert.Equal(HttpStatusCode.NotFound, e.Status);
    }

    [Fact] public async Task A_report_goes_as_json_with_its_page_and_comes_back_with_where_it_is()
    {
        var (client, site) = Client((_, _) => (HttpStatusCode.OK, """{"id":"abcdef0123456789","updated":false,"url":"https://shop.example/wp-admin/admin-post.php?action=mzc_report&id=abcdef0123456789","link":null}"""));
        var r = await client.SendReportAsync("k", new SiteReport("abcdef0123456789", "خلاصه", DateTimeOffset.UnixEpoch, "TestSession", null, "CPU · GPU", null, "3 tests", "0.8.0", "<html>x</html>"), CancellationToken.None);
        Assert.Equal(("abcdef0123456789", false, (string?)null), (r.Id, r.Updated, r.Link));
        var sent = JsonNode.Parse(site.Seen[0].Body)!;
        Assert.Equal("abcdef0123456789", sent["id"]!.GetValue<string>()); Assert.Equal("<html>x</html>", sent["html"]!.GetValue<string>());
        Assert.Null(sent["verdict"]);   // a benchmark report has none: left out, not sent as a word
    }

    [Fact] public async Task Runs_are_sent_as_they_are_logged_with_which_way_is_better()
    {
        var (client, site) = Client((_, _) => (HttpStatusCode.OK, """{"added":1,"known":0,"rejected":0,"pending":false,"marked":0,"lists":4}"""));
        var run = JsonNode.Parse("""{"id":"run-00000001","benchmark":"bench.cpu.multi","version":1,"settings":"","part":"Core i7","system":"abc","value":12.5,"unit":"GFLOPS"}""")!;
        var r = await client.SendRunsAsync("k", [run], new Dictionary<string, bool> { ["bench.cpu.multi"] = true }, null, CancellationToken.None);
        Assert.Equal((1, false, 4), (r.Added, r.Pending, r.Lists));
        var sent = JsonNode.Parse(site.Seen[0].Body)!;
        Assert.Equal(12.5, sent["runs"]![0]!["value"]!.GetValue<double>()); Assert.True(sent["higher"]!["bench.cpu.multi"]!.GetValue<bool>()); Assert.Null(sent["marks"]);
    }

    [Fact] public async Task A_shared_result_goes_without_a_key_and_comes_back_as_a_link()
    {
        var (client, site) = Client((_, _) => (HttpStatusCode.OK, """{"link":"https://shop.example/?mazesta_share=0123456789abcdef01234567","rows":1,"queued":0}"""));
        var run = JsonNode.Parse("""{"id":"run-00000001","benchmark":"bench.cpu.multi","version":1,"settings":"","part":"Core i7","system":"abc","value":12.5,"unit":"GFLOPS"}""")!;
        var r = await client.ShareAsync([run], new Dictionary<string, string> { ["bench.cpu.multi"] = "پردازنده، همهٔ هسته‌ها" }, new Dictionary<string, bool> { ["bench.cpu.multi"] = true },
            new SiteMachine("Core i7", null, 32, "Windows 11"), "0.8.0", CancellationToken.None);
        Assert.Equal(("https://shop.example/?mazesta_share=0123456789abcdef01234567", 1), (r.Link, r.Rows));
        var seen = Assert.Single(site.Seen); var sent = JsonNode.Parse(seen.Body)!;
        Assert.Null(seen.Key); Assert.Equal("/wp-json/mazesta/v1/share", seen.Path);
        Assert.Equal("Core i7", sent["machine"]!["cpu"]!.GetValue<string>()); Assert.Null(sent["machine"]!["gpu"]);   // no card named: left out, not sent empty
        Assert.Equal("پردازنده، همهٔ هسته‌ها", sent["names"]!["bench.cpu.multi"]!.GetValue<string>());
    }

    [Fact] public void Only_the_site_s_own_key_counts_as_a_key()
    {
        Assert.True(SiteClient.IsKey("mz_" + new string('a', 48)));
        Assert.False(SiteClient.IsKey("mz_" + new string('a', 47))); Assert.False(SiteClient.IsKey("mz_" + new string('G', 48))); Assert.False(SiteClient.IsKey(null));
        Assert.False(SiteClient.IsKey("MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg"));   // the start of a PKCS#8 private key, as pasted once by mistake
    }

    [Fact] public void The_key_is_found_inside_what_was_pasted()
    {
        string key = "mz_" + new string('0', 47) + "f";
        Assert.Equal(key, SiteClient.FindKey("\u200e " + key + "\n")); Assert.Equal(key, SiteClient.FindKey("کلید سایت: " + key + " ."));
        Assert.Null(SiteClient.FindKey("mz_123")); Assert.Null(SiteClient.FindKey(key + "ab")); Assert.Null(SiteClient.FindKey(null));
    }

    [Fact] public async Task Pairing_sends_the_secret_s_hash_first_and_the_secret_only_to_claim_the_key()
    {
        string secret = new('5', 64), id = Sha(secret);
        var (client, site) = Client((req, _) => req.RequestUri!.AbsolutePath.EndsWith("pair/start", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, $$"""{"url":"https://shop.example/wp-admin/admin.php?page=mazesta-connect&pair={{id}}","code":"{{id[..6].ToUpperInvariant()}}","seconds":600}""")
            : (HttpStatusCode.OK, """{"state":"waiting"}"""));
        var start = await client.PairStartAsync(secret, "SHOP-PC", CancellationToken.None);
        Assert.Equal((id[..6].ToUpperInvariant(), 600), (start.Code, start.Seconds));
        Assert.Equal(id, JsonNode.Parse(site.Seen[0].Body)!["id"]!.GetValue<string>()); Assert.DoesNotContain(secret, site.Seen[0].Body);
        var claim = await client.PairClaimAsync(secret, CancellationToken.None);
        Assert.Equal(("waiting", (string?)null), (claim.State, claim.Key)); Assert.Contains(secret, site.Seen[1].Body);
    }

    [Fact] public async Task The_lists_are_fetched_where_they_changed_and_a_damaged_one_is_refused()
    {
        string list = """{"key":"bench.cpu.multi@1","entries":[]}""", dir = Path.Combine(Path.GetTempPath(), "mz-site-" + Guid.NewGuid().ToString("N"));
        string served = list;
        var (client, site) = Client((req, _) => req.RequestUri!.AbsolutePath.EndsWith("bench/index", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, $$"""{"built":"2026-10-03T10:00:00Z","files":[{"file":"benchdb/bench.cpu.multi-v1.json","size":{{Encoding.UTF8.GetByteCount(list)}},"sha256":"{{Sha(list)}}"}]}""")
            : (HttpStatusCode.OK, served));
        try
        {
            var first = await client.SyncListsAsync(dir, CancellationToken.None);
            Assert.Equal((1, 1), (first.Downloaded, first.Total)); Assert.Equal(list, File.ReadAllText(Path.Combine(dir, "bench.cpu.multi-v1.json")));
            Assert.Equal(0, (await client.SyncListsAsync(dir, CancellationToken.None)).Downloaded);   // unchanged: only the index is read again
            Assert.Equal(3, site.Seen.Count);

            File.Delete(Path.Combine(dir, "bench.cpu.multi-v1.json")); served = list.Replace("[]", "[ ]");
            await Assert.ThrowsAsync<InvalidDataException>(() => client.SyncListsAsync(dir, CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(dir, "bench.cpu.multi-v1.json")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact] public async Task An_index_naming_a_file_outside_the_folder_is_refused()
    {
        var (client, _) = Client((_, _) => (HttpStatusCode.OK, $$"""{"files":[{"file":"../evil.json","size":3,"sha256":"{{Sha("x")}}"}]}"""));
        await Assert.ThrowsAsync<SiteException>(() => client.ListsAsync(CancellationToken.None));
    }
}
