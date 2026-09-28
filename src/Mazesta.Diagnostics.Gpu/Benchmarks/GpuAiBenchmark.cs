using Mazesta.Core.Hardware; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Benchmarks; using Vortice.Direct3D12; using Vortice.DirectML;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>
/// AI inference throughput through DirectML - the GPU path Windows AI applications take (ONNX Runtime and Windows ML) - at the
/// three precision levels AI benchmarks report: FP32, FP16 and INT8. Each level runs a 4096x4096x4096 matrix multiply, the
/// operation that dominates neural-network inference, for a third of the duration. DirectML hands it to the vendor's
/// metacommands where the driver has them, so tensor/matrix cores are used when the GPU has them; the numbers are therefore
/// what an AI application on this machine would get, not the chip's theoretical peak. A precision the GPU or the installed
/// DirectML cannot run is left out and named in the detail. DirectML is the copy that ships with Windows (System32).
/// </summary>
public sealed class GpuAiBenchmark : IBenchmark, ITestAvailability
{
    public static readonly TestDefinition Spec = new(new TestId("bench.gpu.ai"), "Bench_Gpu_Ai", 60, [GpuDevices.Option]);
    public TestDefinition Definition => Spec;
    public HardwareKind Component => HardwareKind.Gpu;
    public Unavailability? CheckAvailability(TestOptions options) => GpuFeatures.GpuAvailability(options);
    private const uint Size = 4096;
    private const double OpsPerMultiply = 2.0 * Size * Size * Size;

    /// <summary>One precision level. INT8 multiplies integers into INT32 (TOPS); the float levels keep their type (TFLOPS).</summary>
    private sealed record Level(TensorDataType Input, string Key, string Name)
    {
        public bool Integer => Input == TensorDataType.Int8;
        public TensorDataType Output => Integer ? TensorDataType.Int32 : Input;
        public string Unit => Integer ? "TOPS" : "TFLOPS";
    }
    private static readonly Level[] Levels = [new(TensorDataType.Float32, "Bench_Gpu_Ai_Fp32", "FP32"), new(TensorDataType.Float16, "Bench_Gpu_Ai_Fp16", "FP16"), new(TensorDataType.Int8, "Bench_Gpu_Ai_Int8", "INT8")];

    public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => GpuBenchmark.RunAsync(Spec, request, s => Run(s, request, ct));

    private static (List<BenchmarkMetric>, string) Run(D3D12Session s, TestExecutionRequest request, CancellationToken ct)
    {
        IDMLDevice dml;
        try { dml = s.Own(DML.DMLCreateDevice(s.Device, CreateDeviceFlags.None)); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or SharpGen.Runtime.SharpGenException) { throw new GpuUnsupportedException($"DirectML is not available on this Windows installation ({e.Message})."); }

        List<BenchmarkMetric> metrics = []; List<string> skipped = [];
        double seconds = request.DurationSeconds / (double)Levels.Length;
        for (int i = 0; i < Levels.Length; i++)
        {
            var level = Levels[i];
            string? missing = !dml.CheckTensorDataTypeSupport(level.Input) ? $"{level.Name}: not supported by this GPU"
                : level.Integer && dml.CheckFeatureLevelsSupport([FeatureLevel.Level2_1]) < FeatureLevel.Level2_1 ? $"{level.Name}: needs DirectML feature level 2.1" : null;
            if (missing is not null) { skipped.Add(missing); continue; }
            int index = i;
            using (s.Scope())   // each level's matrices are freed before the next level allocates its own
                metrics.Add(new(level.Key, Measure(s, dml, level, seconds, p => request.Report((index + p) / Levels.Length), ct) * OpsPerMultiply / 1e12, level.Unit));
        }
        if (metrics.Count == 0) throw new GpuUnsupportedException("DirectML cannot run a matrix multiply at any precision on this GPU: " + string.Join("; ", skipped));
        return (metrics, $"DirectML {Size}x{Size}x{Size} matrix multiply (GEMM for FP32/FP16, integer matmul for INT8), DirectML feature level {dml.HighestFeatureLevel}"
            + (skipped.Count > 0 ? "; not measured: " + string.Join("; ", skipped) : ""));
    }

