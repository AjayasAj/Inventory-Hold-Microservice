using InventoryHold.Domain.Repositories;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

public sealed class MongoUnitOfWork(MongoContext context, MongoOptions options) : IUnitOfWork
{
    private static readonly TransactionOptions TransactionOptions =
        new(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority);

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        // Join an enclosing transaction rather than nesting.
        if (!options.UseTransactions || context.Session is not null)
        {
            return await work(ct);
        }

        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);

        // WithTransactionAsync retries the callback on TransientTransactionError (e.g. a write
        // conflict when two holds reserve the same product) and the commit on unknown results.
        return await session.WithTransactionAsync(
            async (transactionSession, token) =>
            {
                MongoContext.SetSession(transactionSession);
                try
                {
                    return await work(token);
                }
                finally
                {
                    MongoContext.SetSession(null);
                }
            },
            TransactionOptions,
            ct);
    }
}
