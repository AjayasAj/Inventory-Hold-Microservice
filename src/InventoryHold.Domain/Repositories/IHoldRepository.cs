using InventoryHold.Contracts;
using InventoryHold.Domain.Entities;

namespace InventoryHold.Domain.Repositories;

public interface IHoldRepository
{
    Task CreateAsync(Hold hold, CancellationToken ct);

    Task<Hold?> GetAsync(string id, CancellationToken ct);

    /// <summary>Newest first, optionally filtered by status.</summary>
    Task<IReadOnlyList<Hold>> ListAsync(HoldStatus? status, int limit, CancellationToken ct);

    /// <summary>Active holds whose expiry time is at or before <paramref name="nowUtc"/>, oldest first.</summary>
    Task<IReadOnlyList<Hold>> FindDueForExpiryAsync(DateTime nowUtc, int limit, CancellationToken ct);

    /// <summary>
    /// Atomically applies <paramref name="updated"/>'s status only if the stored hold is still in
    /// <paramref name="expectedStatus"/>. Returns false if another caller transitioned it first.
    /// </summary>
    Task<bool> TryTransitionAsync(Hold updated, HoldStatus expectedStatus, CancellationToken ct);
}
