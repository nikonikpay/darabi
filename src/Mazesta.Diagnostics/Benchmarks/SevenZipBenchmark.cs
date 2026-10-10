using System.Diagnostics; using System.Globalization; using System.Text.RegularExpressions; using Mazesta.Core.Hardware;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>
/// 7-Zip's own benchmark (<c>7z b</c>): LZMA compression and decompression, rated in MIPS - the number shops and review sites quote, so a result
/// here compares with theirs. It is run with the 7-Zip installed on the machine, not a copy of ours: nothing is shipped or downloaded, and a machine
/// without 7-Zip gets Unsupported, never a number made some other way. The rating is 7-Zip's; only the clock, power and temperature beside it are ours.
/// </summary>
public sealed partial class SevenZipBenchmark : IBenchmark
{
    public const string ThreadsOption = "threads";
    public static readonly TestDefinition Spec = new(new TestId("bench.cpu.7zip"), "Bench_7z", 60,
        [new TestOption(ThreadsOption, "Bench_7z_Threads", TestOptionKind.Choice, "all", () => [new("all", "Bench_7z_Threads_All", true), new("1", "Bench_7z_Threads_One", true)])]);
    public TestDefinition Definition => Spec;
    public HardwareKind Component => HardwareKind.Cpu;

    /// <summary>Where 7-Zip installs itself (64- and 32-bit), then the PATH.</summary>
    public static string? Find()
    {
        var dirs = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }
            .Where(d => d.Length > 0).Select(d => Path.Combine(d, "7-Zip"))
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        foreach (var dir in dirs) { try { string exe = Path.Combine(dir, "7z.exe"); if (File.Exists(exe)) return exe; } catch (ArgumentException) { } }
        return null;
    }

    public async Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (Find() is not { } exe) return BenchmarkResult.Unsupported(Definition.Id, started, "7-Zip is not installed (7z.exe was not found in Program Files or on the PATH); install it from 7-zip.org to run its benchmark.");
        bool one = request.Options?.Get(ThreadsOption) == "1";
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("b"); if (one) info.ArgumentList.Add("-mmt1");
        using var p = Process.Start(info) ?? throw new InvalidOperationException("7z.exe did not start.");
        var output = p.StandardOutput.ReadToEndAsync(CancellationToken.None); var errors = p.StandardError.ReadToEndAsync(CancellationToken.None);
        // 7-Zip decides how long its passes take; the bar moves on the definition's length and the run is ended if it takes far longer.
        var clock = Stopwatch.StartNew(); var limit = TimeSpan.FromSeconds(Math.Max(60, request.DurationSeconds) * 10);
        while (!p.WaitForExit(250))
        {
            if (ct.IsCancellationRequested || clock.Elapsed > limit)
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                if (ct.IsCancellationRequested) return BenchmarkResult.Cancelled(Definition.Id, started, request.Clock.UtcNow);
                return BenchmarkResult.Failed(Definition.Id, started, request.Clock.UtcNow, $"7-Zip's benchmark gave no result in {limit.TotalMinutes:F0} minutes and was ended.");
            }
            request.Report(Math.Min(0.95, clock.Elapsed.TotalSeconds / Math.Max(1, request.DurationSeconds)));
        }
        string text = await output.ConfigureAwait(false), err = await errors.ConfigureAwait(false);
        var finished = request.Clock.UtcNow;
        // 7-Zip checks every block it decompresses: a CRC error is the CPU or the memory computing wrongly.
        if (text.Contains("CRC Error", StringComparison.OrdinalIgnoreCase) || err.Contains("CRC Error", StringComparison.OrdinalIgnoreCase))
            return BenchmarkResult.Failed(Definition.Id, started, finished, "7-Zip's benchmark reported a CRC error: data decompressed wrongly, a CPU or memory fault.");
        if (Parse(text) is not { } r) return new(Definition.Id, BenchmarkStatus.Error, started, finished, [], $"7-Zip's benchmark ended (exit code {p.ExitCode}) without the rating lines it prints. {err.Trim()}".Trim());
        List<BenchmarkMetric> metrics = [new("Bench_7z_Total", r.Total, "MIPS"), new("Bench_7z_Compress", r.Compress, "MIPS"), new("Bench_7z_Decompress", r.Decompress, "MIPS")];
        metrics.AddFirst(request, HardwareKind.Cpu, started, finished, one ? "Bench_Cpu_ClockPeak" : "Bench_Cpu_Clock", Unit.MegaHertz, one, null, one ? [SensorRole.CpuEffectiveClock, SensorRole.CpuCoreClock] : [SensorRole.CpuEffectiveClockAverage, SensorRole.CpuCoreClockAverage]);
        metrics.AddSensor(request, HardwareKind.Cpu, SensorRole.CpuPackagePower, started, finished, "Bench_Cpu_Power", Unit.Watt);
        if (Evidence.SensorEvidence.CpuTemperature(request.Engine, started, finished) is { } temp) metrics.Add(new("Bench_Cpu_TempMax", temp.Max, Units.Symbol(Unit.Celsius)));
        string version = VersionLine().Match(text) is { Success: true } v ? v.Value.Trim() : "7-Zip";
        return new(Definition.Id, BenchmarkStatus.Completed, started, finished, metrics, $"{version}, '7z b{(one ? " -mmt1" : "")}', rating in MIPS (7-Zip's own)");
    }

    /// <summary>The ratings of the lines 7-Zip ends with - "Avr:" (compressing | decompressing: usage, R/U, rating) and "Tot:" (usage, R/U, rating) -
    /// or null when either is missing.</summary>
    public static (double Compress, double Decompress, double Total)? Parse(string output)
    {
        var avr = Lines(output).FirstOrDefault(l => l.StartsWith("Avr:", StringComparison.Ordinal)); var tot = Lines(output).FirstOrDefault(l => l.StartsWith("Tot:", StringComparison.Ordinal));
        if (avr is null || tot is null || avr.Split('|') is not [var c, var d]) return null;
        double? Last(string part) { var n = Numbers(part); return n.Count >= 3 ? n[^1] : null; }
        return Last(c) is { } cr && Last(d) is { } dr && Last(tot) is { } tr ? (cr, dr, tr) : null;
    }

    private static IEnumerable<string> Lines(string s) => s.Split('\n').Select(l => l.Trim());
    private static List<double> Numbers(string s) => [.. NumberRun().Matches(s).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture))];
    [GeneratedRegex(@"\d+")] private static partial Regex NumberRun();
    [GeneratedRegex(@"7-Zip[^\r\n]*?\d+\.\d+[^\r\n:]*")] private static partial Regex VersionLine();
}
