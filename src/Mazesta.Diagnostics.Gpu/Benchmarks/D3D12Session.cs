using System.Diagnostics; using System.Numerics; using System.Reflection; using System.Runtime.InteropServices; using ComputeSharp; using ComputeSharp.Interop; using Vortice.Direct3D12;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>
/// Raw Direct3D 12 for the benchmarks ComputeSharp cannot express (rasterisation, inline ray tracing, DirectML), on the very
/// ID3D12Device ComputeSharp opened for the chosen adapter - so the GPU option means the same card here as in the tests.
/// One direct queue and one command list; every submission is waited for, so a timed interval only ever contains finished work.
/// </summary>
internal sealed unsafe class D3D12Session : IDisposable
{
    public ID3D12Device5 Device { get; }
    public ID3D12CommandQueue Queue { get; }
    public ID3D12GraphicsCommandList4 List { get; }
    public string AdapterName { get; }
    private readonly ID3D12CommandAllocator _allocator; private readonly ID3D12Fence _fence; private ulong _fenceValue;
    private readonly List<IDisposable> _owned = [];

    public D3D12Session(GraphicsDevice device)
    {
        AdapterName = device.Name;
        Guid iid = typeof(ID3D12Device5).GUID; void* pointer;
        InteropServices.GetID3D12Device(device, &iid, &pointer);   // every DXR-capable Windows 10/11 has ID3D12Device5
        Device = new ID3D12Device5((nint)pointer);
        Queue = Device.CreateCommandQueue(CommandListType.Direct);
        _allocator = Device.CreateCommandAllocator(CommandListType.Direct);
        List = Device.CreateCommandList<ID3D12GraphicsCommandList4>(CommandListType.Direct, _allocator); List.Close();   // created recording; Record resets it
        _fence = Device.CreateFence();
    }

    /// <summary>Keeps a resource alive until the session ends.</summary>
    public T Own<T>(T resource) where T : IDisposable { _owned.Add(resource); return resource; }

    public ID3D12Resource Buffer(ulong bytes, HeapType heap = HeapType.Default, ResourceStates state = ResourceStates.Common, ResourceFlags flags = ResourceFlags.None)
        => Own(Device.CreateCommittedResource(heap, ResourceDescription.Buffer(bytes, flags), state));

