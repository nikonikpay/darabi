using System.Diagnostics; using System.Globalization; using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.Diagnostics.Evidence;

/// <summary>What this program itself took of the machine while a test or benchmark ran: the processor time its process used (as cores busy and as a
/// share of the whole processor) and the RAM it held (its working set), read twice a second. This is the load the run put on the computer, apart from
/// whatever else was running - the figures of the whole system's RAM say nothing about the test when other programs are open.
/// The program's own process only: the browser engine that draws its window is a process of its own and is not counted.</summary>
public sealed class RunFootprint : IDisposable
{
    public readonly record struct Result(double CoresAverage, double CoresPeak, double MachineAveragePercent, double RamAverageMb, double RamPeakMb, int Samples)
    {
        /// <summary>The figures as a benchmark's numbers (all conditions of the run, in the "App" group).</summary>
        public IEnumerable<BenchmarkMetric> Metrics() => Samples == 0 ? [] :
        [
            new("Bench_App_Cores", CoresAverage, "cores"), new("Bench_App_CoresMax", CoresPeak, "cores"), new("Bench_App_CpuLoad", MachineAveragePercent, "%"),
            new("Bench_App_Ram", RamAverageMb, "MB"), new("Bench_App_RamMax", RamPeakMb, "MB"),
        ];

        /// <summary>The same as a line of a test's evidence.</summary>
        public string? Describe() => Samples == 0 ? null : string.Create(CultureInfo.InvariantCulture,
            $"Mazesta's own load: processor {CoresAverage:F1} cores busy on average (peak {CoresPeak:F1}; {MachineAveragePercent:F0} % of the whole processor), RAM {RamAverageMb:F0} MB on average (peak {RamPeakMb:F0} MB)");
    }

    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Timer _timer; private readonly object _lock = new();
    private TimeSpan _lastCpu; private long _lastTicks;
    private double _coresSum, _coresPeak, _ramSum, _ramPeak; private int _samples;

    private RunFootprint()
    {
        _process.Refresh(); _lastCpu = _process.TotalProcessorTime; _lastTicks = Stopwatch.GetTimestamp();
        _timer = new Timer(_ => Sample(), null, 500, 500);
    }

    public static RunFootprint Start() => new();

    private void Sample()
    {
        try
        {
            lock (_lock)
            {
                _process.Refresh();
                var cpu = _process.TotalProcessorTime; long now = Stopwatch.GetTimestamp();
                double seconds = Stopwatch.GetElapsedTime(_lastTicks, now).TotalSeconds;
                if (seconds <= 0) return;
                double cores = (cpu - _lastCpu).TotalSeconds / seconds, ram = _process.WorkingSet64 / 1048576.0;
                _lastCpu = cpu; _lastTicks = now;
                _coresSum += cores; _coresPeak = Math.Max(_coresPeak, cores); _ramSum += ram; _ramPeak = Math.Max(_ramPeak, ram); _samples++;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }   // a figure that cannot be read is left out
    }

    /// <summary>The program's working set now, in megabytes: what the readout over a scene shows while it runs.</summary>
    public static double CurrentRamMb() { using var p = Process.GetCurrentProcess(); return p.WorkingSet64 / 1048576.0; }

    public Result Stop()
    {
        _timer.Dispose(); Sample();
        lock (_lock) return _samples < 2 ? default   // a run of under a second says nothing of its load
            : new(_coresSum / _samples, _coresPeak, _coresSum / _samples / Environment.ProcessorCount * 100, _ramSum / _samples, _ramPeak, _samples);
    }

    public void Dispose() { _timer.Dispose(); _process.Dispose(); }
}
