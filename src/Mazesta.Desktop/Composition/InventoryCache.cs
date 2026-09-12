using Mazesta.Core.Inventory;
using Mazesta.Core.Providers;
using Microsoft.Extensions.Logging;

namespace Mazesta.Desktop.Composition;

/// <summary>
/// Reads the WMI inventory once per process and hands the same result to every page that asks.
/// Before this existed, each Dashboard navigation constructed a fresh view model that re-ran the
/// eight blocking WMI queries (seconds of CPU on a background thread and a fresh allocation of the
/// whole inventory graph every time the user clicked "Dashboard").
/// </summary>
public sealed class InventoryCache
{
    private readonly Lazy<Task<HardwareInventory>> _inventory;

    public InventoryCache(IInventoryProvider provider, ILogger<InventoryCache> log)
        => _inventory = new Lazy<Task<HardwareInventory>>(async () =>
        {
            var inv = await provider.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            log.LogInformation("Inventory ready: {Errors} error(s)", inv.Errors.Count);
            return inv;
        }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The inventory read. The first caller starts the WMI read; later callers await the
    /// same task, so the queries run exactly once.</summary>
    public Task<HardwareInventory> GetAsync() => _inventory.Value;

    /// <summary>True once the read has completed, so a second navigation can render immediately.</summary>
    public bool IsLoaded => _inventory.IsValueCreated && _inventory.Value.IsCompletedSuccessfully;
}
