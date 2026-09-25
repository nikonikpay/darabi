using Mazesta.Core.Inventory;
namespace Mazesta.Reporting;

/// <summary>A test's outcome on each side. Either side is null when the test only ran in the other
/// report - never presented as a fabricated "same" outcome.</summary>
public sealed record TestOutcomeChange(string Id, string Name, ReportOutcome? Before, ReportOutcome? After);

/// <summary>A sensor's min/average/max on each side, and the after-minus-before delta. Any value is
/// null when that sensor has no reading on that side (spec: missing is never shown as zero), and the
/// matching delta is then null too rather than treating the missing side as zero.</summary>
public sealed record SensorDelta(string Id, string Name, string Unit,
    double? MinBefore, double? MinAfter, double? MinDelta,
    double? AverageBefore, double? AverageAfter, double? AverageDelta,
    double? MaxBefore, double? MaxAfter, double? MaxDelta);

/// <summary>One benchmark number on each side (matched by benchmark and metric), null on a side that did not measure it.</summary>
public sealed record BenchmarkDelta(string BenchmarkId, string Benchmark, string Metric, string Unit, double? Before, double? After, double? Delta);

/// <summary>Which identity check two reports failed: the part that differs (or is unknown) between them.</summary>
public enum MachineMismatch { Cpu, Motherboard, Storage, ServiceNumber }

public sealed record ReportComparison(bool IsComparable, string? Reason, IReadOnlyList<TestOutcomeChange> Tests, IReadOnlyList<SensorDelta> Sensors, IReadOnlyList<BenchmarkDelta> Benchmarks, MachineMismatch? Mismatch = null)
{
    private static ReportComparison NotComparable(string reason, MachineMismatch mismatch) => new(false, reason, [], [], [], mismatch);

    /// <summary>Matches tests and sensors by Id across the two reports. Refuses to compare (spec
    /// §identity) unless the CPU name, motherboard manufacturer+product and any storage serial
    /// numbers present on both sides all agree - two reports from different machines must never be
    /// shown as a before/after of the same one.</summary>
    public static ReportComparison Compare(SessionReport before, SessionReport after)
    {
        if (!SameMachine(before.Machine, after.Machine, out string reason, out var mismatch)) return NotComparable(reason, mismatch);
        // Spec 4.4: before and after belong to one service job. Two numbers that differ are two jobs; a report without one is not refused.
        if (before.ServiceNumber is { } sb && after.ServiceNumber is { } sa && !string.Equals(sb, sa, StringComparison.OrdinalIgnoreCase))
            return NotComparable("The two reports belong to different service numbers.", MachineMismatch.ServiceNumber);

        var beforeTests = before.Tests.ToDictionary(t => t.Id);
        var afterTests = after.Tests.ToDictionary(t => t.Id);
        var testIds = beforeTests.Keys.Concat(afterTests.Keys.Where(id => !beforeTests.ContainsKey(id)));
        var tests = testIds.Select(id =>
        {
            beforeTests.TryGetValue(id, out var b); afterTests.TryGetValue(id, out var a);
            return new TestOutcomeChange(id, (b ?? a)!.Name, b?.Outcome, a?.Outcome);
        }).ToList();

        var beforeSensors = before.Sensors.ToDictionary(s => s.Id);
        var afterSensors = after.Sensors.ToDictionary(s => s.Id);
        var sensorIds = beforeSensors.Keys.Concat(afterSensors.Keys.Where(id => !beforeSensors.ContainsKey(id)));
        var sensors = sensorIds.Select(id =>
        {
            beforeSensors.TryGetValue(id, out var b); afterSensors.TryGetValue(id, out var a);
            var s = (b ?? a)!;
            return new SensorDelta(id, s.Name, s.Unit,
                b?.Min, a?.Min, Delta(b?.Min, a?.Min),
                b?.Average, a?.Average, Delta(b?.Average, a?.Average),
                b?.Max, a?.Max, Delta(b?.Max, a?.Max));
        }).ToList();

        var beforeMetrics = Metrics(before); var afterMetrics = Metrics(after);
        var benchmarks = beforeMetrics.Keys.Concat(afterMetrics.Keys.Where(k => !beforeMetrics.ContainsKey(k))).Select(k =>
        {
            beforeMetrics.TryGetValue(k, out var b); afterMetrics.TryGetValue(k, out var a); var any = (b ?? a)!;
            return new BenchmarkDelta(k.Id, any.Benchmark, k.Metric, any.Unit, b?.Value, a?.Value, Delta(b?.Value, a?.Value));
        }).ToList();
        return new(true, null, tests, sensors, benchmarks);
    }

    private sealed record Measured(string Benchmark, string Unit, double Value);
    private static Dictionary<(string Id, string Metric), Measured> Metrics(SessionReport r)
        => (r.Benchmarks ?? []).SelectMany(b => b.Metrics.Select(m => (Key: (b.Id, m.Name), Value: new Measured(b.Name, m.Unit, m.Value)))).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Last().Value);

    private static double? Delta(double? before, double? after) => before is { } b && after is { } a ? a - b : null;

    private static bool SameMachine(HardwareInventory before, HardwareInventory after, out string reason, out MachineMismatch mismatch)
    {
        if (before.Cpu?.Name is not { } cpuBefore || after.Cpu?.Name is not { } cpuAfter || cpuBefore != cpuAfter)
        { reason = "CPU is unknown or different on the two reports."; mismatch = MachineMismatch.Cpu; return false; }

        var mbBefore = before.Motherboard; var mbAfter = after.Motherboard;
        if (mbBefore?.Manufacturer is not { } mfBefore || mbBefore.Product is not { } prBefore ||
            mbAfter?.Manufacturer is not { } mfAfter || mbAfter.Product is not { } prAfter ||
            mfBefore != mfAfter || prBefore != prAfter)
        { reason = "Motherboard is unknown or different on the two reports."; mismatch = MachineMismatch.Motherboard; return false; }

        var serialsBefore = before.Storage.Select(d => d.SerialNumber).Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet();
        var serialsAfter = after.Storage.Select(d => d.SerialNumber).Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet();
        if (serialsBefore.Count > 0 && serialsAfter.Count > 0 && !serialsBefore.SetEquals(serialsAfter))
        { reason = "Storage serial numbers differ between the two reports."; mismatch = MachineMismatch.Storage; return false; }

        reason = ""; mismatch = default; return true;
    }
}
