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

    [Fact]
    public async Task Without_the_site_only_the_last_person_gets_in_with_the_same_password_and_is_signed_in_again_when_it_answers()
    {
        int port = FreePort(); var ct = CancellationToken.None; var sent = new List<string>(); string token = new('a', 64); bool passwordChanged = false;
        HttpListener Up()
        {
            var http = new HttpListener(); http.Prefixes.Add($"http://127.0.0.1:{port}/"); http.Start();
            _ = Task.Run(async () =>
            {
                while (http.IsListening)
                {
                    HttpListenerContext c; try { c = await http.GetContextAsync(); } catch (Exception e) when (e is HttpListenerException or ObjectDisposedException) { return; }
                    string path = c.Request.Url!.AbsolutePath; bool login = path.EndsWith("/staff/login", StringComparison.Ordinal), ok = login ? !passwordChanged : c.Request.Headers["X-MZC-Token"] == token;
                    if (ok && !login) sent.Add(c.Request.HttpMethod + " " + path[(path.IndexOf("staff/", StringComparison.Ordinal) + 6)..]);
                    byte[] body = Encoding.UTF8.GetBytes(!ok ? "{\"error\":\"no\"}" : login ? "{\"token\":\"" + token + "\",\"name\":\"Ali\"}" : "{\"ok\":true}");
                    c.Response.ContentType = "application/json"; c.Response.StatusCode = ok ? 200 : 401; await c.Response.OutputStream.WriteAsync(body); c.Response.Close();
                }
            });
            return http;
        }
        var up = Up();
        using (var first = new SiteSession(_dir)) { Assert.True(first.SetServer($"http://127.0.0.1:{port}")); Assert.Equal(200, (await first.LoginAsync("Ali", "right-pass", ct)).Status); Assert.Equal(200, (await first.CallAsync("GET", "staff/config", null, ct)).Status); }
        up.Close();

        using var site = new SiteSession(_dir);   // a restart with no line: the kept sign-in opens the program
        Assert.True(site.SignedIn); Assert.Equal("Ali", (await site.CallAsync("GET", "staff/me", null, ct)).Data!.Value.GetProperty("name").GetString());
        await site.LogoutAsync(ct); Assert.False(site.SignedIn);
        Assert.Equal(401, (await site.CallAsync("GET", "staff/config", null, ct)).Status);                    // signed out: nothing kept is served
        Assert.Equal(401, (await site.LoginAsync("ali", "wrong", ct)).Status); Assert.Equal(0, (await site.LoginAsync("sara", "right-pass", ct)).Status); Assert.False(site.SignedIn);
        var inside = await site.LoginAsync(" ALI ", "right-pass", ct); Assert.Equal(200, inside.Status); Assert.True(inside.Data!.Value.GetProperty("offline").GetBoolean()); Assert.False(inside.Data!.Value.TryGetProperty("token", out _));
        Assert.Equal(200, (await site.CallAsync("GET", "staff/config", null, ct)).Status);
        Assert.Equal(202, (await site.CallAsync("POST", "staff/payments", "{\"amount\":1}", ct)).Status);
        Assert.DoesNotContain("right-pass", File.ReadAllText(Path.Combine(_dir, "desk.json")));

        sent.Clear(); up = Up();   // the line is back: the waiting payment signs in by itself and goes
        Assert.Equal((1, 0, false), await site.FlushAsync(ct)); Assert.Equal(["POST payments"], sent);
        await site.LogoutAsync(ct);

        // a password the site no longer takes stops working without the site too
        passwordChanged = true; Assert.Equal(401, (await site.LoginAsync("ali", "right-pass", ct)).Status); up.Close();
        Assert.Equal(0, (await site.LoginAsync("ali", "right-pass", ct)).Status); Assert.False(site.SignedIn);
    }
}
