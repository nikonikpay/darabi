using Mazesta.Core.Inventory;
namespace Mazesta.Core.Providers;
public interface IInventoryProvider { Task<HardwareInventory> ReadAsync(CancellationToken ct); }
