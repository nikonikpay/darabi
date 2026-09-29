using System.Globalization; using Mazesta.Core.Providers;
namespace Mazesta.Diagnostics.Storage;

/// <summary>
/// Final SMART re-check (spec 4.2 #11, 5.3): every physical drive's health as Windows reads it from SMART, run last in the queue so it sees what
/// the tests did to the drives. A drive Windows calls Warning or Unhealthy, or one that reports uncorrected read/write errors, fails the test -
/// each such drive is one error. Wear, temperatures and power-on hours are reported as evidence, never judged here. It only reads: nothing is
/// written to any drive. No drive reporting a health status is Unsupported, not a pass.
/// </summary>
public sealed class SmartCheckExecutor(IDriveHealthProvider drives) : ITestExecutor
{
    public static readonly TestDefinition Definition = new(new TestId("storage.smart"), "Test_Storage_Smart", 5);
    TestDefinition ITestExecutor.Definition => Definition;

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        IReadOnlyList<DriveHealth> all;
        request.Note("Log_Smart", @"WMI root\Microsoft\Windows\Storage:MSFT_PhysicalDisk (HealthStatus) + MSFT_StorageReliabilityCounter");
        try { all = drives.Read(); }
        catch (Exception e) when (e is not OperationCanceledException) { return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, $"Windows did not report drive health: {e.Message}")); }
        var reported = all.Where(d => d.Status is not null).ToList();
        if (reported.Count == 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "No drive reported a SMART health status."));
        long bad = reported.Count(NeedsAttention);
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return Task.FromResult(new TestRunResult(Definition.Id, bad > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, bad, string.Join("; ", reported.Select(Describe))));
    }

    internal static bool NeedsAttention(DriveHealth d) => Core.Health.DriveAttention.Needs(d);

    internal static string Describe(DriveHealth d)
    {
        var parts = new List<string> { $"{d.Name}{(d.Serial is { } s ? $" ({s})" : "")}: {d.Status}" };
        if (d.WearPercent is { } wear) parts.Add($"wear {wear}%");
        if (d.TemperatureC is { } t) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{t:0} °C") + (d.TemperatureMaxC is { } max ? string.Create(CultureInfo.InvariantCulture, $" (max {max:0} °C)") : ""));
        if (d.PowerOnHours is { } h) parts.Add($"power-on {h} h");
        if (d.ReadErrorsUncorrected is not null || d.WriteErrorsUncorrected is not null) parts.Add($"uncorrected read/write errors {d.ReadErrorsUncorrected?.ToString(CultureInfo.InvariantCulture) ?? "-"}/{d.WriteErrorsUncorrected?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        return string.Join(", ", parts);
    }
}
