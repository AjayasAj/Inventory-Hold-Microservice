using InventoryHold.Contracts.Events;

namespace InventoryHold.Domain.Messaging;

/// <summary>Publishes integration events to the message broker. Completes only once the broker has confirmed receipt.</summary>
public interface IEventPublisher
{
    Task PublishAsync(HoldEvent evt, CancellationToken ct);
}
