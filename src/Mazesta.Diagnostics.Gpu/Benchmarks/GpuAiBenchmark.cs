using Mazesta.Diagnostics.Benchmarks; using Vortice.Direct3D12; using Vortice.DirectML;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>
/// AI inference throughput through DirectML - the GPU path Windows AI applications take (ONNX Runtime and Windows ML) - at the
/// three precision levels AI benchmarks report: FP32, FP16 and INT8. Each level runs a 4096x4096x4096 matrix multiply, the
/// operation that dominates neural-network inference, for a third of the duration. DirectML hands it to the vendor's
/// metacommands where the driver has them, so tensor/matrix cores are used when the GPU has them; the numbers are therefore
/// what an AI application on this machine would get, not the chip's theoretical peak. A precision the GPU or the installed
/// DirectML cannot run is left out and named in the detail. DirectML is the copy that ships with Windows (System32).
/// </summary>
public sealed class GpuAiBenchmark : IBenchmark
{
    public static readonly TestDefinition Spec = new(new TestId("bench.gpu.ai"), "Bench_Gpu_Ai", 60, [GpuDevices.Option]);
    public TestDefinition Definition => Spec;
    private const uint Size = 4096;
    private const double OpsPerMultiply = 2.0 * Size * Size * Size;

    private sealed record Level(TensorDataType Input, TensorDataType Output, string Key, string Unit, string Name);
    private static readonly Level[] Levels =
    [
        new(TensorDataType.Float32, TensorDataType.Float32, "Bench_Gpu_Ai_Fp32", "TFLOPS", "FP32"),
        new(TensorDataType.Float16, TensorDataType.Float16, "Bench_Gpu_Ai_Fp16", "TFLOPS", "FP16"),
        new(TensorDataType.Int8, TensorDataType.Int32, "Bench_Gpu_Ai_Int8", "TOPS", "INT8")
    ];

