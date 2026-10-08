namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// A crew of worker threads, one for each logical processor, that stay alive between frames and spin at a barrier between the passes of a job - how a game's job system keeps a frame's
/// stages that follow one another (the wind's advection, then its pressure sweeps, then its gradient) from paying the operating system for waking every core again at each stage: a thread
/// pool's parallel loop costs some hundreds of microseconds to start, a pass of a pressure sweep is a few tens. The crew sleeps while no job runs (nothing spins between frames).
/// A job is run by every worker, each told its number, and the workers meet at <see cref="Barrier"/> as often as the job says.
/// </summary>
internal sealed class GardenCrew
{
    public static readonly GardenCrew Shared = new(Environment.ProcessorCount);
    public int Size { get; }
    private readonly SemaphoreSlim[] _wake; private Action<int>? _job;
    private volatile int _finished, _generation; private int _arrived; private readonly object _one = new();

    /// <summary>A crew of its own, below the picture's priority and short of a few cores: for a simulation that runs beside the renderer and must not take the processor from it.</summary>
    public static GardenCrew Beside(int size) => new(size, ThreadPriority.BelowNormal);

    private GardenCrew(int size, ThreadPriority priority = ThreadPriority.AboveNormal)
    {
        Size = Math.Max(1, size); _wake = new SemaphoreSlim[Size];
        for (int w = 1; w < Size; w++)
        {
            int number = w; _wake[w] = new SemaphoreSlim(0);
            new Thread(() => Work(number)) { IsBackground = true, Name = "Garden crew " + number, Priority = priority }.Start();
        }
    }

    private void Work(int number)
    {
        while (true)
        {
            _wake[number].Wait();
            try { _job!(number); } finally { Interlocked.Increment(ref _finished); }
        }
    }

    /// <summary>Runs <paramref name="job"/> on every worker (the caller is worker 0) and returns when all have finished it.</summary>
    public void Run(Action<int> job)
    {
        lock (_one)
        {
            _job = job; _finished = 0; _arrived = 0;
            for (int w = 1; w < Size; w++) _wake[w].Release();
            try { job(0); }
            finally { var spin = new SpinWait(); while (_finished < Size - 1) spin.SpinOnce(-1); }
        }
    }

    /// <summary>Every worker of the job waits here until all have come (inside a job, and from every worker the same number of times).</summary>
    public void Barrier()
    {
        int generation = _generation;
        if (Interlocked.Increment(ref _arrived) == Size) { _arrived = 0; _generation = generation + 1; return; }
        var spin = new SpinWait();
        while (_generation == generation) spin.SpinOnce(-1);
    }

    /// <summary>The run of <paramref name="count"/> items that worker <paramref name="number"/> takes: neighbouring ones together, so two workers rarely share a cache line.</summary>
    public (int From, int To) Share(int number, int count) => ((int)((long)count * number / Size), (int)((long)count * (number + 1) / Size));
}
