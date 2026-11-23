using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;
using Mazesta.Desk;
namespace Mazesta.Desk.Tests;

/// <summary>The real SiteSession against a real HTTP listener on this PC: the site is down, then up.</summary>
public class SiteSessionOfflineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mzdesk-" + Guid.NewGuid().ToString("N"));
    public SiteSessionOfflineTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static int FreePort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }

    [Fact]
    public async Task A_payment_made_offline_waits_then_goes_once_with_its_own_id_and_a_read_comes_back_old()
    {
        int port = FreePort(); using var site = new SiteSession(_dir); Assert.True(site.SetServer($"http://127.0.0.1:{port}"));
        var ct = CancellationToken.None;
        // site down: a new payment waits, a finalise is refused, a read with no old answer fails
        var q = await site.CallAsync("POST", "staff/payments", "{\"amount\":1000,\"direction\":\"in\"}", ct);
        Assert.Equal(202, q.Status); Assert.Equal(1, site.Queue.Pending);
        var fin = await site.CallAsync("POST", "staff/invoices/finalize", "{\"id\":3}", ct); Assert.Equal(0, fin.Status); Assert.Equal(1, site.Queue.Pending);

        // site up: one read answers (kept), the queue is sent with the call's own id in X-MZC-Op
        var ops = new List<string>();
        using var http = new HttpListener(); http.Prefixes.Add($"http://127.0.0.1:{port}/"); http.Start();
        var serve = Task.Run(async () =>
        {
            while (http.IsListening)
            {
                HttpListenerContext c; try { c = await http.GetContextAsync(); } catch (Exception e) when (e is HttpListenerException or ObjectDisposedException) { return; }
                if (c.Request.HttpMethod == "POST") ops.Add(c.Request.Headers["X-MZC-Op"] ?? "");
                byte[] body = Encoding.UTF8.GetBytes(c.Request.HttpMethod == "POST" ? "{\"id\":9}" : "{\"customers\":[1,2]}");
                c.Response.ContentType = "application/json"; c.Response.StatusCode = 200; await c.Response.OutputStream.WriteAsync(body); c.Response.Close();
            }
        });
        var read = await site.CallAsync("GET", "staff/customers?q=a", null, ct); Assert.Equal(200, read.Status);
        var f = await site.FlushAsync(ct);
        Assert.Equal((1, 0, false), f); Assert.Equal(0, site.Queue.Pending); Assert.Single(ops); Assert.Matches("^[0-9a-f]{32}$", ops[0]);

        // site down again: the same read comes back from the last good answer, marked old
        http.Stop();
        var old = await site.CallAsync("GET", "staff/customers?q=a", null, ct);
        Assert.Equal(200, old.Status); Assert.True(old.Data!.Value.GetProperty("offline").GetBoolean()); Assert.Equal(2, old.Data!.Value.GetProperty("customers").GetArrayLength());
        await serve;
    }
}
