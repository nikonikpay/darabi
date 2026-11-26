using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Mazesta.Desk;
namespace Mazesta.Desk.Tests;

/// <summary>The PC's copy: what the site sends is kept sealed and answers the pages' reads; and (with a local WordPress in MAZESTA_DESK_LIVE) it answers them as the site itself does.</summary>
[Collection("live-site")]   // the tests against one real site run one after another: one's changes are the other's surprise
public class ReplicaTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mzrep-" + Guid.NewGuid().ToString("N"));
    public ReplicaTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static JsonElement J(string s) { using var d = JsonDocument.Parse(s); return d.RootElement.Clone(); }
    private static string Sync(string up, bool reset = false, string drop = "[]", string del = "{}", bool more = false) =>
        $$"""{"cursor":"c1","more":{{(more ? "true" : "false")}},"reset":{{(reset ? "true" : "false")}},"drop":{{drop}},"up":{{up}},"del":{{del}},"kinds":["customers","products","builds"],"seq":7,"head":7,"epoch":"e","flag":""}""";

    [Fact]
    public void What_the_site_sends_answers_the_lists_sealed_on_disk_and_ready_only_when_whole()
    {
        string file = Path.Combine(_dir, "r.db");
        using (var r = new Replica(file))
        {
            r.Own("ali", "http://x");
            r.Apply(J(Sync("""{"customers":[{"id":1,"name":"رضا محمدی","mobile":"09120000001"},{"id":2,"name":"سارا","mobile":"09120000002"}]}""", reset: true, more: true)));
            Assert.False(r.Ready);
            r.Apply(J(Sync("""{"builds":[{"id":5,"title":"RTX 4070","status":"queue","customer_id":1,"parts":[{},{}]}],"products":[{"id":3,"name":"B cpu","stock":0},{"id":4,"name":"A ssd","stock":2}]}""")));
            Assert.True(r.Ready && r.OwnedBy("ali", "http://x"));
            var all = r.Answer("staff/customers", _ => null, null)!.Value; Assert.Equal(2, all.GetProperty("total").GetInt32()); Assert.Equal(2, all.GetProperty("items")[0].GetProperty("id").GetInt32());
            Assert.Equal(1, all.GetProperty("items")[1].GetProperty("builds").GetInt32());
            // Persian digits and the customer's name find the computer; the name shown is the customer's as it is now
            var found = r.Answer("staff/builds?q=" + Uri.EscapeDataString("محمدی ۴۰۷۰"), _ => null, null)!.Value; Assert.Equal(1, found.GetProperty("total").GetInt32());
            Assert.Equal("رضا محمدی", found.GetProperty("items")[0].GetProperty("customer_name").GetString()); Assert.Equal(2, found.GetProperty("items")[0].GetProperty("parts_count").GetInt32());
            Assert.Equal(4, r.Answer("staff/products", _ => null, null)!.Value.GetProperty("items")[0].GetProperty("id").GetInt32());
            Assert.Equal(1, r.Answer("staff/products?instock=1", _ => null, null)!.Value.GetProperty("total").GetInt32());
            Assert.Equal(1, r.Answer("staff/customers/1", _ => null, null)!.Value.GetProperty("builds").GetArrayLength());
            Assert.Null(r.Answer("staff/invoices", _ => null, null));     // a kind this person does not hold is the site's to answer (or refuse)
            Assert.Null(r.Answer("staff/stats", _ => null, null)); Assert.Null(r.Answer("staff/customers/99", _ => null, null));
            r.Written("staff/builds/status", J("""{"id":5,"status":"testing"}""")); var one = r.Answer("staff/builds/5", _ => null, null)!.Value; Assert.Equal("testing", one.GetProperty("status").GetString()); Assert.Equal("RTX 4070", one.GetProperty("title").GetString());
            r.Apply(J(Sync("{}", del: """{"customers":[2]}""", drop: """["products"]"""))); Assert.Equal(1, r.Count("customers")); Assert.Equal(0, r.Count("products"));
        }
        Assert.DoesNotContain("0912", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)));
        using var again = new Replica(file); Assert.True(again.Ready); Assert.Equal(1, again.Answer("staff/customers", _ => null, null)!.Value.GetProperty("total").GetInt32());
        again.Own("sara", "http://x"); Assert.False(again.Ready); Assert.Equal(0, again.Count("customers"));   // another person: nothing of the first's
    }

    [Fact]
    public async Task The_copy_of_a_real_site_answers_as_the_site_does_and_follows_it()
    {
        string[] cfg = (Environment.GetEnvironmentVariable("MAZESTA_DESK_LIVE") ?? "").Split('|');
        if (cfg.Length != 5) return;   // no local site given
        var ct = CancellationToken.None; var log = new List<string>(); var wrong = new List<string>();
        string dirA = Path.Combine(_dir, "a"), dirB = Path.Combine(_dir, "b"); Directory.CreateDirectory(dirA); Directory.CreateDirectory(dirB);
        using var proxy = new LiveSyncTests.Proxy(cfg[0]); using var a = new SiteSession(dirA); using var b = new SiteSession(dirB);
        Assert.True(a.SetServer(proxy.Url)); Assert.True(b.SetServer(cfg[0]));
        Assert.Equal(200, (await a.LoginAsync(cfg[1], cfg[2], ct)).Status); Assert.Equal(200, (await b.LoginAsync(cfg[3], cfg[4], ct)).Status);
        var watch = Stopwatch.StartNew(); Assert.True(await a.SyncAsync(ct), a.State.Error); log.Add($"first copy: {a.State.Received} records in {watch.ElapsedMilliseconds} ms");
        Assert.True(a.State.Ready);
        await a.CallAsync("GET", "staff/config", null, ct); await a.CallAsync("GET", "staff/shortages?per=1", null, ct);   // what the lists hold beside their rows

        var paths = new List<string> { "staff/customers", "staff/customers?page=2&per=5", "staff/customers?q=" + Uri.EscapeDataString("مشتری"), "staff/products", "staff/products?instock=1", "staff/products?q=" + Uri.EscapeDataString("کالا"),
            "staff/builds", "staff/builds?status=queue", "staff/jobs", "staff/jobs?status=received", "staff/invoices", "staff/invoices?kind=sale&status=final", "staff/invoices?kind=template&per=50", "staff/invoices?kind=proforma",
            "staff/payments", "staff/payments?direction=in", "staff/cheques", "staff/ships", "staff/ships?status=open", "staff/shortages", "staff/shortages?status=active", "staff/purchases", "staff/suppliers", "staff/counts", "staff/followups", "staff/invoices?kind=proforma&status=draft&per=50",
            "staff/tasks?box=mine&per=50", "staff/tasks?box=sent&per=50", "staff/tasks?box=review", "staff/tasks?box=done&per=50", "staff/tasks?box=all&per=50", "staff/flows" };
        foreach (var kind in new[] { "customers", "builds", "jobs", "invoices", "purchases", "tasks" })
        {
            var l = await b.CallAsync("GET", "staff/" + kind + "?per=3", null, ct);
            if (l.Status == 200) paths.AddRange(l.Data!.Value.GetProperty("items").EnumerateArray().Select(x => "staff/" + kind + "/" + x.GetProperty("id").GetRawText()));
        }
        proxy.Up = false;   // from here A has no site: every answer is its own copy
        long slowest = 0;
        foreach (var path in paths)
        {
            var theirs = await b.CallAsync("GET", path, null, ct);
            watch.Restart(); var mine = await a.CallAsync("GET", path, null, ct); slowest = Math.Max(slowest, watch.ElapsedMilliseconds);
            if (theirs.Status != 200) continue;
            if (mine.Status != 200) { wrong.Add($"{path}: copy answered {mine.Status} {mine.Error}"); continue; }
            Same(path, "", JsonNode.Parse(theirs.Data!.Value.GetRawText()), JsonNode.Parse(mine.Data!.Value.GetRawText()), wrong);
        }
        log.Add($"{paths.Count} reads answered with the site cut off, slowest {slowest} ms");

        proxy.Up = true;
        string mobile = "0937" + Random.Shared.Next(1000000, 9999999);
        var made = await a.CallAsync("POST", "staff/customers", JsonSerializer.Serialize(new { name = "مشتری نسخهٔ محلی", mobile, notes = new string('ی', 3000) }), ct); Assert.Equal(200, made.Status);   // (large: goes gzipped)
        long id = made.Data!.Value.GetProperty("id").GetInt64();
        var seen = await a.CallAsync("GET", "staff/customers?q=" + mobile, null, ct); Assert.Equal(1, seen.Data!.Value.GetProperty("total").GetInt32());
        var onSite = await b.CallAsync("GET", "staff/customers/" + id, null, ct); Assert.Equal(3000, onSite.Data!.Value.GetProperty("customer").GetProperty("notes").GetString()!.Length);

        // another desk changes it: the minute's check (the static file) brings it
        string stamp = onSite.Data!.Value.GetProperty("customer").GetProperty("updated").GetString()!;
        await Task.Delay(1100);
        Assert.Equal(200, (await b.CallAsync("POST", "staff/customers", JsonSerializer.Serialize(new { id, updated = stamp, name = "نام تازه از کامپیوتر دیگر", mobile }), ct)).Status);
        await a.TickAsync(ct);
        var after = await a.CallAsync("GET", "staff/customers/" + id, null, ct); Assert.Equal("نام تازه از کامپیوتر دیگر", after.Data!.Value.GetProperty("customer").GetProperty("name").GetString());

        // taken for editing while the copy is behind: the site says so and the copy is brought up to date
        stamp = after.Data!.Value.GetProperty("customer").GetProperty("updated").GetString()!; await Task.Delay(1100);
        Assert.Equal(200, (await b.CallAsync("POST", "staff/customers", JsonSerializer.Serialize(new { id, updated = stamp, name = "نام سوم", mobile }), ct)).Status);
        var locked = await a.CallAsync("POST", "staff/lock", JsonSerializer.Serialize(new { entity = "customer", id }), ct); Assert.Equal(200, locked.Status); Assert.True(locked.Data!.Value.TryGetProperty("stale", out _), locked.Data!.Value.GetRawText());
        Assert.Equal("نام سوم", (await a.CallAsync("GET", "staff/customers/" + id, null, ct)).Data!.Value.GetProperty("customer").GetProperty("name").GetString());
        await a.CallAsync("POST", "staff/lock", JsonSerializer.Serialize(new { entity = "customer", id, release = true }), ct);

        if (Environment.GetEnvironmentVariable("MAZESTA_DESK_LOG") is { Length: > 0 } file) File.WriteAllLines(file, log.Concat(wrong));
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(40)));
    }

    /// <summary>Everything the site answered is in the copy's answer with the same value; rows in the same order. (The copy may hold more about a row than the site's list gives.)</summary>
    private static void Same(string path, string at, JsonNode? site, JsonNode? copy, List<string> wrong)
    {
        switch (site)
        {
            case JsonObject o:
                if (copy is not JsonObject c) { wrong.Add($"{path}{at}: not an object"); return; }
                foreach (var (k, v) in o) if (k != "photos") Same(path, at + "." + k, v, c[k], wrong);
                break;
            case JsonArray a:
                if (copy is not JsonArray b || b.Count != a.Count) { wrong.Add($"{path}{at}: {a.Count} on the site, {(copy as JsonArray)?.Count} in the copy"); return; }
                for (int i = 0; i < a.Count; i++) Same(path, $"{at}[{i}]", a[i], b[i], wrong);
                break;
            default:
                string x = site?.ToJsonString() ?? "null", y = copy?.ToJsonString() ?? "null";
                if (x != y && !(site is not null && copy is not null && site.ToString() == copy.ToString())) wrong.Add($"{path}{at}: site {x} copy {y}");
                break;
        }
    }
}
