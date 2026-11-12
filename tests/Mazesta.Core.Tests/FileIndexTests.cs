using Mazesta.Core.Search; using Xunit;
namespace Mazesta.Core.Tests;

public class FileIndexTests
{
    private static (FileIndex Index, int Users, int Report, int Photo) Sample()
    {
        var x = new FileIndex(@"C:\");
        int users = x.Add(0, "Users", true), me = x.Add(users, "Me", true);
        int report = x.Add(me, "Report 2024.PDF", false), photo = x.Add(me, "photo.jpg", false);
        x.Add(me, "report", true); x.Add(me, "old-report-copy.docx", false); x.Add(me, "movie.mp4", false);
        return (x, users, report, photo);
    }

    private static List<string> Find(FileIndex x, string q, int max = 100)
    {
        var best = new List<(long Key, int Entry)>(); x.Search(FileQuery.Parse(q)!, max, best);
        return [.. best.OrderBy(b => b.Key).ThenBy(b => b.Entry).Select(b => x.Name(b.Entry))];
    }

    [Fact] public void Paths_are_the_chain_of_parents()
    {
        var (x, users, report, _) = Sample();
        Assert.Equal(@"C:\Users\Me\Report 2024.PDF", x.PathOf(report)); Assert.Equal(@"C:\Users", x.PathOf(users)); Assert.Equal(@"C:\", x.PathOf(0));
        Assert.Equal(7, x.Count);
    }

    [Fact] public void Words_match_in_any_case_and_order_and_the_name_itself_ranks_first()
    {
        var (x, _, _, _) = Sample();
        Assert.Equal(["report", "Report 2024.PDF", "old-report-copy.docx"], Find(x, "REPORT"));
        Assert.Equal(["Report 2024.PDF"], Find(x, "2024 rep"));
        Assert.Empty(Find(x, "report xyz"));
    }

    [Fact] public void Persian_and_accented_names_are_found_with_or_without_the_pool_search()
    {
        var x = new FileIndex(@"E:\");
        int docs = x.Add(0, "اسناد", true); x.Add(docs, "گزارش مالی ۱۴۰۵.pdf", false); x.Add(docs, "Éclair recette.txt", false); x.Add(docs, "plain.txt", false);
        foreach (bool trimmed in new[] { false, true })
        {
            if (trimmed) x.Trim();
            Assert.Equal(["گزارش مالی ۱۴۰۵.pdf"], Find(x, "گزارش")); Assert.Equal(["گزارش مالی ۱۴۰۵.pdf"], Find(x, "مالی ۱۴۰۵"));
            Assert.Equal(["Éclair recette.txt"], Find(x, "éCLAIR")); Assert.Equal(["Éclair recette.txt"], Find(x, "RECETTE"));
            Assert.Equal(@"E:\اسناد\گزارش مالی ۱۴۰۵.pdf", x.PathOf(2));
        }
    }

    [Fact] public void Among_equal_names_the_shorter_path_comes_first()
    {
        var x = new FileIndex(@"C:\");
        int win = x.Add(0, "Windows", true), sxs = x.Add(win, "WinSxS", true), comp = x.Add(sxs, "amd64_microsoft-windows-commandprompt_31bf3856ad364e35_10.0.26100.1", true), r = x.Add(comp, "r", true);
        int deep = x.Add(r, "cmd.exe", false), sys = x.Add(x.Add(win, "System32", true), "cmd.exe", false); x.Trim();
        var best = new List<(long Key, int Entry)>(); x.Search(FileQuery.Parse("cmd.exe")!, 10, best);
        Assert.Equal([sys, deep], best.OrderBy(b => b.Key).Select(b => b.Entry));
    }

    [Fact] public void A_word_is_found_in_any_case_at_every_place_in_a_long_pool()
    {
        var x = new FileIndex(@"F:\");   // names around and across the 32-byte steps, with letters, digits, '_' and '-' around the word
        for (int i = 0; i < 300; i++) x.Add(0, new string('a', i % 70) + (i % 3 == 0 ? "_Re-Port_" : "repo") + i, false);
        x.Trim();
        Assert.Equal(100, x.Search(FileQuery.Parse("re-port")!, 1000, []));
        Assert.Equal(100, x.Search(FileQuery.Parse("_RE-PORT_")!, 1000, []));
        Assert.Equal(300, x.Search(FileQuery.Parse("rE")!, 1000, []));
        Assert.Equal(0, x.Search(FileQuery.Parse("re_port")!, 1000, []));
    }

    [Fact] public void Patterns_fit_the_whole_name()
    {
        var (x, _, _, _) = Sample();
        Assert.Equal(["movie.mp4"], Find(x, "*.MP4")); Assert.Equal(["photo.jpg"], Find(x, "ph?to.*")); Assert.Empty(Find(x, "*.mp"));
        Assert.Null(FileQuery.Parse("   "));
    }

    [Fact] public void Removed_and_renamed_entries_are_found_by_their_new_state()
    {
        var (x, users, report, photo) = Sample(); x.Trim();   // fixed, as after a drive is read: later names are searched apart
        x.Edit(i => i.Add(users, "Report final.txt", false));
        Assert.Equal(["report", "Report 2024.PDF", "Report final.txt", "old-report-copy.docx"], Find(x, "report"));
        x.Edit(i => { i.Remove(i.Entries - 1); return 0; });
        x.Edit(i => { i.Remove(report); i.Move(photo, users, "beach.jpg"); return 0; });
        Assert.Equal(["report", "old-report-copy.docx"], Find(x, "report"));
        Assert.Equal(@"C:\Users\beach.jpg", x.PathOf(photo)); Assert.Empty(Find(x, "photo")); Assert.Equal(6, x.Count);
    }

    [Fact] public void All_matches_are_counted_and_the_best_are_kept()
    {
        var x = new FileIndex(@"D:\");
        for (int i = 0; i < 200_000; i++) x.Add(0, $"file{i:D6}.txt", false);
        x.Add(0, "file", false); x.Trim();
        var best = new List<(long Key, int Entry)>();
        Assert.Equal(200_001, x.Search(FileQuery.Parse("file")!, 50, best));
        Assert.InRange(best.Count, 50, 50 * 64);   // each worker keeps its own 50; the caller takes the best of them
        Assert.Equal("file", x.Name(best.MinBy(b => b.Key).Entry));
    }
}