    /// <summary>A GPU-local buffer holding <paramref name="data"/>, copied through a temporary upload buffer.</summary>
    public ID3D12Resource Upload<T>(ReadOnlySpan<T> data, ResourceStates finalState, ResourceFlags flags = ResourceFlags.None) where T : unmanaged
    {
        ulong bytes = (ulong)(data.Length * sizeof(T));
        using var staging = Device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer(bytes), ResourceStates.GenericRead);
        MemoryMarshal.AsBytes(data).CopyTo(staging.Map<byte>(0, (int)bytes)); staging.Unmap(0);
        var target = Buffer(bytes, HeapType.Default, ResourceStates.CopyDest, flags);
        Record(l => { l.CopyBufferRegion(target, 0, staging, 0, bytes); l.ResourceBarrierTransition(target, ResourceStates.CopyDest, finalState); });
        Submit();
        return target;
    }

    /// <summary>Records into the (reset) command list and closes it; <see cref="Submit"/> may then run it any number of times.</summary>
    public void Record(Action<ID3D12GraphicsCommandList4> record)
    {
        Wait(); _allocator.Reset(); List.Reset(_allocator); record(List); List.Close();
    }

    /// <summary>Runs the recorded list and waits for the GPU to finish it. A device lost mid-run (driver reset, overheating) throws.</summary>
    public void Submit()
    {
        Queue.ExecuteCommandList(List); Queue.Signal(_fence, ++_fenceValue); Wait();
        if (Device.DeviceRemovedReason.Failure) throw new InvalidOperationException($"The GPU was lost during the run (device removed: {Device.DeviceRemovedReason}).");
    }

    /// <summary>Copies <paramref name="count"/> 32-bit values to the CPU: <paramref name="copy"/> records the copy into the given readback buffer.</summary>
    public uint[] Read(int count, Action<ID3D12GraphicsCommandList4, ID3D12Resource> copy)
    {
        using var readback = Device.CreateCommittedResource(HeapType.Readback, ResourceDescription.Buffer((ulong)count * 4), ResourceStates.CopyDest);
        Record(l => copy(l, readback)); Submit();
        var values = readback.Map<uint>(0, count).ToArray(); readback.Unmap(0); return values;
    }

    private void Wait() { if (_fence.CompletedValue < _fenceValue) _fence.SetEventOnCompletion(_fenceValue).CheckError(); }

    /// <summary>
    /// Times repeated runs of a batch for <paramref name="seconds"/>. The batch is recorded first with one unit of work to see how
    /// long it takes, then re-recorded with enough units to keep each submission near a tenth of a second, so the GPU is never
    /// left idle between short submissions and the timer resolution does not matter. Returns units completed per second.
    /// </summary>
    public double Measure(Action<ID3D12GraphicsCommandList4, int> record, double seconds, Action<double> progress, CancellationToken ct)
    {
        Record(l => record(l, 1)); Submit();   // warm-up: first-use costs (shader compilation, residency) are not measured
        var probe = Stopwatch.StartNew(); Submit(); double one = Math.Max(1e-5, probe.Elapsed.TotalSeconds);
        int units = (int)Math.Clamp(0.1 / one, 1, 4096);
        Record(l => record(l, units));
        long done = 0; var sw = Stopwatch.StartNew();
        do { ct.ThrowIfCancellationRequested(); Submit(); done += units; progress(sw.Elapsed.TotalSeconds / seconds); } while (sw.Elapsed.TotalSeconds < seconds);
        return done / sw.Elapsed.TotalSeconds;
    }

    public static byte[] Shader(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Mazesta.Diagnostics.Gpu.Shaders.{name}.cso") ?? throw new InvalidOperationException($"Shader {name} is not embedded.");
        var bytes = new byte[s.Length]; s.ReadExactly(bytes); return bytes;
    }

    public void Dispose()
    {
        try { Wait(); } catch (Exception e) when (e is SharpGen.Runtime.SharpGenException) { }   // a lost device: release anyway
        for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        List.Dispose(); _allocator.Dispose(); _fence.Dispose(); Queue.Dispose(); Device.Dispose();
    }
}

/// <summary>A unit icosphere: the twelve vertices of an icosahedron with every triangle split in four <c>subdivisions</c> times
/// and pushed out to the sphere. Positions double as normals.</summary>
internal static class Icosphere
{
    public static (Vector3[] Vertices, uint[] Indices) Create(int subdivisions)
    {
        float t = (1 + MathF.Sqrt(5)) / 2;
        var vertices = new List<Vector3> { new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1) };
        for (int i = 0; i < vertices.Count; i++) vertices[i] = Vector3.Normalize(vertices[i]);
        List<uint> faces = [0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1];
        for (int s = 0; s < subdivisions; s++)
        {
            var midpoints = new Dictionary<(uint, uint), uint>(); var next = new List<uint>(faces.Count * 4);
            uint Middle(uint a, uint b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (!midpoints.TryGetValue(key, out uint m)) { m = (uint)vertices.Count; vertices.Add(Vector3.Normalize(vertices[(int)a] + vertices[(int)b])); midpoints[key] = m; }
                return m;
            }
            for (int f = 0; f < faces.Count; f += 3)
            {
                uint a = faces[f], b = faces[f + 1], c = faces[f + 2], ab = Middle(a, b), bc = Middle(b, c), ca = Middle(c, a);
                next.AddRange([a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca]);
            }
            faces = next;
        }
        return ([.. vertices], [.. faces]);
    }
}
