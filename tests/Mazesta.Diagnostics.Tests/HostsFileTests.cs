using Xunit; using Mazesta.Diagnostics.Windows;
namespace Mazesta.Diagnostics.Tests;

public class HostsFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-hosts-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) { foreach (var f in Directory.EnumerateFiles(_dir)) File.SetAttributes(f, FileAttributes.Normal); Directory.Delete(_dir, true); } }

    private const string Sample = "# comment\r\n\r\n127.0.0.1       localhost\r\n::1 localhost # loopback\r\n10.0.0.5\r\nnot-an-ip  example.com\r\n\t0.0.0.0\tads.example.com\tads2.example.com";

    [Fact] public void Lines_windows_would_ignore_are_found_by_number()
    {
        var bad = HostsFile.Check(Sample);
        Assert.Equal([(5, "name"), (6, "address")], bad.Select(l => (l.Number, l.Problem)));
    }
    [Fact] public void Entries_count_every_active_line() => Assert.Equal(5, HostsFile.Entries(Sample));
    [Fact] public void Saving_keeps_the_previous_file_and_the_read_only_flag()
    {
        Directory.CreateDirectory(_dir); string path = Path.Combine(_dir, "hosts");
        File.WriteAllText(path, "old"); File.SetAttributes(path, FileAttributes.ReadOnly);
        HostsFile.Save(path, "127.0.0.1 a\n127.0.0.1 b");
        Assert.Equal("127.0.0.1 a\r\n127.0.0.1 b", File.ReadAllText(path)); Assert.Equal("old", File.ReadAllText(HostsFile.BackupOf(path)));
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);   // no BOM
    }
}
