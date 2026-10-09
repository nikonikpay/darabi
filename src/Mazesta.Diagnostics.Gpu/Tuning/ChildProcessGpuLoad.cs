using System.Diagnostics; using System.Globalization; using System.Text; using System.Text.Json; using Mazesta.Core.Tuning; using Mazesta.Diagnostics.Tuning;
namespace Mazesta.Diagnostics.Gpu.Tuning;

/// <summary>
/// The tuner's loads, run in a process of their own (the app's own exe started with <see cref="Argument"/>). A driver reset that a search provokes on purpose - the edge of
/// stability is what it looks for - leaves DirectX in the process that was drawing when it happened without any adapter ("no DirectX 12 hardware GPU"), for good: the next
/// step, and every test after it, would fail until the app was started again. Here each load starts with a fresh DirectX, so the search goes on after a crash, and the app
/// itself never held the card that crashed. The answer comes back in a file; cancelling ends the process.
/// </summary>
public sealed class ChildProcessGpuLoad(string gpuName) : IGpuLoad
{
    public const string Argument = "--gpuload";
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(90);

    public LoadRunResult Run(GpuLoadKind kind, TimeSpan duration, TimeSpan settle, CancellationToken ct) => Run(kind, duration, settle, false, ct);

    public LoadRunResult Run(GpuLoadKind kind, TimeSpan duration, TimeSpan settle, bool rayTraced, CancellationToken ct) => Spawn(kind.ToString(), duration, settle, rayTraced, ct);

    public bool SupportsRayTracing { get { var r = Spawn("RayTracingCheck", TimeSpan.Zero, TimeSpan.Zero, false, CancellationToken.None); return r.Error is null && r.Throughput > 0; } }

    private LoadRunResult Spawn(string kind, TimeSpan duration, TimeSpan settle, bool rayTraced, CancellationToken ct)
    {
        string answer = Path.Combine(Path.GetTempPath(), $"mazesta-gpuload-{Guid.NewGuid():N}.json");
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string a in new[] { Argument, kind, ((long)duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture), ((long)settle.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
            rayTraced ? "1" : "0", answer, Convert.ToBase64String(Encoding.UTF8.GetBytes(gpuName)) }) info.ArgumentList.Add(a);
        try
        {
            using var child = Process.Start(info) ?? throw new InvalidOperationException("The load process did not start.");
            var limit = Stopwatch.StartNew();
            while (!child.WaitForExit(250))
            {
                if (ct.IsCancellationRequested) { Kill(child); throw new OperationCanceledException(ct); }
                if (limit.Elapsed > duration + settle + Slack) { Kill(child); return new(0, 0, true, "The load process gave no answer in time and was ended."); }
            }
            return File.Exists(answer) ? JsonSerializer.Deserialize<LoadRunResult>(File.ReadAllText(answer))! : new(0, 0, true, $"The load process ended without an answer (exit code {child.ExitCode}).");
        }
        finally { try { File.Delete(answer); } catch (IOException) { } }
    }

    private static void Kill(Process p) { try { p.Kill(entireProcessTree: true); p.WaitForExit(10000); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { } }

    /// <summary>The other end, run by the app's <c>Main</c> when it is started with <see cref="Argument"/>: arguments are kind, milliseconds, settle milliseconds, ray tracing, the answer's file and the card's name.</summary>
    public static int RunChild(string[] args)
    {
        string answer = args[5]; LoadRunResult result;
        try
        {
            var load = new ComputeGpuLoad(Encoding.UTF8.GetString(Convert.FromBase64String(args[6])));
            result = args[1] == "RayTracingCheck" ? new(load.SupportsRayTracing ? 1 : 0, 0, false, null)
                : load.Run(Enum.Parse<GpuLoadKind>(args[1]), TimeSpan.FromMilliseconds(long.Parse(args[2], CultureInfo.InvariantCulture)), TimeSpan.FromMilliseconds(long.Parse(args[3], CultureInfo.InvariantCulture)), args[4] == "1", CancellationToken.None);
        }
        catch (Exception e) { result = new(0, 0, true, $"{e.GetType().Name}: {e.Message}"); }
        File.WriteAllText(answer, JsonSerializer.Serialize(result));
        return 0;
    }
}