    /// <summary>Multiplies per second at one precision. Inputs hold random values (an all-zero matrix would let some GPUs idle
    /// their multipliers and inflate the result); consecutive multiplies are separated by a barrier, as layers of a network are.</summary>
    private static double Measure(D3D12Session s, IDMLDevice dml, Level level, double seconds, Action<double> progress, CancellationToken ct)
    {
        var a = Tensor(level.Input, Size); var b = Tensor(level.Input, Size); var output = Tensor(level.Output, Size); var zeroPoint = Tensor(TensorDataType.Int8, 1);
        IOperatorDescription description = level.Integer
            ? new MatrixMultiplyIntegerOperatorDescription { ATensor = new(a), AZeroPointTensor = new TensorDescription(zeroPoint), BTensor = new(b), BZeroPointTensor = new TensorDescription(zeroPoint), OutputTensor = new(output) }
            : new GeneralMatrixMultiplyOperatorDescription { ATensor = new(a), BTensor = new(b), CTensor = new TensorDescription(output), OutputTensor = new(output), Alpha = 1, Beta = 0 };
        using var op = dml.CreateOperator(new OperatorDescription(description));
        // FP16 may also accumulate in FP16, as FP16 inference does; FP32 and INT8 keep their full-precision paths.
        using var compiled = dml.CompileOperator(op, level.Input == TensorDataType.Float16 ? ExecutionFlags.AllowHalfPrecisionComputation : ExecutionFlags.None);
        using var initializer = dml.CreateOperatorInitializer([compiled]);

        ID3D12Resource Random(BufferTensorDescription t, int seed)
        {
            var buffer = s.Buffer(t.TotalTensorSizeInBytes, HeapType.Default, ResourceStates.CopyDest, ResourceFlags.AllowUnorderedAccess);
            s.Fill(buffer, destination => FillRandom(destination, level.Input, seed), ResourceStates.UnorderedAccess); return buffer;
        }
        var bufferA = Random(a, 1); var bufferB = Random(b, 2); var bufferOut = s.UavBuffer(output.TotalTensorSizeInBytes);
        // Optional inputs cannot be left unbound through Vortice, so they get zero buffers: GEMM's C (Alpha*A*B + Beta*C, with Beta = 0 - one
        // extra read per multiply, well under 1% of the work) and the INT8 zero points (0: plain symmetric int8, as quantised models use).
        (BufferTensorDescription Tensor, ID3D12Resource Buffer)[] operands = level.Integer
            ? [(a, bufferA), (zeroPoint, s.UavBuffer(zeroPoint.TotalTensorSizeInBytes)), (b, bufferB), (zeroPoint, s.UavBuffer(zeroPoint.TotalTensorSizeInBytes))]
            : [(a, bufferA), (b, bufferB), (output, s.UavBuffer(output.TotalTensorSizeInBytes))];

        BindingProperties init = initializer.GetBindingProperties(), exec = compiled.GetBindingProperties();
        var heap = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, Math.Max(1, Math.Max(init.RequiredDescriptorCount, exec.RequiredDescriptorCount)), DescriptorHeapFlags.ShaderVisible, 0)));
        BindingDescription Bind(ID3D12Resource r) => new(new BufferBinding { Buffer = r, SizeInBytes = r.Description.Width });
        ID3D12Resource? Scratch(ulong bytes) => bytes == 0 ? null : s.UavBuffer(bytes);
        var persistent = Scratch(exec.PersistentResourceSize); var initTemp = Scratch(init.TemporaryResourceSize); var execTemp = Scratch(exec.TemporaryResourceSize);

        var tableDescription = new BindingTableDescription { Dispatchable = initializer, CPUDescriptorHandle = heap.GetCPUDescriptorHandleForHeapStart(), GPUDescriptorHandle = heap.GetGPUDescriptorHandleForHeapStart(), SizeInDescriptors = heap.Description.DescriptorCount };
        using var table = dml.CreateBindingTable(in tableDescription);
        if (initTemp is not null) table.BindTemporaryResource(Bind(initTemp));
        if (persistent is not null) table.BindOutputs([Bind(persistent)]);
        using var recorder = dml.CreateCommandRecorder();
        s.Run(l => { l.SetDescriptorHeaps(heap); recorder.RecordDispatch(l, initializer, table); });

        tableDescription.Dispatchable = compiled; table.Reset(tableDescription);
        table.BindInputs([.. operands.Select(o => Bind(o.Buffer))]); table.BindOutputs([Bind(bufferOut)]);
        if (execTemp is not null) table.BindTemporaryResource(Bind(execTemp));
        if (persistent is not null) table.BindPersistentResource(Bind(persistent));

        return s.Measure((l, count) =>
        {
            l.SetDescriptorHeaps(heap);
            for (int i = 0; i < count; i++) { recorder.RecordDispatch(l, compiled, table); l.ResourceBarrierUnorderedAccessView(bufferOut); }
        }, seconds, progress, ct);
    }

    private static BufferTensorDescription Tensor(TensorDataType type, uint size)
    {
        uint[] sizes = [1, 1, size, size];
        return new BufferTensorDescription { DataType = type, Sizes = sizes, TotalTensorSizeInBytes = (BufferTensorDescription.CalculateMinimumImpliedSize(type, sizes) + 3) & ~3UL };   // DirectML wants a multiple of 4
    }

    private static void FillRandom(Span<byte> bytes, TensorDataType type, int seed)
    {
        var random = new Random(seed);
        switch (type)
        {
            case TensorDataType.Float32: foreach (ref float v in MemoryMarshal.Cast<byte, float>(bytes)) v = random.NextSingle() * 2 - 1; break;
            case TensorDataType.Float16: foreach (ref Half v in MemoryMarshal.Cast<byte, Half>(bytes)) v = (Half)(random.NextSingle() * 2 - 1); break;
            default: random.NextBytes(bytes); break;
        }
    }
}
