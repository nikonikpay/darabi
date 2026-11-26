using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Xunit;
using Mazesta.Desk;
namespace Mazesta.Desk.Tests;

/// <summary>
/// Two desks on one real site (a local WordPress, never the live one): both open a customer, one loses the line and saves, the other saves meanwhile, the line comes back. Runs only when MAZESTA_DESK_LIVE is
/// "site|userA|passA|userB|passB" (the site's address and two staff users who may change customers); each desk reaches the site through a small proxy that can be cut.
/// </summary>
[Collection("live-site")]
public class LiveSyncTests : IDisposable
{
    private readonly string _dirA = Path.Combine(Path.GetTempPath(), "mzlive-a-" + Guid.NewGuid().ToString("N")), _dirB = Path.Combine(Path.GetTempPath(), "mzlive-b-" + Guid.NewGuid().ToString("N"));
    public LiveSyncTests() { Directory.CreateDirectory(_dirA); Directory.CreateDirectory(_dirB); }
    public void Dispose() { foreach (var d in new[] { _dirA, _dirB }) try { Directory.Delete(d, true); } catch (IOException) { } }

    internal sealed class Proxy : IDisposable
    {
        private readonly HttpListener _l = new(); private readonly HttpClient _c = new(); private readonly string _target;
        public volatile bool Up = true; public int Port { get; } = FreePort();
        public Proxy(string target)
        {
            _target = target.TrimEnd('/'); _l.Prefixes.Add($"http://127.0.0.1:{Port}/"); _l.Start();
            _ = Task.Run(async () =>
            {
                while (_l.IsListening)
                {
                    HttpListenerContext ctx; try { ctx = await _l.GetContextAsync(); } catch (Exception e) when (e is HttpListenerException or ObjectDisposedException) { break; }
                    _ = Task.Run(async () =>
                    {
                        if (!Up) { ctx.Response.Abort(); return; }
                        try
                        {
                            using var req = new HttpRequestMessage(new HttpMethod(ctx.Request.HttpMethod), _target + ctx.Request.Url!.PathAndQuery);
                            foreach (var h in new[] { "X-MZC-Token", "X-MZC-Op", "X-MZC-Gzip" }) if (ctx.Request.Headers[h] is { } v) req.Headers.TryAddWithoutValidation(h, v);
                            if (ctx.Request.HasEntityBody) { using var ms = new MemoryStream(); await ctx.Request.InputStream.CopyToAsync(ms); req.Content = new ByteArrayContent(ms.ToArray()); req.Content.Headers.TryAddWithoutValidation("Content-Type", ctx.Request.ContentType); }
                            using var res = await _c.SendAsync(req); byte[] b = await res.Content.ReadAsByteArrayAsync();
                            ctx.Response.StatusCode = (int)res.StatusCode; ctx.Response.ContentType = "application/json";
                            if (res.Headers.TryGetValues("X-MZC-Conflict", out var cv)) ctx.Response.Headers["X-MZC-Conflict"] = cv.First();
                            await ctx.Response.OutputStream.WriteAsync(b); ctx.Response.Close();
                        }
                        catch (Exception) { ctx.Response.Abort(); }
                    });
                }
            });
        }
        public string Url => $"http://127.0.0.1:{Port}";
        public void Dispose() { _l.Stop(); _c.Dispose(); }
    }

