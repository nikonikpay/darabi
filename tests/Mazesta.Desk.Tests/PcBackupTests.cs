using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;
using Mazesta.Desk;
namespace Mazesta.Desk.Tests;

/// <summary>The PC backup against a real HTTP listener standing for the site: a file is written and old ones pruned, a name the site made up is refused, a relative folder is refused.</summary>
public class PcBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mzbk-" + Guid.NewGuid().ToString("N"));
    public PcBackupTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static int FreePort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }

    [Fact]
    public async Task A_backup_is_written_to_the_folder_old_ones_are_pruned_and_a_made_up_name_is_refused()
    {
        int port = FreePort(); using var site = new SiteSession(_dir); Assert.True(site.SetServer($"http://127.0.0.1:{port}"));
        string name = "mazesta-crm-20261125-100000.sql.gz"; using var http = new HttpListener(); http.Prefixes.Add($"http://127.0.0.1:{port}/"); http.Start();
        var serve = Task.Run(async () => { while (http.IsListening) { try { var c = await http.GetContextAsync(); byte[] b = Encoding.UTF8.GetBytes($"{{\"name\":\"{name.Replace("\\", "\\\\")}\",\"size\":3,\"data\":\"{Convert.ToBase64String(new byte[] { 1, 2, 3 })}\"}}"); c.Response.ContentType = "application/json"; await c.Response.OutputStream.WriteAsync(b); c.Response.Close(); } catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { break; } } });
        string folder = Path.Combine(_dir, "bk"); var bk = new PcBackup(_dir, site);
        Assert.NotEqual("", bk.Set("relative", 2));
        Assert.Equal("", bk.Set(folder, 2));
        Assert.Equal("", await bk.RunAsync(CancellationToken.None)); Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(folder, name)));
        // older files beyond `keep` go
        File.WriteAllBytes(Path.Combine(folder, "mazesta-crm-20261101-010101.sql.gz"), [0]); File.WriteAllBytes(Path.Combine(folder, "mazesta-crm-20261102-010101.sql.gz"), [0]);
        Assert.Equal("", await bk.RunAsync(CancellationToken.None)); Assert.Equal(2, Directory.GetFiles(folder, "mazesta-crm-*.sql.gz").Length); Assert.False(File.Exists(Path.Combine(folder, "mazesta-crm-20261101-010101.sql.gz")));
        // a name that is not ours never reaches the disk
        name = "..\\evil.txt"; Assert.NotEqual("", await bk.RunAsync(CancellationToken.None)); Assert.False(File.Exists(Path.Combine(_dir, "evil.txt")));
        http.Stop();
    }
}
