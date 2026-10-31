namespace InventoryHold.Infrastructure.Mongo;

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = string.Empty;

    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// Multi-document transactions need a replica set (docker-compose runs a single-node one).
    /// Disable only for a standalone mongod in local development; writes are then not atomic across documents.
    /// </summary>
    public bool UseTransactions { get; set; } = true;
}
