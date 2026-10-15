using System.Diagnostics; using ComputeSharp; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Gpu;

/// <summary>
/// VRAM test (spec §10): fills as much of the card's memory as it can safely use with a data pattern,
/// then on every following pass verifies the previous pattern over <b>every allocated cell</b> - the
/// comparison runs on the GPU and counts each difference atomically - before writing the next. Eight patterns
/// rotate (see <see cref="VramPatternShader"/>: address and inverse, 0xAA… and 0x55…, walking 1 and 0, a stride-order write, a scramble). The budget comes from the card's live free-VRAM
/// sensor when there is one: a buffer larger than free VRAM would silently spill into system RAM and the
/// test would be checking the wrong memory.
/// </summary>
public sealed class GpuVramExecutor : ITestExecutor, ITestAvailability
{
    public const string SizeOption = "sizeMb";
    public static readonly TestDefinition Definition = new(new TestId("gpu.vram"), "Test_Gpu_Vram", 300,
        [GpuDevices.Option, new TestOption(SizeOption, "Test_Option_VramMb", TestOptionKind.Integer, "0")]);   // 0 = automatic
    TestDefinition ITestExecutor.Definition => Definition;
    public Unavailability? CheckAvailability(TestOptions options) => GpuFeatures.GpuAvailability(options);

    internal const int Width = 4096, ChunkElements = 64 << 20;   // 256 MiB per buffer
    // The test itself uses 64 MiB buffers so the card can be filled to within ~200 MiB of its budget, like a stress tool that fills until the driver says no.
    private const int TestElements = 16 << 20; private const long ChunkBytes = TestElements * 4L;

