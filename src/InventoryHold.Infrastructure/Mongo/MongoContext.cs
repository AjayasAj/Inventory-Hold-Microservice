using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

/// <summary>
/// Shared database handle plus the ambient transaction session. <see cref="MongoUnitOfWork"/> sets the
/// session for the duration of a unit of work and every repository call inside it enlists automatically,
/// which keeps transactions out of the domain-facing repository interfaces.
/// </summary>
public sealed class MongoContext(IMongoClient client, MongoOptions options)
{
    public const string InventoryCollection = "inventory";
    public const string HoldsCollection = "holds";
    public const string OutboxCollection = "outbox";

    private static readonly AsyncLocal<IClientSessionHandle?> AmbientSession = new();

    public IMongoClient Client { get; } = client;

    public IMongoDatabase Database { get; } = client.GetDatabase(options.Database);

    public IClientSessionHandle? Session => AmbientSession.Value;

    internal static void SetSession(IClientSessionHandle? session) => AmbientSession.Value = session;

    public IMongoCollection<T> Collection<T>(string name) => Database.GetCollection<T>(name);
}
