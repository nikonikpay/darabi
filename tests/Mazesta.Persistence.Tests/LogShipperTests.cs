using System.Text; using System.Text.Json.Nodes; using Mazesta.Persistence; using Mazesta.Persistence.Updates; using Xunit;
namespace Mazesta.Persistence.Tests;

public sealed class LogShipperTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-logsend-" + Guid.NewGuid().ToString("N"));
    private string Logs => Path.Combine(_dir, "logs"); private string Config => Path.Combine(_dir, "config");
    private string Today => Path.Combine(Logs, "mazesta-web-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
    public LogShipperTests() { Directory.CreateDirectory(Logs); Directory.CreateDirectory(Config); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact] public void Only_whole_new_lines_are_sent_and_only_once_committed()
    {
        File.WriteAllText(Today, "10:00 INF one\n10:01 WRN two\n10:02 INF hal");
        var s = new LogShipper(Logs, Config);
        var p = s.Next()!; Assert.Equal("10:00 INF one\n10:01 WRN two\n", p.Text);
        Assert.Equal(p.Text, s.Next()!.Text);   // not committed: the same piece again
        p.Commit(); Assert.Null(s.Next());       // the half line waits
        File.AppendAllText(Today, "f\n10:03 ERR three\n");
        Assert.Equal("10:02 INF half\n10:03 ERR three\n", s.Next()!.Text);
    }

    [Fact] public void The_profile_folder_and_the_computer_name_are_taken_out()
    {
        string text = LogShipper.Scrub($"saved {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\\x.txt on {Environment.MachineName}");
        Assert.Equal("saved %USERPROFILE%\\x.txt on %COMPUTERNAME%", text);
    }

    /// <summary>A real send to a running Mazesta Connect (the local test WordPress): set MAZESTA_TEST_SITE to its mazesta/v1/ address and MAZESTA_TEST_INSTALL to a system it knows.</summary>
    [Fact] public async Task A_piece_reaches_a_real_site_when_one_is_given()
    {
        if (Environment.GetEnvironmentVariable("MAZESTA_TEST_SITE") is not { Length: > 0 } site || Environment.GetEnvironmentVariable("MAZESTA_TEST_INSTALL") is not { Length: 32 } install) return;
        File.WriteAllText(Today, $"10:00 INF Mazesta Web on {Environment.MachineName}\n", Encoding.UTF8);
        var p = new LogShipper(Logs, Config).Next()!;
        using var http = new HttpClient();
        await new SiteClient(new Uri(site), http).SendLogsAsync(new JsonObject { ["install"] = install, ["app"] = "test", ["file"] = p.File, ["text"] = p.Text, ["crashes"] = p.Crashes }.ToJsonString(), CancellationToken.None);
        p.Commit();
    }
}
