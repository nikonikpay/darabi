using System.IO;
using Mazesta.Core.Time; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Gpu; using Mazesta.Diagnostics.Memory; using Mazesta.Diagnostics.Network; using Mazesta.Diagnostics.Storage; using Mazesta.Diagnostics.Whea;
using Mazesta.Monitoring; using Mazesta.Persistence; using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Desktop.Composition;

internal static class DiagnosticsRegistration
{
    /// <summary>
    /// Registers the test engine and every test. The order of the <see cref="ITestExecutor"/> registrations is
    /// the order of the Test Center list, so it runs component by component: CPU, RAM, storage, network, GPU,
    /// then the combined power test that needs both processors.
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

        // Singleton, not per-page: a queue keeps running when the technician navigates away from Test Center
        // and back (TestEngine.RequestCancel's own note) - it must not be recreated per visit.
        s.AddSingleton(sp => new TestEngine(sp.GetRequiredService<IEnumerable<ITestExecutor>>(), sp.GetRequiredService<JsonStore<TestSessionCheckpoint>>(),
            sp.GetRequiredService<IClock>(), sp.GetRequiredService<PollingEngine>(), sp.GetRequiredService<IHardwareErrorSource>()));
        return s;
    }
}
