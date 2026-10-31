using InventoryHold.Contracts;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace InventoryHold.Infrastructure.Mongo;

/// <summary>One document per product; <c>AvailableQuantity</c> is mutated only with conditional <c>$inc</c>.</summary>
[BsonIgnoreExtraElements]
public sealed class ProductDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int AvailableQuantity { get; set; }
}

/// <summary>
/// A hold embeds its lines: they are always read and written with the hold and are immutable after
/// creation, so embedding gives single-document reads and no joins.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class HoldDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.String)]
    public HoldStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    [BsonIgnoreIfNull]
    public DateTime? ClosedAtUtc { get; set; }

    public List<HoldLineDocument> Items { get; set; } = [];
}

[BsonIgnoreExtraElements]
public sealed class HoldLineDocument
{
    public string ProductId { get; set; } = string.Empty;

    [BsonElement("Name")]
    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

/// <summary>
/// Outbox entry. The payload is the exact JSON that will be published, so the relay never needs
/// to re-serialize and what was committed is what consumers receive.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class OutboxDocument
{
    [BsonId]
    public string EventId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string HoldId { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Null until published. A TTL index removes published entries after a retention period.</summary>
    public DateTime? PublishedAtUtc { get; set; }

    public int Attempts { get; set; }

    [BsonIgnoreIfNull]
    public string? LastError { get; set; }
}
