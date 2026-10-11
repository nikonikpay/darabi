using System.Diagnostics; using System.Numerics; using System.Reflection; using System.Runtime.InteropServices; using ComputeSharp; using ComputeSharp.Interop; using Vortice.Direct3D12;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>
/// Raw Direct3D 12 for the benchmarks ComputeSharp cannot express (rasterisation, inline ray tracing, DirectML), on the very
/// ID3D12Device ComputeSharp opened for the chosen adapter - so the GPU option means the same card here as in the tests.
/// One direct queue; unless the session was made for pipelining (<see cref="Frames"/> above one), every submission is waited for, so a timed
/// interval only ever contains finished work. With frames in flight each frame has a slot of its own: its command allocators, and the room the
/// scene keeps its per-frame uploads in, so the processor can prepare the next frame while the card draws this one.
/// </summary>
internal sealed unsafe class D3D12Session : IDisposable
{
    public ID3D12Device5 Device { get; }
    public ID3D12CommandQueue Queue { get; }
    /// <summary>The list being recorded or last recorded (a few take turns, see <see cref="Handoff"/>).</summary>
    public ID3D12GraphicsCommandList4 List => _lists[_cur];
    public string AdapterName { get; }
    private readonly ID3D12CommandAllocator[] _allocators; private readonly ID3D12GraphicsCommandList4[] _lists = new ID3D12GraphicsCommandList4[Chunks]; private int _cur;
    /// <summary>The most lists one recording may be cut into: each has an allocator of its own (for each frame slot), because one still running on the card cannot be reset.</summary>
    public const int Chunks = 3;
    private readonly ID3D12Fence _fence; private ulong _fenceValue;
    private readonly List<IDisposable> _owned = [];
    /// <summary>How many frames may be in flight at once (1: each is waited for before the next is recorded; 2: the processor records one while the card draws the one before).</summary>
    public int Frames { get; }
    /// <summary>The slot the frame being recorded takes (0 to <see cref="Frames"/> - 1): what the scene keeps per frame - its constants, the movers' places, the readout's pixels - it keeps at this slot's place, so the card reading one frame's is not disturbed by the processor writing the next one's.</summary>
    public int Slot { get; private set; }
    private readonly ulong[] _slotFence;

    public D3D12Session(GraphicsDevice device, int frames = 1)
    {
        Frames = Math.Clamp(frames, 1, 2); _slotFence = new ulong[Frames]; _allocators = new ID3D12CommandAllocator[Chunks * Frames]; _stamped = new bool[Frames];
        AdapterName = device.Name;
        Guid iid = typeof(ID3D12Device5).GUID; void* pointer;
        InteropServices.GetID3D12Device(device, &iid, &pointer);   // every DXR-capable Windows 10/11 has ID3D12Device5
        Device = new ID3D12Device5((nint)pointer);
        Queue = Device.CreateCommandQueue(CommandListType.Direct);
        for (int i = 0; i < _allocators.Length; i++) _allocators[i] = Device.CreateCommandAllocator(CommandListType.Direct);
        for (int i = 0; i < Chunks; i++) { _lists[i] = Device.CreateCommandList<ID3D12GraphicsCommandList4>(CommandListType.Direct, _allocators[i]); _lists[i].Close(); }   // created recording; Record resets it
        _fence = Device.CreateFence();
    }

    /// <summary>The frame just submitted is over as far as the processor is concerned: the next one is recorded in the next slot (a no-op with one frame in flight).</summary>
    public void NextSlot() => Slot = (Slot + 1) % Frames;

    /// <summary>Keeps a resource alive until the session ends, or until the innermost open <see cref="Scope"/> ends.</summary>
    public T Own<T>(T resource) where T : IDisposable { _owned.Add(resource); return resource; }

    /// <summary>Releases what is owned from now on when disposed, so a benchmark that runs several workloads frees each one's memory before the next.</summary>
    public IDisposable Scope() => new OwnedSince(this, _owned.Count);
    private sealed class OwnedSince(D3D12Session session, int mark) : IDisposable
    {
        public void Dispose() { session.Wait(); for (int i = session._owned.Count - 1; i >= mark; i--) session._owned[i].Dispose(); session._owned.RemoveRange(mark, session._owned.Count - mark); }
    }

    public ID3D12Resource Buffer(ulong bytes, HeapType heap = HeapType.Default, ResourceStates state = ResourceStates.Common, ResourceFlags flags = ResourceFlags.None)
        => Own(Device.CreateCommittedResource(heap, ResourceDescription.Buffer(bytes, flags), state));

    /// <summary>A GPU-local buffer shaders and DirectML may write (unordered access).</summary>
    public ID3D12Resource UavBuffer(ulong bytes) => Buffer(bytes, HeapType.Default, ResourceStates.UnorderedAccess, ResourceFlags.AllowUnorderedAccess);

