using Xunit; using Mazesta.Core.Windows;
namespace Mazesta.Core.Tests;

public class FileToolsTests
{
    [Theory, InlineData(1, FileLockKind.Window), InlineData(2, FileLockKind.Window), InlineData(3, FileLockKind.Service), InlineData(4, FileLockKind.Explorer), InlineData(5, FileLockKind.Console), InlineData(1000, FileLockKind.Critical), InlineData(0, FileLockKind.Other)]
    public void Restart_managers_app_types_map_to_kinds(int type, FileLockKind kind) => Assert.Equal(kind, FileLockHolder.KindOf(type));

    [Fact] public void A_service_a_critical_process_or_the_system_is_never_closed()
    {
        Assert.False(new FileLockHolder(900, 1, "svc", null, FileLockKind.Service).CanClose);
        Assert.False(new FileLockHolder(900, 1, "csrss", null, FileLockKind.Critical).CanClose);
        Assert.False(new FileLockHolder(4, 1, "System", null, FileLockKind.Other).CanClose);
        Assert.True(new FileLockHolder(900, 1, "Word", null, FileLockKind.Window).CanClose);
    }

    [Fact] public void A_folders_size_is_everything_under_it_biggest_first_and_an_unread_folder_marks_its_parents()
    {
        var root = new SpaceNode(@"C:\"); var users = root.Add("Users"); var win = root.Add("Windows"); var me = users.Add("me"); var locked = users.Add("Locked");
        me.AddFile(700); me.AddFile(300); win.AddFile(500); root.AddFile(10); locked.Unread = true;
        root.Seal();
        Assert.Equal((1510L, 4L), (root.Size, root.Files)); Assert.Equal(1000, users.Size);
        Assert.Equal(["Users", "Windows"], root.Children.Select(c => c.Name));
        Assert.True(users.PartUnread); Assert.True(root.PartUnread); Assert.False(win.PartUnread); Assert.True(locked.Unread); Assert.False(locked.PartUnread);
        Assert.Same(me, root.Find(["users", "ME"])); Assert.Null(root.Find(["Users", "nobody"]));
        Assert.Equal(@"C:\Users\me", me.FullPath);
    }

    [Fact] public void Only_the_biggest_files_are_kept()
    {
        var top = new TopFiles(2);
        foreach (var (p, s) in new[] { ("a", 5L), ("b", 50L), ("c", 1L), ("d", 20L) }) top.Add(p, s);
        Assert.Equal(["b", "d"], top.Biggest().Select(x => x.Path));
    }
}
