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

    [Theory, InlineData(4000, 3000, 1280, 1280, 960), InlineData(3000, 4000, 1280, 960, 1280), InlineData(800, 600, 1280, 800, 600), InlineData(10000, 10, 800, 800, 1)]
    public void A_picture_is_brought_down_to_its_longest_side_never_up(int w, int h, int max, int nw, int nh) => Assert.Equal((nw, nh), ImageFit.Fit(w, h, max));

    [Fact] public void The_new_picture_never_writes_over_a_file()
    {
        var taken = new HashSet<string> { Path.Combine("d", "a (800).jpg"), Path.Combine("d", "a (800) (2).jpg") };
        Assert.Equal(Path.Combine("d", "a (800) (3).jpg"), ImageFit.OutputPath(Path.Combine("d", "a.png"), 800, ".jpg", taken.Contains));
        Assert.Equal((".png", ".png", ".jpg", ".jpeg"), (ImageFit.OutputExtension("x.PNG"), ImageFit.OutputExtension("x.gif"), ImageFit.OutputExtension("x.bmp"), ImageFit.OutputExtension("x.jpeg")));
    }

    private static readonly string D = Path.Combine("x", "pics");
    private static string P(string name) => Path.Combine(D, name);

    [Fact] public void Find_and_replace_keeps_the_extension_and_ignores_case_unless_asked()
    {
        var r = RenamePlan.Make([P("IMG_001.JPG"), P("img_002.jpg"), P("cat.png")], new RenameRule("img_", "Trip "), _ => false);
        Assert.Equal(["Trip 001.JPG", "Trip 002.jpg", "cat.png"], r.Select(i => i.NewName));
        Assert.Equal([RenameState.Ready, RenameState.Ready, RenameState.Same], r.Select(i => i.State));
        Assert.Equal(RenameState.Same, RenamePlan.Make([P("IMG_001.JPG")], new RenameRule("img_", "x", MatchCase: true), _ => false)[0].State);
    }

    [Fact] public void Numbering_pads_to_the_count_and_a_regex_can_reorder()
    {
        var n = RenamePlan.Make(Enumerable.Range(1, 12).Select(i => P($"f{i}.jpg")).ToList(), new RenameRule("", "Holiday", Number: true), _ => false);
        Assert.Equal("Holiday 01.jpg", n[0].NewName); Assert.Equal("Holiday 12.jpg", n[11].NewName);
        Assert.Equal("2024-05 report.pdf", RenamePlan.Make([P("report 2024-05.pdf")], new RenameRule(@"(.+) (\d{4}-\d\d)", "$2 $1", Regex: true), _ => false)[0].NewName);
        Assert.NotNull(RenamePlan.Check(new RenameRule("(", "", Regex: true)));
    }

    [Fact] public void A_runaway_regex_refuses_the_plan_instead_of_showing_names_unchanged() =>
        Assert.Throws<System.Text.RegularExpressions.RegexMatchTimeoutException>(() => RenamePlan.Make([P(new string('a', 40) + "!.txt")], new RenameRule("(a+)+$", "x", Regex: true), _ => false));

    [Fact] public void Two_files_wanting_one_name_a_name_outside_the_batch_or_a_bad_name_are_marked()
    {
        var both = RenamePlan.Make([P("a1.txt"), P("a2.txt")], new RenameRule(@"\d", "", Regex: true), _ => false);
        Assert.All(both, i => Assert.Equal(RenameState.Clash, i.State));
        Assert.Equal(RenameState.Clash, RenamePlan.Make([P("a.txt")], new RenameRule("a", "b"), p => p == P("b.txt"))[0].State);
        // a swap inside the batch is fine: both old names are freed
        var swap = RenamePlan.Make([P("x 02.txt"), P("x 01.txt")], new RenameRule("", "x", Number: true), _ => true);
        Assert.Equal(["x 01.txt", "x 02.txt"], swap.Select(i => i.NewName)); Assert.All(swap, i => Assert.Equal(RenameState.Ready, i.State));
        Assert.Equal(RenameState.Bad, RenamePlan.Make([P("a.txt")], new RenameRule("a", "con"), _ => false)[0].State);
        Assert.Equal(RenameState.Bad, RenamePlan.Make([P("a.txt")], new RenameRule("a", "x?y"), _ => false)[0].State);
        Assert.Equal(RenameState.Bad, RenamePlan.Make([P("a.txt")], new RenameRule("a", ""), _ => false)[0].State);
        Assert.Equal(RenameState.Ready, RenamePlan.Make([P("a.txt")], new RenameRule("a", "A", MatchCase: true), p => p == P("A.txt"))[0].State);
    }

    [Fact] public void A_pasted_checksum_is_found_and_its_kind_known()
    {
        const string sha = "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08";
        Assert.Equal("SHA-256", Checksum.KindOf("SHA256: " + sha));
        Assert.True(Checksum.Matches(sha.ToLowerInvariant(), "  sha256 = " + sha + "\n"));
        Assert.Equal("MD5", Checksum.KindOf("d41d8cd98f00b204e9800998ecf8427e"));
        Assert.Null(Checksum.KindOf("hello")); Assert.False(Checksum.Matches("abc", ""));
    }

    [Theory, InlineData(255, 0, 0, "#FF0000", "rgb(255, 0, 0)", "hsl(0, 100%, 50%)"), InlineData(0, 128, 0, "#008000", "rgb(0, 128, 0)", "hsl(120, 100%, 25%)"),
     InlineData(30, 144, 255, "#1E90FF", "rgb(30, 144, 255)", "hsl(210, 100%, 56%)"), InlineData(128, 128, 128, "#808080", "rgb(128, 128, 128)", "hsl(0, 0%, 50%)"), InlineData(255, 0, 128, "#FF0080", "rgb(255, 0, 128)", "hsl(330, 100%, 50%)")]
    public void A_picked_colour_is_written_three_ways(byte r, byte g, byte b, string hex, string rgb, string hsl) => Assert.Equal((hex, rgb, hsl), (ColorText.Hex(r, g, b), ColorText.Rgb(r, g, b), ColorText.Hsl(r, g, b)));

    [Fact] public void Only_the_biggest_files_are_kept()
    {
        var top = new TopFiles(2);
        foreach (var (p, s) in new[] { ("a", 5L), ("b", 50L), ("c", 1L), ("d", 20L) }) top.Add(p, s);
        Assert.Equal(["b", "d"], top.Biggest().Select(x => x.Path));
    }
}
