using RabbitMQ.Client;

namespace InventoryHold.Infrastructure.Messaging;

/// <summary>
/// One long-lived connection per process, opened lazily so the API can start (and serve reads)
/// while the broker is still unavailable. The client library recovers it automatically after drops.
/// </summary>
public sealed class RabbitMqConnection(RabbitMqOptions options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public bool IsOpen => _connection?.IsOpen ?? false;

    public async Task<IConnection> GetAsync(CancellationToken ct)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is null)
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(options.Uri),
                    ClientProvidedName = "inventory-hold-service",
                    AutomaticRecoveryEnabled = true,
                    TopologyRecoveryEnabled = true
                };
                _connection = await factory.CreateConnectionAsync(ct);
            }

            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }
}
