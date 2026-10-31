using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Repositories;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

public sealed class MongoInventoryRepository(MongoContext context) : IInventoryRepository
{
    private readonly IMongoCollection<ProductDocument> _products =
        context.Collection<ProductDocument>(MongoContext.InventoryCollection);

    public async Task<IReadOnlyList<Product>> GetInventoryAsync(CancellationToken ct)
    {
        var documents = await Find(FilterDefinition<ProductDocument>.Empty)
            .SortBy(product => product.Id)
            .ToListAsync(ct);
        return documents.Select(Map).ToArray();
    }

    public async Task<Product?> GetProductAsync(string id, CancellationToken ct)
    {
        var document = await Find(Builders<ProductDocument>.Filter.Eq(product => product.Id, id))
            .FirstOrDefaultAsync(ct);
        return document is null ? null : Map(document);
    }

    public async Task<Product?> TryReserveAsync(string id, int quantity, CancellationToken ct)
    {
        // The stock check and the decrement are one atomic operation: the filter only matches when
        // enough stock remains, so concurrent reservations can never drive stock negative.
        var filter = Builders<ProductDocument>.Filter.Eq(product => product.Id, id)
            & Builders<ProductDocument>.Filter.Gte(product => product.AvailableQuantity, quantity);
        var update = Builders<ProductDocument>.Update.Inc(product => product.AvailableQuantity, -quantity);
        var options = new FindOneAndUpdateOptions<ProductDocument> { ReturnDocument = ReturnDocument.After };

        var document = context.Session is { } session
            ? await _products.FindOneAndUpdateAsync(session, filter, update, options, ct)
            : await _products.FindOneAndUpdateAsync(filter, update, options, ct);
        return document is null ? null : Map(document);
    }

    public async Task RestockAsync(string id, int quantity, CancellationToken ct)
    {
        var filter = Builders<ProductDocument>.Filter.Eq(product => product.Id, id);
        var update = Builders<ProductDocument>.Update.Inc(product => product.AvailableQuantity, quantity);

        var result = context.Session is { } session
            ? await _products.UpdateOneAsync(session, filter, update, cancellationToken: ct)
            : await _products.UpdateOneAsync(filter, update, cancellationToken: ct);

        if (result.MatchedCount == 0)
        {
            // Aborts the surrounding transaction so the hold is not closed without returning its stock.
            throw new InvalidOperationException($"Cannot restock missing product '{id}'.");
        }
    }

    private IFindFluent<ProductDocument, ProductDocument> Find(FilterDefinition<ProductDocument> filter) =>
        context.Session is { } session ? _products.Find(session, filter) : _products.Find(filter);

    private static Product Map(ProductDocument document) =>
        new(document.Id, document.Name, document.AvailableQuantity);
}
