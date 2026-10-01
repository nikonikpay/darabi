using System.Diagnostics; using System.Globalization; using System.Text.Json; using System.Text.RegularExpressions; using Mazesta.Core.Ai; using Mazesta.Core.Hardware;
using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.Diagnostics.Ai;

/// <summary>What llama-bench measured: prompt processing (reading the question) and generation (writing the answer), in tokens a second.</summary>
public sealed record LlamaBenchResult(double? PromptTokensPerSecond, double? GenerationTokensPerSecond, double? PromptSpread, double? GenerationSpread,
    string? Backend, string? Devices, int? Threads, string? ModelType, int? Build);

/// <summary>
/// Real inference speed of a downloaded language model, measured by llama.cpp's own benchmark (llama-bench): a 512-token prompt and 128
/// generated tokens, three times each after a warm-up, as the llama.cpp project publishes its numbers - so a result here can be set beside
/// theirs. On the GPU (Vulkan) the model is fitted by llama.cpp itself (--fit-target 1024 MiB): wholly on the card when it fits, else as many
/// layers as fit, the rest on the CPU; on the CPU no device is used. Nothing is estimated here - the fit page estimates, this measures.
/// </summary>
public sealed partial class LlamaBenchmark(AiFiles files) : IBenchmark
{
    public const string ModelOption = "model", DeviceOption = "device", GpuOption = "gpu";
    public static readonly TestDefinition Spec = new(new TestId("bench.ai.llm"), "Bench_Ai_Llm", 60,
        [new(ModelOption, "Ai_Option_Model", TestOptionKind.Text, ""), new(DeviceOption, "Ai_Option_Device", TestOptionKind.Text, "gpu"), new(GpuOption, "Test_Option_Gpu", TestOptionKind.Text, "")]);
    public TestDefinition Definition => Spec;
    public HardwareKind Component => HardwareKind.Gpu;
    private const int Repetitions = 3;

    public async Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow; var opts = request.Options ?? TestOptions.None(Spec);
        var model = AiCatalog.Find(opts.Get(ModelOption));
        if (model is null || !files.HasModel(model)) return BenchmarkResult.Unsupported(Spec.Id, started, "The model is not downloaded.");
        if (!files.HasRuntime) return BenchmarkResult.Unsupported(Spec.Id, started, "The llama.cpp runtime is not downloaded.");
        bool gpu = opts.Get(DeviceOption) != "cpu";
        string? device = null; string twin = "";
        if (gpu)
        {
            var devices = await ListDevicesAsync(ct).ConfigureAwait(false);
            if (devices.Count == 0) return BenchmarkResult.Unsupported(Spec.Id, started, "No GPU can run llama.cpp's Vulkan backend (no Vulkan device found; the graphics driver may be missing or too old).");
            string want = opts.Get(GpuOption);
            // The card chosen, or with no choice the first; a chosen card that is not there is an error, never another card measured in its place.
            var named = want.Length == 0 ? devices.Take(1).ToList()
                : devices.Where(d => want.Contains(d.Name, StringComparison.OrdinalIgnoreCase) || d.Name.Contains(want, StringComparison.OrdinalIgnoreCase)).ToList();
            if (named.Count == 0) return BenchmarkResult.Unsupported(Spec.Id, started, $"The chosen graphics card ({want}) is not among llama.cpp's Vulkan devices ({string.Join(", ", devices.Select(d => d.Name))}).");
            device = named[0].Id;
            if (named.Count > 1) twin = $" · one of {named.Count} cards of that name ({named[0].Id}); the card's sensors are not read, as the two can not be told apart";
        }
        string[] args = ["-m", files.ModelPath(model), "-dev", device ?? "none", .. gpu ? new[] { "-fitt", "1024" } : ["-ngl", "0"],
            "-p", "512", "-n", "128", "-r", Repetitions.ToString(CultureInfo.InvariantCulture), "-o", "json", "--progress"];
        request.Report(0.01);
        var (code, output, errors) = await RunAsync(args, line => { if (Progress(line) is { } p) request.Report(p); }, ct).ConfigureAwait(false);
        var finished = request.Clock.UtcNow;
        if (ct.IsCancellationRequested) return BenchmarkResult.Cancelled(Spec.Id, started, finished);
        var result = code == 0 ? Parse(output) : null;
        if (result?.GenerationTokensPerSecond is null)
            return BenchmarkResult.Failed(Spec.Id, started, finished, $"llama-bench exited with {code}: " + string.Join(" | ", errors.TakeLast(4)));

