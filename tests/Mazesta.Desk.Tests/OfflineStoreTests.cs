using Xunit;
using Mazesta.Desk;
namespace Mazesta.Desk.Tests;

public class OfflineStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mzdesk-" + Guid.NewGuid().ToString("N"));
    public OfflineStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Theory]
    [InlineData("POST", "staff/payments", "{\"amount\":5}", true)]
    [InlineData("POST", "staff/payments", "{\"id\":0,\"amount\":5}", true)]
    [InlineData("POST", "staff/payments", "{\"id\":7,\"amount\":5}", false)]       // changing an old payment needs the site (it may have been changed meanwhile)
    [InlineData("POST", "staff/invoices", "{\"title\":\"x\"}", true)]
    [InlineData("POST", "staff/invoices", "{\"id\":3}", true)]                      // a change of a draft: both versions stay in its history
    [InlineData("POST", "staff/customers", "{\"id\":4,\"name\":\"x\"}", true)]
    [InlineData("POST", "staff/products", "{\"id\":4,\"name\":\"x\"}", true)]
    [InlineData("POST", "staff/builds", "{\"id\":4}", true)]
    [InlineData("POST", "staff/jobs", "{}", true)]
    [InlineData("POST", "staff/ships", "{\"id\":2}", true)]
    [InlineData("POST", "staff/shortages", "{\"id\":2,\"status\":\"bought\"}", true)]
    [InlineData("POST", "staff/purchases", "{\"id\":2}", false)]                    // a purchase moves the stock
    [InlineData("POST", "staff/builds/status", "{\"id\":2}", false)]
    [InlineData("POST", "staff/invoices/finalize", "{\"id\":3}", false)]            // numbering is the site's
    [InlineData("POST", "staff/invoices/void", "{}", false)]
    [InlineData("POST", "staff/payments/delete", "{\"id\":1}", false)]
    [InlineData("GET", "staff/payments", "", false)]
    public void Only_a_new_payment_or_a_draft_invoice_may_wait(string method, string path, string body, bool expected) => Assert.Equal(expected, OfflineQueue.Queueable(method, path, body));

    [Fact]
    public async Task Calls_survive_a_restart_go_in_order_and_leave_once_sent()
    {
        string file = Path.Combine(_dir, "q.json");
        var q = new OfflineQueue(file); var a = q.Add("staff/payments", "{\"amount\":1}"); var b = q.Add("staff/invoices", "{\"title\":\"b\"}");
        var again = new OfflineQueue(file); Assert.Equal(2, again.Pending);
        var seen = new List<string>();
        var r = await again.FlushAsync(c => { seen.Add(c.Id); return Task.FromResult((200, (string?)null)); });
        Assert.Equal([a.Id, b.Id], seen); Assert.Equal((2, 0, false), r); Assert.Equal(0, again.Pending); Assert.Equal(0, new OfflineQueue(file).Pending);
    }

    [Fact]
    public async Task A_call_goes_out_only_under_the_person_who_made_it()
    {
        var q = new OfflineQueue(Path.Combine(_dir, "q.json")); var a = q.Add("staff/payments", "{}", "ali"); var b = q.Add("staff/payments", "{}", "sara"); var old = q.Add("staff/payments", "{}");
        var seen = new List<string>();
        var r = await q.FlushAsync(c => { seen.Add(c.Id); return Task.FromResult((200, (string?)null)); }, "sara");
        Assert.Equal([b.Id, old.Id], seen); Assert.Equal((2, 0, false), r); Assert.Equal(1, q.Pending);
        await q.FlushAsync(c => { seen.Add(c.Id); return Task.FromResult((200, (string?)null)); }, "ali");
        Assert.Equal(a.Id, seen[^1]); Assert.Equal(0, q.Pending);
    }

    [Fact]
    public async Task A_dead_line_stops_the_run_and_keeps_every_call_in_order()
    {
        var q = new OfflineQueue(Path.Combine(_dir, "q.json")); q.Add("staff/payments", "{}"); q.Add("staff/payments", "{}");
        int tries = 0;
        var r = await q.FlushAsync(_ => { tries++; return Task.FromResult((0, (string?)"offline")); });
        Assert.Equal(1, tries); Assert.True(r.Stopped); Assert.Equal(2, q.Pending);
    }

    [Fact]
    public async Task A_refusal_is_kept_and_shown_and_does_not_block_the_next_call()
    {
        var q = new OfflineQueue(Path.Combine(_dir, "q.json")); var bad = q.Add("staff/payments", "{}"); q.Add("staff/payments", "{}");
        int n = 0;
        var r = await q.FlushAsync(_ => Task.FromResult(++n == 1 ? (422, (string?)"مبلغ را درست بنویسید.") : (200, (string?)null)));
        Assert.Equal((1, 1, false), r); Assert.Equal(0, q.Pending); Assert.Equal(bad.Id, Assert.Single(q.Refused).Id);
        q.Discard(bad.Id); Assert.Empty(q.Refused);
    }

    [Fact]
    public void The_local_store_keeps_the_last_answer_and_forgets_on_clear()
    {
        string file = Path.Combine(_dir, "c.db");
        using (var c = new LocalStore(file)) { c.Put("staff/customers?q=a", "{\"n\":1}"); c.Put("staff/customers?q=a", "{\"n\":2}"); }
        using (var again = new LocalStore(file)) { Assert.Equal("{\"n\":2}", again.Get("staff/customers?q=a")!.Value.Json); again.Clear(); Assert.Null(again.Get("staff/customers?q=a")); }
        string f2 = Path.Combine(_dir, "sealed.db");
        using (var s = new LocalStore(f2)) s.Put("staff/x", "{\"secret\":\"SECRETMARK\"}");
        Assert.DoesNotContain("SECRETMARK", File.ReadAllText(f2, System.Text.Encoding.Latin1), StringComparison.Ordinal);   // the answer itself is sealed on disk
    }

    [Fact]
    public void A_search_is_answered_from_the_kept_copy_with_persian_digits_and_paging_and_survives_a_restart()
    {
        string file = Path.Combine(_dir, "s.db");
        using (var s = new LocalStore(file))
        {
            using var d = System.Text.Json.JsonDocument.Parse("[{\"id\":1,\"name\":\"Ryzen 5 7600\",\"brand\":\"AMD\",\"stock\":3},{\"id\":2,\"name\":\"کارت گرافیک RTX 4070\",\"brand\":\"MSI\",\"stock\":0},{\"id\":3,\"name\":\"SSD 1TB\",\"brand\":\"WD\",\"stock\":9}]");
            s.Upsert("products", d.RootElement); Assert.Equal(3, s.Count("products"));
        }
        using var again = new LocalStore(file);
        var all = again.List("products", "per=2&page=1")!.Value; Assert.Equal(3, all.GetProperty("total").GetInt32()); Assert.Equal(2, all.GetProperty("items").GetArrayLength()); Assert.True(all.GetProperty("offline").GetBoolean());
        Assert.Equal(1, again.List("products", "q=" + Uri.EscapeDataString("۴۰۷۰"))!.Value.GetProperty("items").GetArrayLength());                 // Persian digits find the Latin number
        Assert.Equal(2, again.List("products", "q=rtx&per=5")!.Value.GetProperty("items")[0].GetProperty("id").GetInt32());
        Assert.Equal(0, again.List("products", "q=ryzen+4070")!.Value.GetProperty("total").GetInt32());                                         // every word must be there
        Assert.Equal(1, again.List("products", "brand=WD")!.Value.GetProperty("total").GetInt32());                                             // a parameter naming a field filters on it
        Assert.Null(again.List("customers", ""));                                                                                               // nothing kept: the caller says so
    }
}
