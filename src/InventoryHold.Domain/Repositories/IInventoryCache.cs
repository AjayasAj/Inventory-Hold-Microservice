using InventoryHold.Contracts;

namespace InventoryHold.Domain.Repositories;

/// <summary>
/// Read-through cache for the inventory listing. Implementations must degrade to calling
/// <c>factory</c> directly when the cache is unavailable rather than failing the request.
/// </summary>
public interface IInventoryCache
{
    Task<IReadOnlyList<InventoryResponse>> GetOrCreateAsync(
        Func<CancellationToken, Task<IReadOnlyList<InventoryResponse>>> factory,
        CancellationToken ct);

    /// <summary>Invalidates the cached listing after stock changes. Must not throw.</summary>
    Task InvalidateAsync(CancellationToken ct);
}
