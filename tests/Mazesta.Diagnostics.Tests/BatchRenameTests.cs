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

    [Fact] public void A_file_that_cannot_go_back_stays_in_the_undo_log_until_it_can()
    {
        string dir = Directory.CreateTempSubdirectory("mz-ren").FullName, log = Path.Combine(dir, "undo", "last.json");
        try
        {
            string a = Path.Combine(dir, "a.txt"); File.WriteAllText(a, "mine");
            Assert.Equal(1, BatchRename.Apply(RenamePlan.Make([a], new RenameRule("a", "b"), File.Exists), log).Renamed);
            File.WriteAllText(a, "someone else's");   // the old name taken by a file outside the batch
            var u = BatchRename.Undo(log);
            Assert.Equal(0, u.Renamed); Assert.Equal("taken", Assert.Single(u.Failed).Error); Assert.Single(BatchRename.Read(log));
            File.Delete(a);
            Assert.Equal(1, BatchRename.Undo(log).Renamed); Assert.Equal("mine", File.ReadAllText(a)); Assert.False(File.Exists(log));
        }
        finally { Directory.Delete(dir, true); }
    }
}
