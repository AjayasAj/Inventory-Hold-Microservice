using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

/// <summary>
/// Idempotent startup initialization: collections, indexes and seed data. Safe to run on every
/// start and from several instances at once.
/// </summary>
public sealed class MongoInitializer(MongoContext context, ILogger<MongoInitializer> logger)
{
    private static readonly TimeSpan PublishedOutboxRetention = TimeSpan.FromDays(7);

    private static readonly ProductDocument[] SeedProducts =
    [
        new() { Id = "SKU-1001", Name = "Wireless Mouse", AvailableQuantity = 50 },
        new() { Id = "SKU-1002", Name = "Mechanical Keyboard", AvailableQuantity = 25 },
        new() { Id = "SKU-1003", Name = "USB-C Hub", AvailableQuantity = 40 },
        new() { Id = "SKU-1004", Name = "Monitor Stand", AvailableQuantity = 18 },
        new() { Id = "SKU-1005", Name = "Webcam", AvailableQuantity = 30 },
        new() { Id = "SKU-1006", Name = "Noise-Cancelling Headset", AvailableQuantity = 3 }
    ];

    public async Task InitializeAsync(CancellationToken ct)
    {
        // Collections must exist before they are written inside a transaction.
        var existing = await (await context.Database.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct);
        foreach (var name in new[] { MongoContext.InventoryCollection, MongoContext.HoldsCollection, MongoContext.OutboxCollection })
        {
            if (!existing.Contains(name))
            {
                await context.Database.CreateCollectionAsync(name, cancellationToken: ct);
            }
        }

        await CreateIndexesAsync(ct);
        await SeedInventoryAsync(ct);
        logger.LogInformation("MongoDB initialized (database {Database})", context.Database.DatabaseNamespace.DatabaseName);
    }

    private async Task CreateIndexesAsync(CancellationToken ct)
    {
        var holds = context.Collection<HoldDocument>(MongoContext.HoldsCollection);
        var holdKeys = Builders<HoldDocument>.IndexKeys;
        await holds.Indexes.CreateManyAsync(
        [
            // Expiry sweep: { Status: Active, ExpiresAtUtc <= now } sorted by ExpiresAtUtc.
            new CreateIndexModel<HoldDocument>(holdKeys.Ascending(h => h.Status).Ascending(h => h.ExpiresAtUtc)),
            // Hold list filtered by status, newest first.
            new CreateIndexModel<HoldDocument>(holdKeys.Ascending(h => h.Status).Descending(h => h.CreatedAtUtc)),
            // Unfiltered hold list, newest first.
            new CreateIndexModel<HoldDocument>(holdKeys.Descending(h => h.CreatedAtUtc))
        ], ct);

        var outbox = context.Collection<OutboxDocument>(MongoContext.OutboxCollection);
        var outboxKeys = Builders<OutboxDocument>.IndexKeys;
        await outbox.Indexes.CreateManyAsync(
        [
            // Relay poll: unpublished entries in write order.
            new CreateIndexModel<OutboxDocument>(outboxKeys.Ascending(e => e.PublishedAtUtc).Ascending(e => e.CreatedAtUtc)),
            // Purge published entries after the retention window (TTL ignores null values).
            new CreateIndexModel<OutboxDocument>(
                outboxKeys.Ascending(e => e.PublishedAtUtc),
                new CreateIndexOptions { ExpireAfter = PublishedOutboxRetention })
        ], ct);
    }

    private async Task SeedInventoryAsync(CancellationToken ct)
    {
        var products = context.Collection<ProductDocument>(MongoContext.InventoryCollection);

        // $setOnInsert only: restarting never resets stock that holds have already consumed.
        var operations = SeedProducts.Select(product => new UpdateOneModel<ProductDocument>(
            Builders<ProductDocument>.Filter.Eq(existing => existing.Id, product.Id),
            Builders<ProductDocument>.Update
                .SetOnInsert(existing => existing.Name, product.Name)
                .SetOnInsert(existing => existing.AvailableQuantity, product.AvailableQuantity))
        {
            IsUpsert = true
        });

        await products.BulkWriteAsync(operations, cancellationToken: ct);
    }
}
