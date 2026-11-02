using System.Globalization; using Mazesta.Core.Providers;
namespace Mazesta.Diagnostics.Storage;

/// <summary>
/// Final SMART re-check (spec 4.2 #11, 5.3): every physical drive's health as Windows reads it from SMART, run last in the queue so it sees what
/// the tests did to the drives. A drive Windows calls Warning or Unhealthy, or one that reports uncorrected read/write errors, fails the test -
/// each such drive is one error. Wear, temperatures and power-on hours are reported as evidence, never judged here. It only reads: nothing is
/// written to any drive. No drive reporting a health status is Unsupported, not a pass. NVMe drives are also asked for their own health log:
/// a critical-warning bit or a media (unrecovered data integrity) error it counts is one error for the drive, as Windows' own verdict is.
/// </summary>
public sealed class SmartCheckExecutor(IDriveHealthProvider drives, INvmeHealthSource? nvme = null) : ITestExecutor
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
        IReadOnlyList<NvmeDriveHealth> logs = [];
        if (nvme is not null)
        {
            request.Note("Log_Smart_Nvme", "IOCTL_STORAGE_QUERY_PROPERTY: NVMe log page 02h (SMART / Health Information)");
            try { logs = nvme.Read(); } catch (Exception e) when (e is not OperationCanceledException) { request.NoteWarning("Log_Smart_NvmeFailed", e.Message); }
        }
        // Every drive is named in full, failing ones first and marked, so whoever reads the result knows which disk it is about without knowing models.
        var failing = new List<string>();
        foreach (var d in reported)
        {
            request.Note("Log_Smart_Drive", d.Serial is { } s ? $"S/N {s}" : null, d.Label, "@Detail_Drive_" + d.Status);
            if (NeedsAttention(d)) { failing.Add(d.Label); request.NoteError("Log_Smart_DriveBad", null, d.Label); }
        }
        foreach (var l in logs.Where(l => NvmeNeedsAttention(l.Log)))
        {
            string label = LabelOf(l, reported);
            if (!failing.Contains(label)) failing.Add(label);
            request.NoteError("Log_Smart_NvmeBad", null, label);
        }
        long bad = reported.Count(NeedsAttention) + logs.Count(l => NvmeNeedsAttention(l.Log));
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        var lines = failing.Select(f => $"needs attention: {f}").Concat(reported.OrderByDescending(NeedsAttention).Select(Describe)).Concat(logs.Select(l => DescribeNvme(l, LabelOf(l, reported))));
        return Task.FromResult(new TestRunResult(Definition.Id, bad > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, bad, string.Join("; ", lines)));
    }

    /// <summary>The NVMe log's drive as the health list names it (same serial, else same model); its bare model when Windows did not list it.</summary>
    internal static string LabelOf(NvmeDriveHealth l, IReadOnlyList<DriveHealth> drives)
        => (drives.FirstOrDefault(d => d.Serial is not null && l.Serial is not null && Key(d.Serial) == Key(l.Serial)) ?? drives.FirstOrDefault(d => l.Model is not null && d.Name == l.Model.Trim()))?.Label
           ?? $"{l.Model ?? "NVMe"} (disk {l.DiskNumber})";
    private static string Key(string serial) => new([.. serial.Where(char.IsLetterOrDigit)]);

    internal static bool NeedsAttention(DriveHealth d) => Core.Health.DriveAttention.Needs(d);
    internal static bool NvmeNeedsAttention(NvmeHealthLog l) => l.CriticalWarning != 0 || l.MediaErrors > 0;

    internal static string DescribeNvme(NvmeDriveHealth d, string label)
        => $"{label} NVMe log: critical warning {(d.Log.CriticalWarning == 0 ? "none" : string.Join(", ", d.Log.Warnings))}, media errors {d.Log.MediaErrors}, "
         + $"spare {d.Log.AvailableSparePercent}% (threshold {d.Log.SpareThresholdPercent}%), used {d.Log.PercentageUsed}%, unsafe shutdowns {d.Log.UnsafeShutdowns}, error-log entries {d.Log.ErrorLogEntries}";

    internal static string Describe(DriveHealth d)
    {
        var parts = new List<string> { $"{d.Label}{(d.Serial is { } s ? $" S/N {s}" : "")}: {d.Status}" };
        if (d.WearPercent is { } wear) parts.Add($"wear {wear}%");
        if (d.TemperatureC is { } t) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{t:0} °C") + (d.TemperatureMaxC is { } max ? string.Create(CultureInfo.InvariantCulture, $" (max {max:0} °C)") : ""));
        if (d.PowerOnHours is { } h) parts.Add($"power-on {h} h");
        if (d.ReadErrorsUncorrected is not null || d.WriteErrorsUncorrected is not null) parts.Add($"uncorrected read/write errors {d.ReadErrorsUncorrected?.ToString(CultureInfo.InvariantCulture) ?? "-"}/{d.WriteErrorsUncorrected?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        return string.Join(", ", parts);
    }
}
