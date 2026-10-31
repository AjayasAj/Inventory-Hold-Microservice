using InventoryHold.Contracts;

namespace InventoryHold.Domain.Services;

public interface IHoldService
{
    Task<HoldResponse> CreateAsync(CreateHoldRequest request, CancellationToken ct);

    /// <summary>Returns the hold, expiring it first if its time has passed.</summary>
    Task<HoldResponse> GetAsync(string id, CancellationToken ct);

    Task<IReadOnlyList<HoldResponse>> ListAsync(HoldStatus? status, int limit, CancellationToken ct);

    Task<HoldResponse> ReleaseAsync(string id, CancellationToken ct);

    /// <summary>Expires up to <paramref name="batchSize"/> overdue holds. Returns how many this call expired.</summary>
    Task<int> ExpireDueHoldsAsync(int batchSize, CancellationToken ct);
}
