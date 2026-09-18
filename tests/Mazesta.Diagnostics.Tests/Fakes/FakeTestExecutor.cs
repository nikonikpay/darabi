namespace Mazesta.Diagnostics.Tests.Fakes;
public sealed class FakeTestExecutor(TestDefinition definition, Func<TestExecutionRequest, CancellationToken, Task<TestRunResult>> run) : ITestExecutor
{
    public TestDefinition Definition { get; } = definition;
    public int CallCount { get; private set; }
    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct) { CallCount++; return run(request, ct); }
}