    private static int FreePort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }
    private static string S(JsonElement? d, params string[] path) { var e = d!.Value; foreach (var p in path) e = e.GetProperty(p); return e.ToString(); }

    [Fact]
    public async Task Two_desks_changing_one_record_offline_both_stay_in_its_history_and_the_later_one_is_the_record()
    {
        string[] cfg = (Environment.GetEnvironmentVariable("MAZESTA_DESK_LIVE") ?? "").Split('|');
        if (cfg.Length != 5) return;   // no local site given: nothing to run against
        var ct = CancellationToken.None;
        using var pa = new Proxy(cfg[0]); using var pb = new Proxy(cfg[0]); using var a = new SiteSession(_dirA); using var b = new SiteSession(_dirB);
        Assert.True(a.SetServer(pa.Url)); Assert.True(b.SetServer(pb.Url));
        Assert.Equal(200, (await a.LoginAsync(cfg[1], cfg[2], ct)).Status); Assert.Equal(200, (await b.LoginAsync(cfg[3], cfg[4], ct)).Status);

        string mobile = "0936" + Random.Shared.Next(1000000, 9999999);
        var made = await a.CallAsync("POST", "staff/customers", JsonSerializer.Serialize(new { name = "مشتری همگام زنده", mobile, address = "اولیه", notes = "اولیه" }), ct); Assert.Equal(200, made.Status);
        var rec = made.Data!.Value.TryGetProperty("customer", out var cu) ? cu : made.Data!.Value;   // the save answers the record itself, a read wraps it
        string id = rec.GetProperty("id").ToString(), u0 = rec.GetProperty("updated").ToString();
        var open = await b.CallAsync("GET", "staff/customers/" + id, null, ct); Assert.Equal("اولیه", S(open.Data, "customer", "address"));   // B has it open

        // B loses the line and saves: it waits, and B's own page still shows the change
        pb.Up = false;
        var q = await b.CallAsync("POST", "staff/customers", JsonSerializer.Serialize(new { id = long.Parse(id), updated = u0, name = "مشتری همگام زنده", mobile, address = "نشانی B (آفلاین)", notes = "یادداشت B" }), ct);
        Assert.Equal(202, q.Status); Assert.Equal(1, b.Queue.Pending);
        var own = await b.CallAsync("GET", "staff/customers/" + id, null, ct); Assert.Equal(200, own.Status); Assert.Equal("نشانی B (آفلاین)", S(own.Data, "customer", "address")); Assert.True(own.Data!.Value.GetProperty("pending").GetBoolean());

        // A saves later, online
        await Task.Delay(1500);
        Assert.Equal(200, (await a.CallAsync("POST", "staff/customers", JsonSerializer.Serialize(new { id = long.Parse(id), updated = u0, name = "مشتری همگام زنده", mobile, address = "نشانی A (آنلاین، جدیدتر)", notes = "اولیه" }), ct)).Status);

        // the line comes back: B's older change is not refused, not applied, and B is told
        pb.Up = true; var f = await b.FlushAsync(ct); Assert.Equal((1, 0, false), f); Assert.Equal(0, b.Queue.Pending);
        var told = b.TakeConflicts(); Assert.Single(told); Assert.Contains("قدیمی‌تر", told[0]);
        var now = await a.CallAsync("GET", "staff/customers/" + id, null, ct); Assert.Equal("نشانی A (آنلاین، جدیدتر)", S(now.Data, "customer", "address"));
        var hist = (await a.CallAsync("GET", $"staff/revisions?entity=customer&id={id}", null, ct)).Data!.Value.GetProperty("items").EnumerateArray().ToList();
        var theirs = Assert.Single(hist, v => v.GetProperty("offline").GetBoolean()); Assert.False(theirs.GetProperty("applied").GetBoolean());

        // A takes only B's note from the history; the address stays A's
        int rev = theirs.GetProperty("id").GetInt32();
        var cmp = (await a.CallAsync("GET", "staff/revisions/compare?rev=" + rev, null, ct)).Data!.Value.GetProperty("fields").EnumerateArray().Select(x => x.GetProperty("key").GetString()).ToList(); Assert.Contains("notes", cmp); Assert.Contains("address", cmp);
        Assert.Equal(200, (await a.CallAsync("POST", "staff/revisions/pick", JsonSerializer.Serialize(new { rev, fields = new[] { "notes" } }), ct)).Status);
        var picked = await a.CallAsync("GET", "staff/customers/" + id, null, ct); Assert.Equal("نشانی A (آنلاین، جدیدتر)", S(picked.Data, "customer", "address")); Assert.Equal("یادداشت B", S(picked.Data, "customer", "notes"));

        // the other way round: B presses save offline, A saves online BEFORE that moment's later... B's change is the later one and becomes the record, A's stays in the history
        string u1 = S(picked.Data, "customer", "updated");
        Assert.Equal(200, (await a.CallAsync("POST", "staff/customers", JsonSerializer.Serialize(new { id = long.Parse(id), updated = u1, name = "مشتری همگام زنده", mobile, address = "نشانی A دوم", notes = "یادداشت B" }), ct)).Status);
        pb.Up = false; await Task.Delay(1500);
        Assert.Equal(202, (await b.CallAsync("POST", "staff/customers", JsonSerializer.Serialize(new { id = long.Parse(id), updated = u1, name = "مشتری همگام زنده", mobile, address = "نشانی B دوم (دیرتر)", notes = "یادداشت B" }), ct)).Status);
        pb.Up = true; Assert.Equal((1, 0, false), await b.FlushAsync(ct)); Assert.Contains("دیرتر بود", Assert.Single(b.TakeConflicts()));
        var latest = await a.CallAsync("GET", "staff/customers/" + id, null, ct); Assert.Equal("نشانی B دوم (دیرتر)", S(latest.Data, "customer", "address"));

        // search with no line: B's own copy of the products answers
        await b.WarmAsync(ct); pb.Up = false;
        var offlineSearch = await b.CallAsync("GET", "staff/products?q=" + Uri.EscapeDataString("ssd") + "&per=12", null, ct);
        Assert.Equal(200, offlineSearch.Status); Assert.True(offlineSearch.Data!.Value.GetProperty("offline").GetBoolean()); Assert.True(offlineSearch.Data!.Value.GetProperty("total").GetInt32() >= 1);
    }
}
