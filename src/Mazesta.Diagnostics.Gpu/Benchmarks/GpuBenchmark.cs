using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>The GPU cannot run this workload at all (no DXR 1.1, no DirectML, no FP16...): the benchmark is Unsupported, not failed.</summary>
internal sealed class GpuUnsupportedException(string message) : Exception(message);

/// <summary>What the three GPU benchmarks share: picking the adapter, running on a worker thread, turning a lost device into
/// Failed and a missing feature into Unsupported, and adding the GPU's own clock, power and temperature for the run.</summary>
internal static class GpuBenchmark
{
    public static Task<BenchmarkResult> RunAsync(TestDefinition spec, TestExecutionRequest request, Func<D3D12Session, (List<BenchmarkMetric> Metrics, string Detail)> body)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(BenchmarkResult.Unsupported(spec.Id, started, "Duration must be positive."));
        var device = GpuDevices.Resolve(request, spec);
        if (device is null) return Task.FromResult(BenchmarkResult.Unsupported(spec.Id, started, GpuDevices.NoGpu));
        return Task.Run(() =>
        {
            try
            {
                using var session = new D3D12Session(device);
                var (metrics, detail) = body(session);
                var finished = request.Clock.UtcNow;
                var node = Node(request, session.AdapterName);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuCoreClock, started, finished, "Bench_Gpu_Clock", Unit.MegaHertz, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuMemoryClock, started, finished, "Bench_Gpu_MemClock", Unit.MegaHertz, node: node);
                metrics.AddFirst(request, HardwareKind.Gpu, started, finished, "Bench_Gpu_Load", Unit.Percent, false, node, SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuPower, started, finished, "Bench_Gpu_Power", Unit.Watt, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuVoltage, started, finished, "Bench_Gpu_Voltage", Unit.Volt, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuCoreTemp, started, finished, "Bench_Gpu_TempAvg", Unit.Celsius, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuCoreTemp, started, finished, "Bench_Gpu_TempMax", Unit.Celsius, peak: true, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuHotSpotTemp, started, finished, "Bench_Gpu_HotSpotMax", Unit.Celsius, peak: true, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuVramTemp, started, finished, "Bench_Gpu_VramTempMax", Unit.Celsius, peak: true, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuFanPercent, started, finished, "Bench_Gpu_Fan", Unit.Percent, node: node);
                return new BenchmarkResult(spec.Id, BenchmarkStatus.Completed, started, finished, metrics, $"{detail}; on {session.AdapterName}");
            }
            catch (OperationCanceledException) { return BenchmarkResult.Cancelled(spec.Id, started, request.Clock.UtcNow); }
            catch (GpuUnsupportedException e) { return BenchmarkResult.Unsupported(spec.Id, started, e.Message); }
            catch (Exception e) { return BenchmarkResult.Failed(spec.Id, started, request.Clock.UtcNow, $"GPU error during the run: {e.GetType().Name}: {e.Message}"); }
        }, CancellationToken.None);
    }

    /// <summary>The monitor's node of the adapter a run used: the only GPU there is, or the one of the same name. With two GPUs and no match
    /// nothing is read, rather than the other card's readings.</summary>
    internal static Func<HardwareNode, bool> Node(TestExecutionRequest request, string adapter)
    {
        var gpus = request.Engine?.Hardware.Where(n => n.Kind == HardwareKind.Gpu && n.ParentId is null).ToList() ?? [];
        if (gpus.Count == 1) { var only = gpus[0].Id; return n => n.Id == only || n.ParentId == only; }
        string want = BenchmarkPeers.PartName(adapter);
        var match = gpus.FirstOrDefault(n => string.Equals(BenchmarkPeers.PartName(n.Name), want, StringComparison.OrdinalIgnoreCase))?.Id;
        return n => match is { } id && (n.Id == id || n.ParentId == id);
    }
}
