using InventoryHold.Domain.Entities;

namespace InventoryHold.Domain.Repositories;

public interface IInventoryRepository
{
    Task<IReadOnlyList<Product>> GetInventoryAsync(CancellationToken ct);

    Task<Product?> GetProductAsync(string id, CancellationToken ct);

    /// <summary>
    /// Atomically decrements stock only if at least <paramref name="quantity"/> is available.
    /// Returns the product after the decrement, or null if it does not exist or stock is insufficient.
    /// </summary>
    Task<Product?> TryReserveAsync(string id, int quantity, CancellationToken ct);

    /// <summary>Atomically returns <paramref name="quantity"/> units to stock.</summary>
    Task RestockAsync(string id, int quantity, CancellationToken ct);
}
