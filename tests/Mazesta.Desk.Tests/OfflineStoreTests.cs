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
    [InlineData("POST", "staff/invoices", "{\"id\":3}", false)]
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
    public void The_read_cache_keeps_the_last_answer_and_forgets_on_clear()
    {
        string file = Path.Combine(_dir, "c.json"); var c = new ReadCache(file);
        c.Put("staff/customers?q=a", "{\"n\":1}"); c.Put("staff/customers?q=a", "{\"n\":2}");
        Assert.Equal("{\"n\":2}", new ReadCache(file).Get("staff/customers?q=a")!.Value.Json);
        c.Clear(); Assert.Null(new ReadCache(file).Get("staff/customers?q=a"));
    }
}
