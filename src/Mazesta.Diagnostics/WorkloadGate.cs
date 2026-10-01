namespace Mazesta.Diagnostics;

public enum Workload { Tests, Benchmark, Tuning, Drivers }

/// <summary>A test queue, a benchmark and the automatic GPU tuning each load the machine to its limit and each measures it. Two at once would
/// stress each other and measure each other, so a stability result or a speed record made that way means nothing. All three take this one gate
/// before they start and refuse to start while another holds it. The combined CPU+GPU power test is one test inside the queue, not two
/// activities, so it is unaffected. A driver install takes it too: nothing measures a card while its driver changes.</summary>
public sealed class WorkloadGate
{
    private readonly object _lock = new();
    private Workload? _holder;

    /// <summary>What is running now, or null.</summary>
    public Workload? Holder { get { lock (_lock) return _holder; } }
    /// <summary>Raised on the thread that took or released the gate.</summary>
    public event Action<Workload?>? Changed;

    /// <summary>The gate for <paramref name="workload"/> until the returned lease is disposed, or null when something else holds it.</summary>
    public IDisposable? TryEnter(Workload workload)
    {
        lock (_lock) { if (_holder is not null) return null; _holder = workload; }
        Changed?.Invoke(workload);
        return new Lease(this);
    }

    private void Release() { lock (_lock) _holder = null; Changed?.Invoke(null); }

    private sealed class Lease(WorkloadGate gate) : IDisposable
    {
        private int _done;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) gate.Release(); }
    }
}

/// <summary>A start refused because another activity holds the <see cref="WorkloadGate"/>.</summary>
public sealed class WorkloadBusyException(Workload holder) : InvalidOperationException($"{holder} is running; only one load runs at a time.")
{
    public Workload Holder { get; } = holder;
}
