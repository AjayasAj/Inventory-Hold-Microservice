using InventoryHold.Domain.Messaging;
using InventoryHold.Domain.Repositories;
using InventoryHold.Infrastructure.Caching;
using InventoryHold.Infrastructure.Messaging;
using InventoryHold.Infrastructure.Mongo;
using InventoryHold.Infrastructure.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace InventoryHold.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryHoldInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<MongoOptions>(configuration, MongoOptions.SectionName,
            o => !string.IsNullOrWhiteSpace(o.ConnectionString) && !string.IsNullOrWhiteSpace(o.Database),
            "Mongo:ConnectionString and Mongo:Database are required.");
        services.AddValidatedOptions<RedisOptions>(configuration, RedisOptions.SectionName,
            o => !string.IsNullOrWhiteSpace(o.ConnectionString) && o.InventoryTtlSeconds > 0,
            "Redis:ConnectionString is required and Redis:InventoryTtlSeconds must be positive.");
        services.AddValidatedOptions<RabbitMqOptions>(configuration, RabbitMqOptions.SectionName,
            o => Uri.TryCreate(o.Uri, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(o.Exchange) && !string.IsNullOrWhiteSpace(o.Queue),
            "RabbitMq:Uri must be an absolute amqp:// URI and Exchange/Queue are required.");
        services.AddValidatedOptions<WorkerOptions>(configuration, WorkerOptions.SectionName,
            o => o.ExpirationSweepSeconds > 0 && o.ExpirationBatchSize > 0 && o.OutboxPollMilliseconds > 0 && o.OutboxBatchSize > 0,
            "Workers settings must all be positive.");

        // MongoDB
        services.AddSingleton<IMongoClient>(sp => new MongoClient(sp.GetRequiredService<MongoOptions>().ConnectionString));
        services.AddSingleton<MongoContext>();
        services.AddSingleton<MongoInitializer>();
        services.AddSingleton<IUnitOfWork, MongoUnitOfWork>();
        services.AddSingleton<IInventoryRepository, MongoInventoryRepository>();
        services.AddSingleton<IHoldRepository, MongoHoldRepository>();
        services.AddSingleton<IOutboxRepository, MongoOutboxRepository>();

        // Redis: AbortOnConnectFail=false lets the API start and keep serving (from MongoDB) while Redis is down.
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisOptions = ConfigurationOptions.Parse(sp.GetRequiredService<RedisOptions>().ConnectionString);
            redisOptions.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(redisOptions);
        });
        services.AddSingleton<IInventoryCache, RedisInventoryCache>();

        // RabbitMQ
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

        // Background processing
        services.AddHostedService<HoldExpirationWorker>();
        services.AddHostedService<OutboxRelayWorker>();

        return services;
    }

    /// <summary>Binds a section, fails fast at startup if it is invalid, and exposes the value directly for injection.</summary>
    private static void AddValidatedOptions<T>(
        this IServiceCollection services,
        IConfiguration configuration,
        string section,
        Func<T, bool> validation,
        string failureMessage) where T : class
    {
        services.AddOptions<T>()
            .Bind(configuration.GetSection(section))
            .Validate(validation, failureMessage)
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<T>>().Value);
    }
}
