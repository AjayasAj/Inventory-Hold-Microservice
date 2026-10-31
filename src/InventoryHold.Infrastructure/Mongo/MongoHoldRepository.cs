using InventoryHold.Contracts;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Repositories;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

public sealed class MongoHoldRepository(MongoContext context) : IHoldRepository
{
    private static readonly FilterDefinitionBuilder<HoldDocument> Filter = Builders<HoldDocument>.Filter;

    private readonly IMongoCollection<HoldDocument> _holds =
        context.Collection<HoldDocument>(MongoContext.HoldsCollection);

    public Task CreateAsync(Hold hold, CancellationToken ct)
    {
        var document = ToDocument(hold);
        return context.Session is { } session
            ? _holds.InsertOneAsync(session, document, cancellationToken: ct)
            : _holds.InsertOneAsync(document, cancellationToken: ct);
    }

    public async Task<Hold?> GetAsync(string id, CancellationToken ct)
    {
        var document = await Find(Filter.Eq(hold => hold.Id, id)).FirstOrDefaultAsync(ct);
        return document is null ? null : ToEntity(document);
    }

    public async Task<IReadOnlyList<Hold>> ListAsync(HoldStatus? status, int limit, CancellationToken ct)
    {
        var filter = status is { } value ? Filter.Eq(hold => hold.Status, value) : Filter.Empty;
        var documents = await Find(filter)
            .SortByDescending(hold => hold.CreatedAtUtc)
            .Limit(limit)
            .ToListAsync(ct);
        return documents.Select(ToEntity).ToArray();
    }

    public async Task<IReadOnlyList<Hold>> FindDueForExpiryAsync(DateTime nowUtc, int limit, CancellationToken ct)
    {
        // Served by the { Status, ExpiresAtUtc } index.
        var filter = Filter.Eq(hold => hold.Status, HoldStatus.Active) & Filter.Lte(hold => hold.ExpiresAtUtc, nowUtc);
        var documents = await Find(filter)
            .SortBy(hold => hold.ExpiresAtUtc)
            .Limit(limit)
            .ToListAsync(ct);
        return documents.Select(ToEntity).ToArray();
    }

    public async Task<bool> TryTransitionAsync(Hold updated, HoldStatus expectedStatus, CancellationToken ct)
    {
        // Compare-and-set on Status: only one caller can move a hold out of Active.
        var filter = Filter.Eq(hold => hold.Id, updated.Id) & Filter.Eq(hold => hold.Status, expectedStatus);
        var update = Builders<HoldDocument>.Update
            .Set(hold => hold.Status, updated.Status)
            .Set(hold => hold.ClosedAtUtc, updated.ClosedAtUtc);

        var result = context.Session is { } session
            ? await _holds.UpdateOneAsync(session, filter, update, cancellationToken: ct)
            : await _holds.UpdateOneAsync(filter, update, cancellationToken: ct);
        return result.ModifiedCount == 1;
    }

    private IFindFluent<HoldDocument, HoldDocument> Find(FilterDefinition<HoldDocument> filter) =>
        context.Session is { } session ? _holds.Find(session, filter) : _holds.Find(filter);

    private static HoldDocument ToDocument(Hold hold) => new()
    {
        Id = hold.Id,
        Status = hold.Status,
        CreatedAtUtc = hold.CreatedAtUtc,
        ExpiresAtUtc = hold.ExpiresAtUtc,
        ClosedAtUtc = hold.ClosedAtUtc,
        Items = hold.Lines
            .Select(line => new HoldLineDocument
            {
                ProductId = line.ProductId,
                ProductName = line.ProductName,
                Quantity = line.Quantity
            })
            .ToList()
    };

    private static Hold ToEntity(HoldDocument document) => new(
        document.Id,
        document.Status,
        document.CreatedAtUtc,
        document.ExpiresAtUtc,
        document.ClosedAtUtc,
        document.Items.Select(line => new HoldLine(line.ProductId, line.ProductName, line.Quantity)).ToArray());
}
