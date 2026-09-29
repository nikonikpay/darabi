using Xunit;
namespace Mazesta.Diagnostics.Gpu.Tests;

/// <summary>The stress test reads back only what the last dispatch of a batch left, so each dispatch continues from the previous one's output:
/// a wrong value anywhere in the batch must still show at the end.</summary>
public class GpuHashChainTests
{
    [Fact] public void One_dispatch_is_the_plain_hash_of_the_index()
        => Assert.Equal(GpuHash.Reference(77, 512, 0xABCDu), GpuHash.Chained(77, 512, 0xABCDu, 1));

    [Theory, InlineData(0), InlineData(3), InlineData(7)]
    public void A_wrong_result_in_any_dispatch_of_a_chain_changes_its_end_value(int wrongAt)
    {
        const int Dispatches = 8, Rounds = 64; const uint Seed = 0x5EEDu, Index = 1234;
        uint h = Index;
        for (int d = 0; d < Dispatches; d++) { h = GpuHash.Reference(h, Rounds, Seed); if (d == wrongAt) h ^= 1u << 9; }   // one flipped bit, as a faulty ALU would leave it
        Assert.NotEqual(GpuHash.Chained(Index, Rounds, Seed, Dispatches), h);
    }
}
