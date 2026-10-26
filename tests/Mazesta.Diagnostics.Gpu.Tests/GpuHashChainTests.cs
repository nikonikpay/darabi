using Xunit;
namespace Mazesta.Diagnostics.Gpu.Tests;

/// <summary>The stress test reads back only what the last dispatch of a batch left, so each dispatch continues from the previous one's output:
/// a wrong value anywhere in the batch must still show at the end.</summary>
public class GpuHashChainTests
{
    [Fact] public void One_dispatch_is_the_plain_hash_of_the_index()
        => Assert.Equal(GpuHash.Reference(77, 512, 0xABCDu), GpuHash.Chained(77, 512, 0xABCDu, 1));

    [Fact] public void The_temperature_loads_table_reads_change_the_result_and_depend_on_the_thread()
    {
        Assert.NotEqual(GpuHash.Reference(77, 64, 9u), GpuHash.ThermalReference(77, 77, 64, 9u));   // the table is read (every eighth round)
        Assert.NotEqual(GpuHash.ThermalReference(1, 5, 64, 9u), GpuHash.ThermalReference(2, 5, 64, 9u));   // from an address of the thread's own
        Assert.Equal(GpuHash.ThermalReference(3, 3, 64, 9u), GpuHash.ThermalChained(3, 64, 9u, 1));
    }

    [Theory, InlineData(0), InlineData(3), InlineData(7)]
    public void A_wrong_result_in_any_dispatch_of_a_chain_changes_its_end_value(int wrongAt)
    {
        const int Dispatches = 8, Rounds = 64; const uint Seed = 0x5EEDu, Index = 1234;
        uint h = Index;
        for (int d = 0; d < Dispatches; d++) { h = GpuHash.Reference(h, Rounds, Seed); if (d == wrongAt) h ^= 1u << 9; }   // one flipped bit, as a faulty ALU would leave it
        Assert.NotEqual(GpuHash.Chained(Index, Rounds, Seed, Dispatches), h);
    }
}