    /// <summary>What may be used: 98 % of the free VRAM minus a 128 MiB margin for the display and other apps; without a live free-VRAM reading, 80 % of the dedicated memory.
    /// Never more than 98 % of what Windows lets this process keep resident on the card (<paramref name="residentBytes"/>, DXGI's budget), past which it would move
    /// the test's buffers out to system RAM.</summary>
    internal static long Budget(double? freeVramMb, long dedicatedBytes, int requestedMb = 0, long? residentBytes = null)
    {
        long budget = Math.Max(0, freeVramMb is { } free ? (long)(free * 1024 * 1024 * 0.98) - (128L << 20) : (long)(dedicatedBytes * 0.8));
        if (residentBytes is { } resident) budget = Math.Min(budget, (long)(resident * 0.98));
        return requestedMb > 0 ? Math.Min(budget, (long)requestedMb << 20) : budget;
    }

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        var options = request.Options ?? TestOptions.None(Definition);
        var device = GpuDevices.Resolve(request, Definition);
        if (device is null) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, GpuDevices.NoGpu));
        long budget = Budget(SensorEvidence.Latest(request.Engine, HardwareKind.Gpu, SensorRole.GpuVramFree, started, GpuDevices.SensorNode(request.Engine, device.Name)), (long)device.DedicatedMemorySize, options.GetInt(SizeOption), GpuFeatures.ResidentBudgetBytes(device));
        if (budget < ChunkBytes) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, $"Only {budget >> 20} MiB of VRAM can safely be tested; at least {ChunkBytes >> 20} MiB is needed."));
        return Task.Run(() => Run(request, device, budget, started, ct), CancellationToken.None);
    }

    private static TestRunResult Run(TestExecutionRequest request, GraphicsDevice device, long budget, DateTimeOffset started, CancellationToken ct)
    {
        var buffers = new List<ReadWriteBuffer<uint>>(); long errors = 0; uint passes = 0; bool refused = false; double? bandwidth = null;
        var clock = Stopwatch.StartNew();
        try
        {
            // One error counter per buffer: a buffer has 2^24 cells, so its count cannot overflow, where one counter for a whole card that is
            // wrong everywhere (2^32 cells on a 16 GB card) would wrap round - possibly to zero or below, a fault read as a clean pass.
            using var counter = device.AllocateReadWriteBuffer<int>((int)Math.Max(1, budget / ChunkBytes));
            for (long allocated = 0; allocated + ChunkBytes <= budget; allocated += ChunkBytes)
            {
                ct.ThrowIfCancellationRequested();
                try { buffers.Add(device.AllocateReadWriteBuffer<uint>(TestElements)); }
                catch (Exception) when (buffers.Count > 0) { refused = true; break; }   // the driver refused more: test what was granted
                Dispatch(device, buffers[^1], counter, buffers.Count - 1, 0, verify: false);
            }
            if (buffers.Count == 0) return TestRunResult.Unsupported(Definition.Id, started, "The driver refused to allocate any VRAM buffer.");
            request.Note("Log_Vram_Allocated", "budget = 0.98 × free VRAM (this card's sensor) − 128 MiB, else 0.8 × dedicated;  64 MiB per buffer", (long)buffers.Count * (ChunkBytes >> 20), device.Name, buffers.Count);
            var duration = TimeSpan.FromSeconds(request.DurationSeconds); var timed = Stopwatch.StartNew(); long moved = 0; var bad = new int[counter.Length]; var zeros = new int[counter.Length];
            // A whole pass (every buffer verified, then rewritten) goes to the card in one submission and the error count is read back once:
            // waiting for the CPU after every buffer left the memory idle between them, so it ran cooler than under a game or FurMark.
            do
            {
                ct.ThrowIfCancellationRequested();
                counter.CopyFrom(zeros);
                using (var context = device.CreateComputeContext())
                {
                    // The buffers are independent: all are read first with no wait between them, then all rewritten, so the memory is never drained between two buffers.
                    for (int b = 0; b < buffers.Count; b++) { context.For(Width, TestElements / Width, Shader(buffers[b], counter, b, passes, verify: true)); }
                    for (int b = 0; b < buffers.Count; b++) context.Barrier(buffers[b]);
                    for (int b = 0; b < buffers.Count; b++) context.For(Width, TestElements / Width, Shader(buffers[b], counter, b, passes + 1, verify: false));
                    for (int b = 0; b < buffers.Count; b++) context.Barrier(buffers[b]);
                }
                counter.CopyTo(bad); errors += Total(bad); moved += 2L * buffers.Count * ChunkBytes;
                passes++;
                request.Note("Log_Vram_Pass", $"on the GPU, for every cell: if (cell ≠ pattern[p]) errors++ (atomic);  then write pattern[p+1]   pattern {passes % VramPattern.Kinds}: {VramPattern.Names[passes % VramPattern.Kinds]}", passes, errors);
                request.Progress?.Invoke(new TestProgress(Math.Clamp(timed.Elapsed / duration, 0, 1), "Test_Status_Running"));
            }
            while (timed.Elapsed < duration);
            bandwidth = moved / Math.Max(0.001, timed.Elapsed.TotalSeconds) / 1e9;
            for (int b = 0; b < buffers.Count; b++) errors += Verify(device, buffers[b], counter, b, passes);
        }
        catch (OperationCanceledException) { return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, Describe(device, buffers.Count, passes, bandwidth, request, started, refused)); }
        catch (Exception ex) { return new(Definition.Id, TestOutcome.Failed, started, request.Clock.UtcNow, errors + 1, $"GPU error during the VRAM test: {ex.GetType().Name}: {ex.Message}"); }
        // ComputeSharp 3.2 returns a descriptor twice after a refused allocation, so its pool throws on the last return - with the memory already freed; the finished run is not a crash.
        finally { foreach (var b in buffers) { try { b.Dispose(); } catch (InvalidOperationException) { } } }
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, errors, Describe(device, buffers.Count, passes, bandwidth, request, started, refused));
    }

    private static VramPatternShader Shader(ReadWriteBuffer<uint> buffer, ReadWriteBuffer<int> counter, int chunk, uint pass, bool verify)
        => new(buffer, counter, chunk, Width, pass, (uint)chunk * (uint)TestElements, verify ? 1 : 0, TestElements - 1);

    /// <summary>The per-buffer counts added up in 64 bits.</summary>
    internal static long Total(ReadOnlySpan<int> counts) { long sum = 0; foreach (int c in counts) sum += c; return sum; }
    private static void Dispatch(GraphicsDevice device, ReadWriteBuffer<uint> buffer, ReadWriteBuffer<int> counter, int chunk, uint pass, bool verify)
        => device.For(Width, TestElements / Width, Shader(buffer, counter, chunk, pass, verify));

    private static long Verify(GraphicsDevice device, ReadWriteBuffer<uint> buffer, ReadWriteBuffer<int> counter, int chunk, uint pass)
    {
        counter.CopyFrom(new int[counter.Length]);
        Dispatch(device, buffer, counter, chunk, pass, verify: true);
        var bad = new int[counter.Length]; counter.CopyTo(bad);   // the copy waits for the verification dispatch
        return bad[chunk];
    }

    private static string Describe(GraphicsDevice device, int chunks, uint passes, double? bandwidth, TestExecutionRequest request, DateTimeOffset started, bool refused)
    {
        var finished = request.Clock.UtcNow; var card = GpuDevices.SensorNode(request.Engine, device.Name);
        return SensorEvidence.Join($"VRAM pattern test on {device.Name}", $"tested={chunks * (ChunkBytes >> 20)} MiB in {chunks} buffers", $"passes={passes}",
            bandwidth is { } gb ? $"{gb:F0} GB/s" : null, refused ? $"the driver refused more VRAM after {chunks} buffers (the part it granted was tested)" : null,
            SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuVramTemp, started, finished, card, null)?.Format("VRAM temperature", "°C", includeMax: true),
            SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuMemoryClock, started, finished, card, null)?.Format("GPU memory clock", " MHz", includeMax: true),
            SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuLoadMemoryController, started, finished, card, null)?.Format("memory controller load", "%"),
            SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuVramUsed, started, finished, card, null)?.Format("VRAM in use", " MB", includeMax: true));
    }
}
