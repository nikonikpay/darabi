using System.Diagnostics; using Mazesta.Core.Search; using Mazesta.Hardware.Files; using Xunit; using Xunit.Abstractions;
namespace Mazesta.Hardware.Tests;

/// <summary>The system drive's table read and its journal followed. Needs an elevated test run (the volume is opened directly).</summary>
[Trait("Category", "Hardware")]
public class DriveIndexHardwareTests(ITestOutputHelper output)
{
    private static int Find(FileIndex x, string q, string path)
    {
        var best = new List<(long Key, int Entry)>(); x.Search(FileQuery.Parse(q)!, 200, best);
        return best.Select(b => b.Entry).FirstOrDefault(e => string.Equals(x.PathOf(e), path, StringComparison.OrdinalIgnoreCase), -1);
    }

    [Fact] public void The_system_drive_is_read_from_its_table_and_followed_through_its_journal()
    {
        string root = Path.GetPathRoot(Environment.SystemDirectory)!;
        using var drive = NtfsDrive.TryOpen(root, out var why) ?? throw new InvalidOperationException(why);
        var clock = Stopwatch.StartNew(); var x = drive.Build(); long built = clock.ElapsedMilliseconds;
        clock.Restart(); var best = new List<(long Key, int Entry)>(); int hits = x.Search(FileQuery.Parse("notepad.exe")!, 100, best); long searched = clock.ElapsedMilliseconds;
        GC.Collect(); clock.Restart(); x.Search(FileQuery.Parse("report")!, 100, []); long again = clock.ElapsedMilliseconds;
        output.WriteLine($"{x.Count} names in {built} ms, {GC.GetTotalMemory(true) / 1048576} MB managed after collecting; 'notepad.exe' {hits} hits in {searched} ms (first), 'report' in {again} ms");
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Assert.True(Find(x, "cmd.exe", cmd) > 0, "a hard-linked System32 file is found under its System32 name");
        foreach (var (_, e) in best.OrderBy(b => b.Key).Take(6)) output.WriteLine("  " + x.PathOf(e));
        Assert.True(x.Count > 50_000);
        Assert.True(Find(x, "notepad.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe")) > 0);

        string dir = Directory.CreateTempSubdirectory("mz-idx").FullName, a = Path.Combine(dir, "mazesta-index-probe-1.txt"), b = Path.Combine(dir, "mazesta-index-probe-2.txt");
        try
        {
            File.WriteAllText(a, "x"); Assert.True(drive.CatchUp(x)); Assert.True(Find(x, "mazesta-index-probe-1", a) > 0);
            File.Move(a, b); Assert.True(drive.CatchUp(x)); Assert.Equal(-1, Find(x, "mazesta-index-probe-1", a)); Assert.True(Find(x, "mazesta-index-probe-2", b) > 0);
            File.Delete(b); Assert.True(drive.CatchUp(x)); Assert.Equal(-1, Find(x, "mazesta-index-probe-2", b));
        }
        finally { Directory.Delete(dir, true); }
    }
}
