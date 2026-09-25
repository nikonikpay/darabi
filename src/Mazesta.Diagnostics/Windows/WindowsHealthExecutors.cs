using System.Text; using System.Text.RegularExpressions;
namespace Mazesta.Diagnostics.Windows;

/// <summary>What a Windows health tool concluded, read from its own words. Unknown means its output was not one of the known results - then the
/// test says so (Unsupported, with the tool's last lines) instead of guessing a pass or a fail.</summary>
public enum WindowsHealth { Healthy, Repaired, Damaged, Unknown }

/// <summary>
/// Windows health (spec 7.1: "Windows health (sfc, DISM)"): System File Checker verifies (and, as sfc always does, repairs) the protected system
/// files. Healthy or repaired passes; corrupt files it could not repair fail. It takes several minutes whatever the duration says; cancelling
/// stops it, which sfc tolerates. Runs only when the technician queues it.
/// </summary>
public sealed class SfcExecutor(ICommandRunner runner) : ITestExecutor
{
    public static readonly TestDefinition Definition = new(new TestId("windows.sfc"), "Test_Windows_Sfc", 900);
    TestDefinition ITestExecutor.Definition => Definition;
    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => WindowsTool.RunAsync(Definition, runner, "sfc.exe", "/scannow", Encoding.Unicode, ParseSfc, request, ct);

    /// <summary>sfc's final sentence (English Windows). Its console output is UTF-16, with progress lines in between.</summary>
    public static WindowsHealth ParseSfc(IReadOnlyList<string> output)
    {
        string all = string.Join(" ", output);
        if (all.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase)) return WindowsHealth.Healthy;
        if (all.Contains("successfully repaired", StringComparison.OrdinalIgnoreCase)) return WindowsHealth.Repaired;
        if (all.Contains("unable to fix", StringComparison.OrdinalIgnoreCase)) return WindowsHealth.Damaged;
        return WindowsHealth.Unknown;
    }
}

/// <summary>DISM's read-only check of the component store (the source sfc repairs from): "no corruption" passes, "repairable" or "not repairable"
/// fails - fixing it is a separate, deliberate step on the Windows Tools page (/RestoreHealth), never done by a test.</summary>
public sealed class DismScanExecutor(ICommandRunner runner) : ITestExecutor
{
    public static readonly TestDefinition Definition = new(new TestId("windows.dism"), "Test_Windows_Dism", 300);
    TestDefinition ITestExecutor.Definition => Definition;
    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => WindowsTool.RunAsync(Definition, runner, "dism.exe", "/Online /Cleanup-Image /ScanHealth", WindowsTool.Oem, ParseDism, request, ct);

    public static WindowsHealth ParseDism(IReadOnlyList<string> output)
    {
        string all = string.Join(" ", output);
        if (all.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase)) return WindowsHealth.Healthy;
        if (all.Contains("restore operation completed successfully", StringComparison.OrdinalIgnoreCase)) return WindowsHealth.Repaired;
        if (all.Contains("component store is repairable", StringComparison.OrdinalIgnoreCase) || all.Contains("not repairable", StringComparison.OrdinalIgnoreCase)) return WindowsHealth.Damaged;
        return WindowsHealth.Unknown;
    }
}

public static partial class WindowsTool
{
    /// <summary>The console code page Windows tools such as powercfg and DISM write in when their output is redirected (sfc writes UTF-16).</summary>
    public static Encoding Oem { get; } = OemEncoding();
    private static Encoding OemEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try { return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } catch (ArgumentException) { return Encoding.UTF8; }
    }

    [GeneratedRegex(@"(\d{1,3}(?:\.\d)?)\s*%")] private static partial Regex Percent();

    /// <summary>Progress from a tool's own "45% complete" / "[====  45.0% ]" lines, as a fraction; null for any other line.</summary>
    public static double? ProgressOf(string line) => Percent().Match(line) is { Success: true } m && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double p) ? Math.Clamp(p / 100, 0, 1) : null;

    internal static async Task<TestRunResult> RunAsync(TestDefinition definition, ICommandRunner runner, string file, string arguments, Encoding encoding, Func<IReadOnlyList<string>, WindowsHealth> parse, TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        CommandResult result;
        try { result = await runner.RunAsync(file, arguments, encoding, l => { if (ProgressOf(l) is { } p) request.Progress?.Invoke(new TestProgress(p, "Test_Status_Running")); }, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return TestRunResult.Cancelled(definition.Id, started, request.Clock.UtcNow); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return TestRunResult.Unsupported(definition.Id, started, $"{file} could not run: {e.Message}"); }
        var health = parse(result.Output);
        string tail = string.Join(" | ", result.Output.Where(l => ProgressOf(l) is null).TakeLast(3));
        string detail = $"{file} {arguments}: {health} (exit code {result.ExitCode}); {tail}";
        return health switch
        {
            WindowsHealth.Healthy or WindowsHealth.Repaired => new(definition.Id, TestOutcome.Passed, started, request.Clock.UtcNow, 0, detail),
            WindowsHealth.Damaged => new(definition.Id, TestOutcome.Failed, started, request.Clock.UtcNow, 1, detail),
            _ => TestRunResult.Unsupported(definition.Id, started, "The tool's result could not be read (non-English Windows or an unexpected message). " + detail)
        };
    }
}
