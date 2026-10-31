using System.Text.Json;
using InventoryHold.Contracts.Events;
using InventoryHold.Domain.Repositories;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

public sealed class MongoOutboxRepository(MongoContext context) : IOutboxRepository
{
    private static readonly FilterDefinitionBuilder<OutboxDocument> Filter = Builders<OutboxDocument>.Filter;

    private readonly IMongoCollection<OutboxDocument> _outbox =
        context.Collection<OutboxDocument>(MongoContext.OutboxCollection);

    public Task AddAsync(HoldEvent evt, CancellationToken ct)
    {
        var document = new OutboxDocument
        {
            EventId = evt.EventId,
            EventType = evt.EventType,
            HoldId = evt.HoldId,
            Payload = JsonSerializer.Serialize(evt, EventSerialization.Options),
            CreatedAtUtc = evt.OccurredAtUtc
        };

        // Enlists in the caller's transaction, so the event exists if and only if the state change committed.
        return context.Session is { } session
            ? _outbox.InsertOneAsync(session, document, cancellationToken: ct)
            : _outbox.InsertOneAsync(document, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<HoldEvent>> GetPendingAsync(int limit, CancellationToken ct)
    {
        var documents = await _outbox
            .Find(Filter.Eq(entry => entry.PublishedAtUtc, null))
            .SortBy(entry => entry.CreatedAtUtc)
            .Limit(limit)
            .ToListAsync(ct);

        return documents
            .Select(entry => JsonSerializer.Deserialize<HoldEvent>(entry.Payload, EventSerialization.Options)
                ?? throw new InvalidOperationException($"Outbox entry '{entry.EventId}' has an empty payload."))
            .ToArray();
    }

    public Task MarkPublishedAsync(string eventId, DateTime publishedAtUtc, CancellationToken ct) =>
        _outbox.UpdateOneAsync(
            Filter.Eq(entry => entry.EventId, eventId),
            Builders<OutboxDocument>.Update.Set(entry => entry.PublishedAtUtc, publishedAtUtc),
            cancellationToken: ct);

    public Task RecordFailureAsync(string eventId, string error, CancellationToken ct) =>
        _outbox.UpdateOneAsync(
            Filter.Eq(entry => entry.EventId, eventId),
            Builders<OutboxDocument>.Update
                .Inc(entry => entry.Attempts, 1)
                .Set(entry => entry.LastError, error),
            cancellationToken: ct);
}
