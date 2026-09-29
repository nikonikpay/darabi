using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Hardware.Details; using Mazesta.Monitoring; using Microsoft.Extensions.Logging;
namespace Mazesta.Desktop.Composition;

/// <summary>
/// Reads the specification details once per process, like <see cref="InventoryCache"/>. It waits until the sensor provider has settled: the memory
/// modules' SPD chips are read over the SMBus that provider opens, so an earlier read would find no bus and report no modules. A provider that
/// failed still lets the rest be read (the SPD part is then empty).
/// </summary>
public sealed class HardwareDetailsCache(IHardwareDetailsProvider provider, InventoryCache inventory, PollingEngine engine, ILogger<HardwareDetailsCache> log)
{
    private readonly object _lock = new();
    private Task<HardwareDetails>? _task;

    public Task<HardwareDetails> GetAsync() { lock (_lock) return _task ??= ReadAsync(); }

    private async Task<HardwareDetails> ReadAsync()
    {
        var inv = await inventory.GetAsync().ConfigureAwait(false);
        await SettledAsync().ConfigureAwait(false);
        var details = await provider.ReadAsync(inv, CancellationToken.None).ConfigureAwait(false);
        log.LogInformation("Hardware details ready: {Modules} SPD module(s), {Errors} error(s)", details.Spd.Count, details.Errors.Count);
        return details;
    }

    private Task SettledAsync()
    {
        static bool Settled(ProviderStatus s) => s.State is ProviderState.Ready or ProviderState.Degraded or ProviderState.Failed;
        if (Settled(engine.Provider.Status)) return Task.CompletedTask;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void On(ProviderStatus s) { if (Settled(s)) { engine.Provider.StatusChanged -= On; done.TrySetResult(); } }
        engine.Provider.StatusChanged += On;
        if (Settled(engine.Provider.Status)) On(engine.Provider.Status);
        return Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(3)));   // never wait for ever: without the SPD, the rest still shows
    }
}
