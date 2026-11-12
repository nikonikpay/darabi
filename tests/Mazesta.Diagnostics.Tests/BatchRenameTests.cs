using Mazesta.Core.Windows; using Mazesta.Diagnostics.Windows; using Xunit;
namespace Mazesta.Diagnostics.Tests;

public class BatchRenameTests
{
    [Fact] public void Names_can_swap_and_the_last_batch_is_undone_after_a_restart()
    {
        string dir = Directory.CreateTempSubdirectory("mz-ren").FullName, log = Path.Combine(dir, "undo", "last.json");
        try
        {
            string a = Path.Combine(dir, "x 02.txt"), b = Path.Combine(dir, "x 01.txt");
            File.WriteAllText(a, "two"); File.WriteAllText(b, "one");
            var plan = RenamePlan.Make([a, b], new RenameRule("", "x", Number: true), File.Exists);
            var r = BatchRename.Apply(plan, log);
            Assert.Equal(2, r.Renamed); Assert.Empty(r.Failed);
            Assert.Equal("two", File.ReadAllText(Path.Combine(dir, "x 01.txt"))); Assert.Equal("one", File.ReadAllText(Path.Combine(dir, "x 02.txt")));
            Assert.Equal(2, BatchRename.Read(log).Count);
            var u = BatchRename.Undo(log);
            Assert.Equal(2, u.Renamed); Assert.Equal("two", File.ReadAllText(a)); Assert.Equal("one", File.ReadAllText(b)); Assert.False(File.Exists(log));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact] public void A_files_checksums_are_read_in_one_pass()
    {
        string f = Path.GetTempFileName();
        try
        {
            File.WriteAllText(f, "test");
            var (md5, sha1, sha256) = FileHash.Hash(f, _ => { }, CancellationToken.None);
            Assert.Equal("098f6bcd4621d373cade4e832627b4f6", md5); Assert.Equal("a94a8fe5ccb19ba61c4c0873d391e987982fbbd3", sha1);
            Assert.Equal("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", sha256);
        }
        finally { File.Delete(f); }
    }
}
