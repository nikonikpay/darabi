using Xunit; using Mazesta.Diagnostics.Ai;
namespace Mazesta.Diagnostics.Tests;

public class AiChatsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mz-chats-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static AiChat Chat(DateTimeOffset at, string question)
    {
        var c = AiChats.New(at); c.Updated = at;
        c.Messages.Add(new() { Role = "user", Text = question });
        c.Messages.Add(new() { Role = "assistant", Text = "ok", Tools = [new("run_tests", "{}", """{"started":false}""", true)] });
        return c;
    }

    [Fact] public void A_chat_is_kept_with_its_tools_and_listed_newest_first()
    {
        var store = new AiChats(_root); var t = DateTimeOffset.Now;
        store.Save(Chat(t, "first")); store.Save(Chat(t.AddMinutes(1), "second"));
        var again = new AiChats(_root);
        Assert.Equal(["second", "first"], again.List().Select(c => c.Title));
        var c = again.Load(again.List()[0].Id)!;
        Assert.Equal("run_tests", c.Messages[1].Tools[0].Name); Assert.True(c.Messages[1].Tools[0].Ok);
    }

    [Fact] public void Delete_one_or_all()
    {
        var store = new AiChats(_root); var t = DateTimeOffset.Now; var a = Chat(t, "a"); store.Save(a); store.Save(Chat(t.AddSeconds(1), "b"));
        store.Delete(a.Id); Assert.Equal(["b"], store.List().Select(c => c.Title)); Assert.Null(store.Load(a.Id));
        store.DeleteAll(); Assert.Empty(store.List()); Assert.Empty(new AiChats(_root).List());
    }

    [Fact] public void An_empty_chat_is_not_kept_and_a_strange_id_names_no_file()
    {
        var store = new AiChats(_root); store.Save(AiChats.New(DateTimeOffset.Now)); Assert.Empty(store.List());
        Assert.Null(store.Load("..\\..\\appconfig")); store.Delete("../x");
    }

    [Fact] public void Only_the_newest_are_kept()
    {
        var store = new AiChats(_root); var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < AiChats.Keep + 3; i++) store.Save(Chat(t.AddMinutes(i), "q" + i));
        Assert.Equal(AiChats.Keep, store.List().Count); Assert.Equal("q" + (AiChats.Keep + 2), store.List()[0].Title);
    }

    [Fact] public void A_title_is_the_question_cut_at_a_word()
    {
        Assert.Equal("رم رو چک کن", AiChats.TitleOf("  رم رو چک کن \n more"));
        var t = AiChats.TitleOf(string.Join(' ', Enumerable.Repeat("word", 40)));
        Assert.True(t.Length <= AiChats.TitleChars + 1); Assert.EndsWith("…", t);
    }
}
