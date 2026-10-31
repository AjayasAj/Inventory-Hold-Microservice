using System.Text.Json;
using InventoryHold.Contracts.Events;
using InventoryHold.Domain.Messaging;
using RabbitMQ.Client;

namespace InventoryHold.Infrastructure.Messaging;

/// <summary>
/// Publishes on a single long-lived channel with publisher confirms: <see cref="PublishAsync"/> only
/// completes once the broker has persisted the message, so the outbox entry is marked published only
/// when delivery is guaranteed. Topology is declared once per channel, not per message.
/// </summary>
public sealed class RabbitMqEventPublisher(RabbitMqConnection connection, RabbitMqOptions options)
    : IEventPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(HoldEvent evt, CancellationToken ct)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(evt, EventSerialization.Options);
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = evt.EventId,
            CorrelationId = evt.HoldId,
            Type = evt.EventType,
            Timestamp = new AmqpTimestamp(new DateTimeOffset(evt.OccurredAtUtc).ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?> { ["schema-version"] = evt.SchemaVersion }
        };

        await _gate.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);
            await channel.BasicPublishAsync(
                options.Exchange,
                HoldEventTypes.RoutingKeyFor(evt.EventType),
                mandatory: true,
                properties,
                body,
                ct);
        }
        catch
        {
            // Discard a possibly broken channel; the next publish opens a fresh one.
            await ResetChannelAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await ResetChannelAsync();
        var conn = await connection.GetAsync(ct);
        var channel = await conn.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);

        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(options.Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(options.Queue, options.Exchange, "hold.#", cancellationToken: ct);

        _channel = channel;
        return channel;
    }

    private async Task ResetChannelAsync()
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            await _channel.DisposeAsync();
        }
        catch
        {
            // Already broken; nothing more to release.
        }

        _channel = null;
    }

    public async ValueTask DisposeAsync()
    {
        await ResetChannelAsync();
        _gate.Dispose();
    }
}
