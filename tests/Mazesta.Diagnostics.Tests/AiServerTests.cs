using Xunit; using Mazesta.Diagnostics.Ai;
namespace Mazesta.Diagnostics.Tests;

public class AiServerTests
{
    [Fact] public void A_streamed_chunk_gives_its_text() => Assert.Equal("سلام", AiServer.Piece("""{"choices":[{"delta":{"content":"سلام"}}]}"""));
    [Fact] public void A_chunk_without_text_or_not_json_gives_nothing()
    {
        Assert.Null(AiServer.Piece("""{"choices":[{"delta":{"role":"assistant"}}]}""")); Assert.Null(AiServer.Piece("""{"choices":[]}""")); Assert.Null(AiServer.Piece("not json"));
    }
}
