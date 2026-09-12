using Mazesta.Core.Inventory;
namespace Mazesta.Hardware;
public interface IInventoryProvider { Task<HardwareInventory> ReadAsync(CancellationToken ct); }