    /// <summary>A GPU-local buffer holding <paramref name="data"/>, copied through a temporary upload buffer.</summary>
    public ID3D12Resource Upload<T>(T[] data, ResourceStates finalState, ResourceFlags flags = ResourceFlags.None) where T : unmanaged
    {
        var target = Buffer((ulong)(data.Length * sizeof(T)), HeapType.Default, ResourceStates.CopyDest, flags);
        Fill(target, destination => MemoryMarshal.AsBytes(data.AsSpan()).CopyTo(destination), finalState); return target;
    }

    /// <summary>Fills a buffer (created in the copy-destination state) through a temporary upload buffer that <paramref name="write"/>
    /// writes straight into, so large contents need no managed copy of their own.</summary>
    public void Fill(ID3D12Resource target, SpanAction write, ResourceStates finalState)
    {
        ulong bytes = target.Description.Width;
        using var staging = Device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer(bytes), ResourceStates.GenericRead);
        write(staging.Map<byte>(0, (int)bytes)); staging.Unmap(0);
        Run(l => { l.CopyBufferRegion(target, 0, staging, 0, bytes); l.ResourceBarrierTransition(target, ResourceStates.CopyDest, finalState); });
    }
    public delegate void SpanAction(Span<byte> destination);

    /// <summary>Records into the (reset) command list and closes it; <see cref="Submit"/> may then run it any number of times. <paramref name="timed"/>: the card's own time for
    /// what is recorded is measured (two timestamps, the first at the start of the first list and the second at the end of the last) and added to <see cref="GpuSeconds"/> once the frame is done.</summary>
    public void Record(Action<ID3D12GraphicsCommandList4> record, bool timed = false)
    {
        long a = Stopwatch.GetTimestamp(); Wait(Slot); long b = Stopwatch.GetTimestamp(); LastWaitSeconds = Stopwatch.GetElapsedTime(a, b).TotalSeconds;
        _cur = 0; _allocators[Slot * Chunks].Reset(); List.Reset(_allocators[Slot * Chunks]);
        if (timed) { EnsureStamps(); List.EndQuery(_queries!, QueryType.Timestamp, (uint)Slot * 2); }
        record(List);
        if (timed)
        {
            List.EndQuery(_queries!, QueryType.Timestamp, (uint)Slot * 2 + 1); List.ResolveQueryData(_queries!, QueryType.Timestamp, (uint)Slot * 2, 2, _stampBuffer!, (ulong)Slot * 16); _stamped[Slot] = true;
        }
        List.Close();
        LastRecordSeconds = Stopwatch.GetElapsedTime(b).TotalSeconds;
    }

    // ----- the card's own time for each frame, from GPU timestamps (never the processor's clock: with frames in flight the processor waits for the card in other places) -----
    private ID3D12QueryHeap? _queries; private ID3D12Resource? _stampBuffer; private readonly bool[] _stamped; private ulong _frequency;
    private void EnsureStamps()
    {
        if (_queries is not null) return;
        _queries = Own(Device.CreateQueryHeap<ID3D12QueryHeap>(new QueryHeapDescription(QueryHeapType.Timestamp, (uint)Frames * 2, 0)));
        _stampBuffer = Own(Device.CreateCommittedResource(HeapType.Readback, ResourceDescription.Buffer((ulong)Frames * 16), ResourceStates.CopyDest));
        Queue.GetTimestampFrequency(out _frequency);
    }
    /// <summary>How many timed frames have finished and the card's time over them (from its first timestamp to its last, so the gaps while it waited for the processor are not in it).</summary>
    public long GpuFrames { get; private set; }
    public double GpuSeconds { get; private set; }
    /// <summary>The card's time for the frame that finished last, or null before any.</summary>
    public double? LastGpuSeconds { get; private set; }
    private void Harvest(int slot)
    {
        if (!_stamped[slot] || _stampBuffer is null) return;
        _stamped[slot] = false;
        var t = _stampBuffer.Map<ulong>(0, Frames * 2); ulong from = t[slot * 2], to = t[slot * 2 + 1]; _stampBuffer.Unmap(0);
        if (to <= from || _frequency == 0) return;
        double seconds = (to - from) / (double)_frequency; GpuSeconds += seconds; GpuFrames++; LastGpuSeconds = seconds;
    }

    private bool _inFrame;
    /// <summary>With frames in flight, the card does not start a frame before it has finished the one before (a wait on the queue, which costs the card next to nothing): the frames are recorded ahead by the
    /// processor, but never drawn overlapping, so no pass of one can disturb a resource of the other.</summary>
    private void BeginExecuting()
    {
        if (_inFrame) return;
        _inFrame = true; if (Frames > 1 && _fenceValue > 0) Queue.Wait(_fence, _fenceValue);
    }

    /// <summary>Inside a <see cref="Record"/>: sends what is recorded so far to the card at once and goes on in the other list, so the card
    /// works on the first passes while the processor records the rest (it would otherwise sit idle for the whole recording). Returns the list to go on with;
    /// what the caller bound on the old one (root signature, heaps, views) is not bound on it.</summary>
    public ID3D12GraphicsCommandList4 Handoff()
    {
        List.Close(); BeginExecuting(); Queue.ExecuteCommandList(List); _cur = (_cur + 1) % Chunks; var allocator = _allocators[Slot * Chunks + _cur]; allocator.Reset(); List.Reset(allocator); return List;
    }

    /// <summary>Records once and runs it once.</summary>
    public void Run(Action<ID3D12GraphicsCommandList4> record, bool wait = true, bool timed = false)
    {
        Record(record, timed); long b = Stopwatch.GetTimestamp(); Submit(wait); LastSubmitSeconds = Stopwatch.GetElapsedTime(b).TotalSeconds;
    }
    /// <summary>The last <see cref="Run"/>'s two halves: the processor recording the commands, and the card (with the driver) taking the submission to its end.
    /// A frame's time is their sum, because every submission is waited for; the benchmark reads them apart as the processor's and the card's share.</summary>
    /// <summary>Further facts of how a run was set up, which the benchmark that made them leaves for its result.</summary>
    public List<Mazesta.Diagnostics.Benchmarks.SpecItem> Setup { get; } = [];
    public double LastRecordSeconds { get; private set; }
    public double LastSubmitSeconds { get; private set; }
    /// <summary>How long the last <see cref="Record"/> waited for the card to be done with its slot's earlier frame (not part of <see cref="LastRecordSeconds"/>).</summary>
    public double LastWaitSeconds { get; private set; }

    /// <summary>Runs the recorded list and waits for the GPU to finish it. A device lost mid-run (driver reset, overheating) throws.</summary>
    public void Submit(bool wait = true)
    {
        BeginExecuting(); Queue.ExecuteCommandList(List); Queue.Signal(_fence, ++_fenceValue); _slotFence[Slot] = _fenceValue; _inFrame = false; if (wait) Finish();
    }
    /// <summary>Waits for what was submitted without waiting (the processor's own work may overlap the card's), and checks the device is alive.</summary>
    public void Finish()
    {
        long t = Stopwatch.GetTimestamp(); Wait(); LastSubmitSeconds += Stopwatch.GetElapsedTime(t).TotalSeconds;
        for (int i = 0; i < Frames; i++) Harvest(i);
        CheckAlive();
    }
    private void CheckAlive() { if (Device.DeviceRemovedReason.Failure) throw new GpuLostException($"The GPU was lost during the run (device removed: {Device.DeviceRemovedReason})."); }

    /// <summary>Copies <paramref name="count"/> 32-bit values to the CPU: <paramref name="copy"/> records the copy into the given readback buffer.</summary>
    public uint[] Read(int count, Action<ID3D12GraphicsCommandList4, ID3D12Resource> copy)
    {
        using var readback = Device.CreateCommittedResource(HeapType.Readback, ResourceDescription.Buffer((ulong)count * 4), ResourceStates.CopyDest);
        Run(l => copy(l, readback));
        var values = readback.Map<uint>(0, count).ToArray(); readback.Unmap(0); return values;
    }

    private void Wait() { if (_fence.CompletedValue < _fenceValue) _fence.SetEventOnCompletion(_fenceValue).CheckError(); }
    /// <summary>Waits until the card is done with what was last submitted in <paramref name="slot"/> (the frame that used it before), and takes that frame's timestamps.</summary>
    private void Wait(int slot)
    {
        ulong value = _slotFence[slot]; if (_fence.CompletedValue < value) _fence.SetEventOnCompletion(value).CheckError();
        Harvest(slot); if (Frames > 1) CheckAlive();
    }

    /// <summary>
    /// Times repeated runs of a batch for <paramref name="seconds"/>. The batch is recorded first with one unit of work to see how
    /// long it takes, then re-recorded with enough units to keep each submission near a tenth of a second, so the GPU is never
    /// left idle between short submissions and the timer resolution does not matter. Returns units completed per second.
    /// </summary>
    public double Measure(Action<ID3D12GraphicsCommandList4, int> record, double seconds, Action<double> progress, CancellationToken ct)
    {
        Run(l => record(l, 1));   // warm-up: first-use costs (shader compilation, residency) are not measured
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
        foreach (var l in _lists) l.Dispose(); foreach (var a in _allocators) a.Dispose(); _fence.Dispose(); Queue.Dispose(); Device.Dispose();
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
