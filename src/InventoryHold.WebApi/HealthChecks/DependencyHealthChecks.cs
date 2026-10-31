using InventoryHold.Infrastructure.Messaging;
using InventoryHold.Infrastructure.Mongo;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;
using StackExchange.Redis;

namespace InventoryHold.WebApi.HealthChecks;

public sealed class MongoHealthCheck(MongoContext mongo) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await mongo.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MongoDB is unreachable.", ex);
        }
    }
}

/// <summary>Redis is an optimization: when it is down the API still works, so it reports Degraded, not Unhealthy.</summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Redis is unreachable; inventory is served uncached.", ex);
        }
    }
}

/// <summary>Events queue in the outbox while the broker is down, so an outage degrades rather than fails the service.</summary>
public sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var conn = await connection.GetAsync(ct);
            return conn.IsOpen
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Degraded("RabbitMQ connection is recovering; events are buffered in the outbox.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("RabbitMQ is unreachable; events are buffered in the outbox.", ex);
        }
    }
}
