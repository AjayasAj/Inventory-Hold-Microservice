using InventoryHold.Contracts.Events;

namespace InventoryHold.Domain.Repositories;

/// <summary>
/// Transactional outbox. Events are written in the same unit of work as the state change that
/// produced them, then relayed to the broker asynchronously (at-least-once delivery).
/// </summary>
public interface IOutboxRepository
{
    Task AddAsync(HoldEvent evt, CancellationToken ct);

    /// <summary>Unpublished events in the order they were written.</summary>
    Task<IReadOnlyList<HoldEvent>> GetPendingAsync(int limit, CancellationToken ct);

    Task MarkPublishedAsync(string eventId, DateTime publishedAtUtc, CancellationToken ct);

    Task RecordFailureAsync(string eventId, string error, CancellationToken ct);
}