    public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => GpuBenchmark.RunAsync(Spec, request, ct, s => Run(s, request, ct));

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
                : level.Input == TensorDataType.Int8 && dml.CheckFeatureLevelsSupport([FeatureLevel.Level2_1]) < FeatureLevel.Level2_1 ? $"{level.Name}: needs DirectML feature level 2.1" : null;
            if (missing is not null) { skipped.Add(missing); continue; }
            int index = i;
            double perSecond = Measure(s, dml, level, seconds, p => request.Report((index + p) / Levels.Length), ct);
            metrics.Add(new(level.Key, perSecond * OpsPerMultiply / 1e12, level.Unit));
        }
        if (metrics.Count == 0) throw new GpuUnsupportedException("DirectML cannot run a matrix multiply at any precision on this GPU: " + string.Join("; ", skipped));
        return (metrics, $"DirectML {Size}x{Size}x{Size} matrix multiply (GEMM for FP32/FP16, integer matmul for INT8), DirectML feature level {dml.HighestFeatureLevel}"
            + (skipped.Count > 0 ? "; not measured: " + string.Join("; ", skipped) : ""));
    }

    /// <summary>Multiplies per second at one precision. Inputs hold random values (an all-zero matrix would let some GPUs idle
    /// their multipliers and inflate the result); consecutive multiplies are separated by a barrier, as layers of a network are.</summary>
    private static double Measure(D3D12Session s, IDMLDevice dml, Level level, double seconds, Action<double> progress, CancellationToken ct)
    {
        bool integer = level.Input == TensorDataType.Int8;
        var a = Tensor(level.Input, Size); var b = Tensor(level.Input, Size); var output = Tensor(level.Output, Size); var zeroPoint = Tensor(TensorDataType.Int8, 1);
        IOperatorDescription description = integer
            ? new MatrixMultiplyIntegerOperatorDescription { ATensor = new(a), AZeroPointTensor = new TensorDescription(zeroPoint), BTensor = new(b), BZeroPointTensor = new TensorDescription(zeroPoint), OutputTensor = new(output) }
            : new GeneralMatrixMultiplyOperatorDescription { ATensor = new(a), BTensor = new(b), CTensor = new TensorDescription(output), OutputTensor = new(output), Alpha = 1, Beta = 0 };
        using var op = dml.CreateOperator(new OperatorDescription(description));
        // FP16 may also accumulate in FP16, as FP16 inference does; FP32 and INT8 keep their full-precision paths.
        using var compiled = dml.CompileOperator(op, level.Input == TensorDataType.Float16 ? ExecutionFlags.AllowHalfPrecisionComputation : ExecutionFlags.None);
        using var initializer = dml.CreateOperatorInitializer([compiled]);

        var bufferA = s.Upload<byte>(RandomBytes(a.TotalTensorSizeInBytes, level.Input, 1), ResourceStates.UnorderedAccess, ResourceFlags.AllowUnorderedAccess);
        var bufferB = s.Upload<byte>(RandomBytes(b.TotalTensorSizeInBytes, level.Input, 2), ResourceStates.UnorderedAccess, ResourceFlags.AllowUnorderedAccess);
        var bufferOut = s.Buffer(output.TotalTensorSizeInBytes, state: ResourceStates.UnorderedAccess, flags: ResourceFlags.AllowUnorderedAccess);
        // Optional inputs cannot be left unbound through Vortice, so they get zero buffers: GEMM's C (Alpha*A*B + Beta*C, with Beta = 0 - one
        // extra read per multiply, well under 1% of the work) and the INT8 zero points (0: plain symmetric int8, as quantised models use).
        var zeros = s.Buffer(output.TotalTensorSizeInBytes, state: ResourceStates.UnorderedAccess, flags: ResourceFlags.AllowUnorderedAccess);
        (BufferTensorDescription Tensor, ID3D12Resource Buffer)[] operands = integer ? [(a, bufferA), (zeroPoint, zeros), (b, bufferB), (zeroPoint, zeros)] : [(a, bufferA), (b, bufferB), (output, zeros)];

        BindingProperties init = initializer.GetBindingProperties(), exec = compiled.GetBindingProperties();
        using var heap = s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, Math.Max(1, Math.Max(init.RequiredDescriptorCount, exec.RequiredDescriptorCount)), DescriptorHeapFlags.ShaderVisible, 0));
        ID3D12Resource? Scratch(ulong bytes) => bytes == 0 ? null : s.Buffer(bytes, state: ResourceStates.UnorderedAccess, flags: ResourceFlags.AllowUnorderedAccess);
        BindingDescription? Bind(ID3D12Resource? r, ulong bytes) => r is null ? null : new BindingDescription(new BufferBinding { Buffer = r, SizeInBytes = bytes });
        var persistent = Scratch(exec.PersistentResourceSize); var initTemp = Scratch(init.TemporaryResourceSize); var execTemp = Scratch(exec.TemporaryResourceSize);

        var tableDescription = new BindingTableDescription { Dispatchable = initializer, CPUDescriptorHandle = heap.GetCPUDescriptorHandleForHeapStart(), GPUDescriptorHandle = heap.GetGPUDescriptorHandleForHeapStart(), SizeInDescriptors = heap.Description.DescriptorCount };
        using var table = dml.CreateBindingTable(in tableDescription);
        if (Bind(initTemp, init.TemporaryResourceSize) is { } it) table.BindTemporaryResource(it);
        if (Bind(persistent, exec.PersistentResourceSize) is { } p) table.BindOutputs([p]);
        using var recorder = dml.CreateCommandRecorder();
        s.Record(l => { l.SetDescriptorHeaps(heap); recorder.RecordDispatch(l, initializer, table); });
        s.Submit();

        tableDescription.Dispatchable = compiled; table.Reset(tableDescription);
        table.BindInputs([.. operands.Select(o => Bind(o.Buffer, o.Tensor.TotalTensorSizeInBytes)!.Value)]); table.BindOutputs([Bind(bufferOut, output.TotalTensorSizeInBytes)!.Value]);
        if (Bind(execTemp, exec.TemporaryResourceSize) is { } et) table.BindTemporaryResource(et);
        if (Bind(persistent, exec.PersistentResourceSize) is { } pe) table.BindPersistentResource(pe);

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

    private static byte[] RandomBytes(ulong length, TensorDataType type, int seed)
    {
        var bytes = new byte[length]; var random = new Random(seed);
        switch (type)
        {
            case TensorDataType.Float32: { var v = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes.AsSpan()); for (int i = 0; i < v.Length; i++) v[i] = random.NextSingle() * 2 - 1; break; }
            case TensorDataType.Float16: { var v = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(bytes.AsSpan()); for (int i = 0; i < v.Length; i++) v[i] = (Half)(random.NextSingle() * 2 - 1); break; }
            default: random.NextBytes(bytes); break;
        }
        return bytes;
    }
}