        List<BenchmarkMetric> metrics = [];
        if (result.PromptTokensPerSecond is { } pp) metrics.Add(new("Bench_Ai_Prompt", pp, "tok/s"));
        metrics.Add(new("Bench_Ai_Gen", result.GenerationTokensPerSecond.Value, "tok/s"));
        if (gpu && twin.Length == 0)
        {
            metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuPower, started, finished, "Bench_Gpu_Power", Unit.Watt);
            metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuCoreTemp, started, finished, "Bench_Gpu_TempMax", Unit.Celsius, peak: true);
        }
        else
        {
            metrics.AddSensor(request, HardwareKind.Cpu, SensorRole.CpuPackagePower, started, finished, "Bench_Cpu_Power", Unit.Watt);
        }
        string detail = $"{model.Name} {model.Quant} · llama.cpp {AiCatalog.Runtime.Build} " + (gpu ? $"{result.Backend} on {result.Devices}" : "on the CPU") + $", {result.Threads} CPU threads"
            + $" · prompt 512 tokens ±{result.PromptSpread:0.#}, generation 128 tokens ±{result.GenerationSpread:0.#} tok/s, {Repetitions} runs each"
            + (gpu ? " · fitted by llama.cpp (--fit-target 1024 MiB): layers that do not fit on the card run on the CPU" : "") + twin;
        return new BenchmarkResult(Spec.Id, BenchmarkStatus.Completed, started, finished, metrics, detail);
    }

    /// <summary>llama-bench's JSON: one entry for the prompt test (n_prompt &gt; 0) and one for generation (n_gen &gt; 0).</summary>
    public static LlamaBenchResult? Parse(string json)
    {
        int start = json.IndexOf('[');
        if (start < 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(json[start..]);
            double? pp = null, tg = null, ppSd = null, tgSd = null; string? backend = null, devices = null, type = null; int? threads = null, build = null;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                double ts = e.GetProperty("avg_ts").GetDouble(), sd = e.TryGetProperty("stddev_ts", out var s) ? s.GetDouble() : 0;
                if (e.GetProperty("n_gen").GetInt32() > 0) { tg = ts; tgSd = sd; } else if (e.GetProperty("n_prompt").GetInt32() > 0) { pp = ts; ppSd = sd; }
                backend = e.TryGetProperty("backends", out var b) ? b.GetString() : null; devices = e.TryGetProperty("devices", out var d) ? d.GetString() : null;
                threads = e.TryGetProperty("n_threads", out var t) ? t.GetInt32() : null; type = e.TryGetProperty("model_type", out var m) ? m.GetString() : null;
                build = e.TryGetProperty("build_number", out var n) ? n.GetInt32() : null;
            }
            return new(pp, tg, ppSd, tgSd, backend, devices, threads, type, build);
        }
        catch (JsonException) { return null; }
    }

    [GeneratedRegex(@"benchmark (\d+)/(\d+): (?:warmup \w+ run|\w+ run (\d+)/(\d+))")] private static partial Regex ProgressLine();
    /// <summary>The share done from a --progress line ("benchmark 2/2: generation run 1/3"); the warm-up counts as a run.</summary>
    public static double? Progress(string line)
    {
        var m = ProgressLine().Match(line);
        if (!m.Success) return null;
        int bench = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), of = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        int run = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0, runs = m.Groups[4].Success ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : Repetitions;
        return ((bench - 1) * (runs + 1) + run + 1) / (double)(of * (runs + 1));
    }

    [GeneratedRegex(@"^\s*(Vulkan\d+):\s*(.+?)\s+\(\d+ MiB")] private static partial Regex DeviceLine();
    /// <summary>The Vulkan devices llama.cpp sees ("Vulkan0: NVIDIA GeForce RTX 3090 (24539 MiB, 23753 MiB free)").</summary>
    public static IReadOnlyList<(string Id, string Name)> Devices(string listing)
        => [.. listing.Split('\n').Select(l => DeviceLine().Match(l)).Where(m => m.Success).Select(m => (m.Groups[1].Value, m.Groups[2].Value))];

    public async Task<IReadOnlyList<(string Id, string Name)>> ListDevicesAsync(CancellationToken ct)
    {
        var (_, output, errors) = await RunAsync(["--list-devices"], null, ct).ConfigureAwait(false);
        return Devices(output + "\n" + string.Join("\n", errors));
    }

    private async Task<(int Code, string Output, List<string> Errors)> RunAsync(string[] args, Action<string>? onError, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(files.BenchExe) { WorkingDirectory = files.RuntimeDir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = new Process { StartInfo = psi };
        List<string> errors = [];
        p.ErrorDataReceived += (_, e) => { if (e.Data is null) return; lock (errors) errors.Add(e.Data); onError?.Invoke(e.Data); };
        p.Start(); p.BeginErrorReadLine();
        var output = p.StandardOutput.ReadToEndAsync(CancellationToken.None);
        try { await p.WaitForExitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } await p.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
        string text = await output.ConfigureAwait(false);
        lock (errors) return (p.ExitCode, text, [.. errors]);
    }
}
