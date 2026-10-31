using InventoryHold.Domain.Messaging;
using InventoryHold.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace InventoryHold.Domain.Services;

/// <summary>Moves events from the outbox to the broker. Delivery is at-least-once; consumers de-duplicate on EventId.</summary>
public sealed class EventRelayService(
    IOutboxRepository outbox,
    IEventPublisher publisher,
    TimeProvider clock,
    ILogger<EventRelayService> logger)
{
    /// <summary>Publishes up to <paramref name="batchSize"/> pending events in order. Returns how many were published.</summary>
    public async Task<int> RelayPendingAsync(int batchSize, CancellationToken ct)
    {
        var published = 0;
        foreach (var evt in await outbox.GetPendingAsync(batchSize, ct))
        {
            try
            {
                await publisher.PublishAsync(evt, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Stop at the first failure so events for the same hold are not published out of order.
                logger.LogWarning(ex, "Publishing {EventType} {EventId} failed; will retry", evt.EventType, evt.EventId);
                await outbox.RecordFailureAsync(evt.EventId, ex.Message, ct);
                break;
            }

            await outbox.MarkPublishedAsync(evt.EventId, clock.GetUtcNow().UtcDateTime, ct);
            published++;
        }

        return published;
    }
}
