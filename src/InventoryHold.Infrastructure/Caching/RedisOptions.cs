namespace InventoryHold.Infrastructure.Caching;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; set; } = string.Empty;

    public string KeyPrefix { get; set; } = "inventory-hold";

    /// <summary>
    /// Safety-net TTL. Correctness comes from invalidation on every stock change; the TTL only bounds
    /// staleness if an invalidation is ever missed (e.g. Redis briefly unreachable).
    /// </summary>
    public int InventoryTtlSeconds { get; set; } = 30;
}
