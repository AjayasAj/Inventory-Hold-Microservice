using System.Text.Json;
using InventoryHold.Contracts;
using InventoryHold.Domain.Repositories;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace InventoryHold.Infrastructure.Caching;

/// <summary>
/// Version-keyed read-through cache. Invalidation bumps a version counter instead of deleting a key,
/// and readers store their result under the version they observed <i>before</i> loading from MongoDB.
/// A slow reader that loaded pre-mutation data can therefore only write to a version nobody reads
/// any more — the classic "stale set after delete" race cannot repopulate the cache with old stock.
/// Redis failures degrade to reading MongoDB directly; they never fail the request.
/// </summary>
public sealed class RedisInventoryCache(
    IConnectionMultiplexer redis,
    RedisOptions options,
    ILogger<RedisInventoryCache> logger) : IInventoryCache
{
    private readonly string _versionKey = $"{options.KeyPrefix}:inventory:version";
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(options.InventoryTtlSeconds);

    public async Task<IReadOnlyList<InventoryResponse>> GetOrCreateAsync(
        Func<CancellationToken, Task<IReadOnlyList<InventoryResponse>>> factory,
        CancellationToken ct)
    {
        string? dataKey = null;
        try
        {
            var database = redis.GetDatabase();
            var version = (long?)await database.StringGetAsync(_versionKey) ?? 0;
            dataKey = $"{options.KeyPrefix}:inventory:v{version}";

            var cached = await database.StringGetAsync(dataKey);
            if (cached.HasValue)
            {
                return JsonSerializer.Deserialize<InventoryResponse[]>(cached.ToString())!;
            }
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            logger.LogWarning(ex, "Redis unavailable; serving inventory from MongoDB");
            return await factory(ct);
        }

        var inventory = await factory(ct);

        try
        {
            await redis.GetDatabase().StringSetAsync(dataKey, JsonSerializer.Serialize(inventory), _ttl);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            logger.LogWarning(ex, "Failed to cache inventory");
        }

        return inventory;
    }

    public async Task InvalidateAsync(CancellationToken ct)
    {
        try
        {
            await redis.GetDatabase().StringIncrementAsync(_versionKey);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            // The TTL bounds how long the stale listing can be served.
            logger.LogWarning(ex, "Failed to invalidate inventory cache; entry will expire within {Ttl}", _ttl);
        }
    }

    private static bool IsRedisFailure(Exception ex) => ex is RedisException or TimeoutException;
}
