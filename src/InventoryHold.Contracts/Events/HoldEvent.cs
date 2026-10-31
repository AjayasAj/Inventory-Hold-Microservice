namespace InventoryHold.Contracts.Events;

/// <summary>
/// Integration event published to RabbitMQ for every hold lifecycle transition.
/// Carries the full hold snapshot so consumers never need to call back into this service.
/// </summary>
/// <param name="EventId">Unique id; also sent as the AMQP message-id so consumers can de-duplicate.</param>
/// <param name="EventType">One of <see cref="HoldEventTypes"/>.</param>
/// <param name="SchemaVersion">Incremented on breaking payload changes.</param>
public sealed record HoldEvent(
    string EventId,
    string EventType,
    int SchemaVersion,
    DateTime OccurredAtUtc,
    string HoldId,
    HoldStatus Status,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    IReadOnlyList<HoldItem> Items);
