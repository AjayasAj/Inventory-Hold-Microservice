using InventoryHold.Contracts;
using InventoryHold.Domain.Repositories;

namespace InventoryHold.Domain.Services;

public interface IInventoryService
{
    Task<IReadOnlyList<InventoryResponse>> GetInventoryAsync(CancellationToken ct);
}

/// <summary>Inventory listing is the hottest read path (dashboard + every checkout page), so it is served read-through from cache.</summary>
public sealed class InventoryService(IInventoryRepository inventory, IInventoryCache cache) : IInventoryService
{
    public Task<IReadOnlyList<InventoryResponse>> GetInventoryAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync(LoadAsync, ct);

    private async Task<IReadOnlyList<InventoryResponse>> LoadAsync(CancellationToken ct) =>
        (await inventory.GetInventoryAsync(ct))
            .OrderBy(product => product.Id, StringComparer.Ordinal)
            .Select(product => new InventoryResponse(product.Id, product.Name, product.AvailableQuantity))
            .ToArray();
}
