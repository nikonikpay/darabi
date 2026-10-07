using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>The GPU cannot run this workload at all (no DXR 1.1, no DirectML, no FP16...): the benchmark is Unsupported, not failed.</summary>
internal sealed class GpuUnsupportedException(string message) : Exception(message);

/// <summary>The device was lost mid-run (driver reset, hang, overheating): evidence about the card.</summary>
internal sealed class GpuLostException(string message) : Exception(message);

/// <summary>The card computed a known result wrongly: evidence about the card.</summary>
internal sealed class GpuWrongResultException(string message) : Exception(message);

/// <summary>What an exception from a GPU run says about the card. Only a lost device or a wrong result is the card's; anything else (a window, a
/// shader, a file the program needed) is the program's and is never reported as the card failing.</summary>
internal static class GpuFault
{
    public enum Kind { Lost, Wrong, Unsupported, Cancelled, Internal }
    // DXGI_ERROR_DEVICE_REMOVED, _HUNG, _RESET, DRIVER_INTERNAL_ERROR
    private static readonly int[] LostCodes = [unchecked((int)0x887A0005), unchecked((int)0x887A0006), unchecked((int)0x887A0007), unchecked((int)0x887A0020)];
    public static Kind Of(Exception e) => e switch
    {
        OperationCanceledException => Kind.Cancelled,
        GpuUnsupportedException => Kind.Unsupported,
        GpuLostException => Kind.Lost,
        GpuWrongResultException => Kind.Wrong,
        SharpGen.Runtime.SharpGenException s when LostCodes.Contains(s.HResult) => Kind.Lost,
        _ => Kind.Internal,
    };
}

/// <summary>What the three GPU benchmarks share: picking the adapter, running on a worker thread, turning a lost device into
/// Failed and a missing feature into Unsupported, and adding the GPU's own clock, power and temperature for the run.</summary>
internal static class GpuBenchmark
{
    public static Task<BenchmarkResult> RunAsync(TestDefinition spec, TestExecutionRequest request, Func<D3D12Session, (List<BenchmarkMetric> Metrics, string Detail)> body, (int Width, int Height)? resolution = null, bool ownThread = false)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(BenchmarkResult.Unsupported(spec.Id, started, "Duration must be positive."));
        var device = GpuDevices.Resolve(request, spec);
        if (device is null) return Task.FromResult(BenchmarkResult.Unsupported(spec.Id, started, GpuDevices.NoGpu));
        // A run that opens a window creates it, pumps it and closes it on one thread: its own, not a pool thread.
        Func<Func<BenchmarkResult>, Task<BenchmarkResult>> start = work =>
        {
            if (!ownThread) return Task.Run(work, CancellationToken.None);
            var done = new TaskCompletionSource<BenchmarkResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => done.SetResult(work())) { IsBackground = true, Name = "Mazesta GPU benchmark" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            return done.Task;
        };
        return start(() =>
        {
            try
            {
                using var session = new D3D12Session(device);
                var (metrics, detail) = body(session);
                var finished = request.Clock.UtcNow;
                var node = Node(request, session.AdapterName);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuCoreClock, started, finished, "Bench_Gpu_Clock", Unit.MegaHertz, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuCoreClock, started, finished, "Bench_Gpu_ClockMax", Unit.MegaHertz, peak: true, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuMemoryClock, started, finished, "Bench_Gpu_MemClock", Unit.MegaHertz, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuMemoryClock, started, finished, "Bench_Gpu_MemClockMax", Unit.MegaHertz, peak: true, node: node);
                metrics.AddFirst(request, HardwareKind.Gpu, started, finished, "Bench_Gpu_LoadMax", Unit.Percent, true, node, SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuPower, started, finished, "Bench_Gpu_PowerMax", Unit.Watt, peak: true, node: node);
                if (HostMetrics.Gigabytes(request.Engine, HardwareKind.Gpu, SensorRole.GpuVramUsed, node, started, finished) is { } vram) metrics.Add(new("Bench_Gpu_VramUsedMax", vram.Max, "GB"));
                metrics.AddFirst(request, HardwareKind.Gpu, started, finished, "Bench_Gpu_Load", Unit.Percent, false, node, SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuPower, started, finished, "Bench_Gpu_Power", Unit.Watt, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuVoltage, started, finished, "Bench_Gpu_Voltage", Unit.Volt, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuCoreTemp, started, finished, "Bench_Gpu_TempAvg", Unit.Celsius, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuCoreTemp, started, finished, "Bench_Gpu_TempMax", Unit.Celsius, peak: true, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuHotSpotTemp, started, finished, "Bench_Gpu_HotSpotMax", Unit.Celsius, peak: true, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuVramTemp, started, finished, "Bench_Gpu_VramTempMax", Unit.Celsius, peak: true, node: node);
                metrics.AddSensor(request, HardwareKind.Gpu, SensorRole.GpuFanPercent, started, finished, "Bench_Gpu_Fan", Unit.Percent, node: node);
                return new BenchmarkResult(spec.Id, BenchmarkStatus.Completed, started, finished, metrics, $"{detail}; on {session.AdapterName}",
                    [.. resolution is { } r ? [new SpecItem(BenchmarkDetails.RunGroup, "Bench_Set_Resolution", $"{r.Width}×{r.Height}")] : Array.Empty<SpecItem>(), .. session.Setup]);
            }
            catch (Exception e)
            {
                return GpuFault.Of(e) switch
                {
                    GpuFault.Kind.Cancelled => BenchmarkResult.Cancelled(spec.Id, started, request.Clock.UtcNow),
                    GpuFault.Kind.Unsupported => BenchmarkResult.Unsupported(spec.Id, started, e.Message),
                    GpuFault.Kind.Lost or GpuFault.Kind.Wrong => BenchmarkResult.Failed(spec.Id, started, request.Clock.UtcNow, $"GPU error during the run: {e.Message}"),
                    _ => BenchmarkResult.Error(spec.Id, started, request.Clock.UtcNow, e),
                };
            }
        });
    }

    /// <summary>The monitor's node of the adapter a run used: the only GPU there is, or the one of the same name. With two GPUs and no match
    /// nothing is read, rather than the other card's readings.</summary>
    internal static Func<HardwareNode, bool> Node(TestExecutionRequest request, string adapter) => GpuDevices.SensorNode(request.Engine, adapter);
}
