using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics; using Xunit;
namespace Mazesta.Desktop.Tests;

public class TestAdviceTests
{
    [Theory]
    [InlineData("cpu.linpack", TestOutcome.Failed, "Advice_Cpu")]
    [InlineData("memory.pattern", TestOutcome.Failed, "Advice_Memory")]
    [InlineData("gpu.vram", TestOutcome.Failed, "Advice_Vram")]
    [InlineData("gpu.steady", TestOutcome.Failed, "Advice_Gpu")]
    [InlineData("storage.smart", TestOutcome.Failed, "Advice_Smart")]
    [InlineData("storage.random4k", TestOutcome.Failed, "Advice_Storage")]
    [InlineData("cpu.singlecore", TestOutcome.Inconclusive, "Advice_Inconclusive_Cores")]
    [InlineData("network.latency", TestOutcome.Inconclusive, "Advice_Inconclusive_Network")]
    [InlineData("cpu.fft", TestOutcome.Error, "Advice_Error")]
    public void A_bad_result_gets_the_advice_for_its_part(string id, TestOutcome outcome, string key) => Assert.Equal(key, TestAdvice.KeyFor(id, outcome));

    [Theory, InlineData(TestOutcome.Passed), InlineData(TestOutcome.Cancelled), InlineData(TestOutcome.Unsupported), InlineData(TestOutcome.NotRun)]
    public void A_pass_or_a_test_that_did_not_run_gets_none(TestOutcome outcome) => Assert.Null(TestAdvice.KeyFor("cpu.matrix", outcome));

    [Fact] public void Every_advice_key_has_text() =>
        Assert.All(new[] { "Advice_Cpu", "Advice_Memory", "Advice_Gpu", "Advice_Vram", "Advice_Storage", "Advice_Smart", "Advice_Network", "Advice_Power", "Advice_Windows", "Advice_Inconclusive", "Advice_Inconclusive_Cores", "Advice_Inconclusive_Network", "Advice_Error" },
            k => Assert.NotEqual(k, Loc.Get(k)));
}
