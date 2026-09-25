using System.IO;
using Mazesta.Core.Time; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Gpu.Benchmarks; using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Gpu; using Mazesta.Diagnostics.Memory; using Mazesta.Diagnostics.Network; using Mazesta.Diagnostics.Storage; using Mazesta.Diagnostics.Whea;
using Mazesta.Monitoring; using Mazesta.Persistence; using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Desktop.Composition;

internal static class DiagnosticsRegistration
{
    /// <summary>
    /// Registers the test engine and every test. The order of the <see cref="ITestExecutor"/> registrations is
    /// the order of the Test Center list, so it runs component by component: CPU, RAM, storage, network, GPU,
    /// then the combined power test that needs both processors, Windows health (sfc, DISM check), then the final SMART re-check.
    /// </summary>
    public static IServiceCollection AddDiagnostics(this IServiceCollection s, AppPaths paths, ILoggerFactory loggers)
    {
        s.AddSingleton(_ => new JsonStore<TestSessionCheckpoint>(Path.Combine(paths.SessionsDir, "test-checkpoint.json"), new SchemaMigrator([]), TestSessionCheckpoint.CurrentSchemaVersion, loggers.CreateLogger("Diagnostics")));
        s.AddSingleton<IMemoryProbe, Win32MemoryProbe>();
        s.AddSingleton<IHardwareErrorSource, WheaErrorSource>();

        s.AddSingleton<ITestExecutor, CpuMatrixStressExecutor>();
        s.AddSingleton<ITestExecutor, MemoryPatternExecutor>();
        s.AddSingleton<ITestExecutor, StorageSequentialExecutor>();
        s.AddSingleton<ITestExecutor, StorageRandom4kExecutor>();
        s.AddSingleton<ITestExecutor, NetworkLatencyExecutor>();
        s.AddSingleton<ITestExecutor>(new GpuStressExecutor(GpuStressProfile.Steady));
        s.AddSingleton<ITestExecutor>(new GpuStressExecutor(GpuStressProfile.Variable));
        s.AddSingleton<ITestExecutor>(new GpuStressExecutor(GpuStressProfile.Pulse));
        s.AddSingleton<ITestExecutor, GpuVramExecutor>();
        s.AddSingleton<ITestExecutor, GpuRenderExecutor>();
        s.AddSingleton<ITestExecutor>(new PowerExecutor(new CpuMatrixStressExecutor(), new GpuStressExecutor(GpuStressProfile.Steady)));
        s.AddSingleton<Mazesta.Diagnostics.Windows.ICommandRunner, Mazesta.Diagnostics.Windows.ProcessCommandRunner>();
        s.AddSingleton<ITestExecutor, Mazesta.Diagnostics.Windows.SfcExecutor>();
        s.AddSingleton<ITestExecutor, Mazesta.Diagnostics.Windows.DismScanExecutor>();
        s.AddSingleton<Mazesta.Core.Providers.IDriveHealthProvider>(sp => new Mazesta.Hardware.Wmi.WmiDriveHealthProvider(sp.GetRequiredService<Mazesta.Hardware.Wmi.IWmiQuery>()));
        s.AddSingleton<ITestExecutor, SmartCheckExecutor>();   // last: the final SMART re-check sees what the tests did to the drives (spec 4.2, item 11)

        // Benchmarks page order: CPU (single thread, then all threads), memory, storage, the three GPU workloads, then the internet link.
        s.AddSingleton<IBenchmark>(new CpuBenchmark(allThreads: false));
        s.AddSingleton<IBenchmark>(new CpuBenchmark(allThreads: true));
        s.AddSingleton<IBenchmark, MemoryBenchmark>();
        s.AddSingleton<IBenchmark, StorageBenchmark>();
        s.AddSingleton<IBenchmark, GpuRasterBenchmark>();
        s.AddSingleton<IBenchmark, GpuRayTracingBenchmark>();
        s.AddSingleton<IBenchmark, GpuAiBenchmark>();
        s.AddSingleton<IBenchmark>(new InternetSpeedBenchmark());
        // Singleton for the same reason as the engine below: a benchmark keeps running, and its result stays, while the page is closed.
        s.AddSingleton(sp => new BenchmarkRunner(sp.GetRequiredService<IEnumerable<IBenchmark>>(), sp.GetRequiredService<IClock>(), sp.GetRequiredService<PollingEngine>()));

        // Singleton, not per-page: a queue keeps running when the technician navigates away from Test Center
        // and back (TestEngine.RequestCancel's own note) - it must not be recreated per visit.
        s.AddSingleton(sp => new TestEngine(sp.GetRequiredService<IEnumerable<ITestExecutor>>(), sp.GetRequiredService<JsonStore<TestSessionCheckpoint>>(),
            sp.GetRequiredService<IClock>(), sp.GetRequiredService<PollingEngine>(), sp.GetRequiredService<IHardwareErrorSource>()));
        return s;
    }
}
