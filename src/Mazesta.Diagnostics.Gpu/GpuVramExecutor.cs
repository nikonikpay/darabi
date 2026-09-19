using System.Diagnostics; using ComputeSharp; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Gpu;

/// <summary>
/// VRAM test (spec §10): fills as much of the card's memory as it can safely use with a data pattern,
/// then on every following pass verifies the previous pattern over <b>every allocated cell</b> - the
/// comparison runs on the GPU and counts each difference atomically - before writing the next. Four patterns
/// rotate (address-derived, its inverse, 0xAA…, 0x55…). The budget comes from the card's live free-VRAM
/// sensor when there is one: a buffer larger than free VRAM would silently spill into system RAM and the
/// test would be checking the wrong memory.
/// </summary>
public sealed class GpuVramExecutor : ITestExecutor
{
    public const string SizeOption = "sizeMb";
    public static readonly TestDefinition Definition = new(new TestId("gpu.vram"), "Test_Gpu_Vram", 60,
        [GpuDevices.Option, new TestOption(SizeOption, "Test_Option_VramMb", TestOptionKind.Integer, "0")]);   // 0 = automatic
    TestDefinition ITestExecutor.Definition => Definition;

    internal const int Width = 4096, ChunkElements = 64 << 20;   // 256 MiB per buffer
    private const long ChunkBytes = ChunkElements * 4L;

    /// <summary>What may be used: 90 % of the free VRAM minus a 512 MiB margin for the display and other apps; without a live free-VRAM reading, 60 % of the dedicated memory.</summary>
    internal static long Budget(double? freeVramMb, long dedicatedBytes, int requestedMb = 0)
    {
        long budget = Math.Max(0, freeVramMb is { } free ? (long)(free * 1024 * 1024 * 0.9) - (512L << 20) : (long)(dedicatedBytes * 0.6));
        return requestedMb > 0 ? Math.Min(budget, (long)requestedMb << 20) : budget;
    }

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        var options = request.Options ?? TestOptions.None(Definition);
        var device = GpuDevices.Resolve(options.Get(GpuDevices.OptionKey));
        if (device is null) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "No DirectX 12 hardware GPU is available."));
        long budget = Budget(SensorEvidence.Latest(request.Engine, HardwareKind.Gpu, SensorRole.GpuVramFree), (long)device.DedicatedMemorySize, options.GetInt(SizeOption));
        if (budget < ChunkBytes) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, $"Only {budget >> 20} MiB of VRAM can safely be tested; at least {ChunkBytes >> 20} MiB is needed."));
        return Task.Run(() => Run(request, device, budget, started, ct), CancellationToken.None);
    }

    private static TestRunResult Run(TestExecutionRequest request, GraphicsDevice device, long budget, DateTimeOffset started, CancellationToken ct)
    {
        var buffers = new List<ReadWriteBuffer<uint>>(); long errors = 0; uint passes = 0;
        var clock = Stopwatch.StartNew();
        try
        {
            using var counter = device.AllocateReadWriteBuffer<int>(1);
            for (long allocated = 0; allocated + ChunkBytes <= budget; allocated += ChunkBytes)
            {
                ct.ThrowIfCancellationRequested();
                try { buffers.Add(device.AllocateReadWriteBuffer<uint>(ChunkElements)); }
                catch (Exception) when (buffers.Count > 0) { break; }   // the driver refused more: test what was granted
                Dispatch(device, buffers[^1], counter, buffers.Count - 1, 0, verify: false);
            }
            if (buffers.Count == 0) return TestRunResult.Unsupported(Definition.Id, started, "The driver refused to allocate any VRAM buffer.");
            var duration = TimeSpan.FromSeconds(request.DurationSeconds); var timed = Stopwatch.StartNew();
            do
            {
                for (int b = 0; b < buffers.Count; b++)
                {
                    ct.ThrowIfCancellationRequested();
                    errors += Verify(device, buffers[b], counter, b, passes);
                    Dispatch(device, buffers[b], counter, b, passes + 1, verify: false);
                }
                passes++;
                request.Progress?.Invoke(new TestProgress(Math.Clamp(timed.Elapsed / duration, 0, 1), "Test_Status_Running"));
            }
            while (timed.Elapsed < duration);
            for (int b = 0; b < buffers.Count; b++) errors += Verify(device, buffers[b], counter, b, passes);
        }
        catch (OperationCanceledException) { return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, Describe(device, buffers.Count, passes, request, started)); }
        catch (Exception ex) { return new(Definition.Id, TestOutcome.Failed, started, request.Clock.UtcNow, errors + 1, $"GPU error during the VRAM test: {ex.GetType().Name}: {ex.Message}"); }
        finally { foreach (var b in buffers) b.Dispose(); }
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, errors, Describe(device, buffers.Count, passes, request, started));
    }

    private static void Dispatch(GraphicsDevice device, ReadWriteBuffer<uint> buffer, ReadWriteBuffer<int> counter, int chunk, uint pass, bool verify)
        => device.For(Width, ChunkElements / Width, new VramPatternShader(buffer, counter, Width, pass, (uint)chunk * (uint)ChunkElements, verify ? 1 : 0));

    private static long Verify(GraphicsDevice device, ReadWriteBuffer<uint> buffer, ReadWriteBuffer<int> counter, int chunk, uint pass)
    {
        counter.CopyFrom([0]);
        Dispatch(device, buffer, counter, chunk, pass, verify: true);
        var bad = new int[1]; counter.CopyTo(bad);   // the copy waits for the verification dispatch
        return bad[0];
    }

    private static string Describe(GraphicsDevice device, int chunks, uint passes, TestExecutionRequest request, DateTimeOffset started)
        => SensorEvidence.Join($"VRAM pattern test on {device.Name}", $"tested={chunks * (ChunkBytes >> 20)} MiB in {chunks} buffers", $"passes={passes}",
            SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuVramUsed, started, request.Clock.UtcNow)?.Format("VRAM in use", " MB", includeMax: true));
}
